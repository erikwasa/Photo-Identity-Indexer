using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSmartCollectionOrientationTests
{
    [Fact]
    public async Task Orientation_uses_proxy_first_visual_geometry_and_round_trips_saved_definitions()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_smart_orientation_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };

            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            Assert.Null(initialization.Error);

            AssetRevisionId catalogueLandscape = AssetRevisionId.New();
            AssetRevisionId cataloguePortrait = AssetRevisionId.New();
            AssetRevisionId square = AssetRevisionId.New();
            AssetRevisionId unknown = AssetRevisionId.New();
            AssetRevisionId proxyPortrait = AssetRevisionId.New();
            AssetRevisionId proxyLandscape = AssetRevisionId.New();
            await SeedAsync(
                database,
                catalogueLandscape,
                cataloguePortrait,
                square,
                unknown,
                proxyPortrait,
                proxyLandscape);

            PostgresSmartCollectionRepository definitions = new(database, TimeProvider.System);
            PostgresSmartCollectionQueryRepository query = new(database, definitions, TimeProvider.System);

            SmartCollectionPhotoPage landscape = await query.QueryAsync(
                new SmartCollectionFilter(orientation: SmartCollectionOrientations.Landscape),
                limit: 20);
            Assert.Equal(2, landscape.Total);
            Assert.Contains(landscape.Items, item => item.RevisionId == catalogueLandscape);
            Assert.Contains(landscape.Items, item => item.RevisionId == proxyLandscape);
            Assert.DoesNotContain(landscape.Items, item => item.RevisionId == proxyPortrait);
            Assert.DoesNotContain(landscape.Items, item => item.RevisionId == square);
            Assert.DoesNotContain(landscape.Items, item => item.RevisionId == unknown);

            SmartCollectionPhotoPage portrait = await query.QueryAsync(
                new SmartCollectionFilter(orientation: SmartCollectionOrientations.Portrait),
                limit: 20);
            Assert.Equal(2, portrait.Total);
            Assert.Contains(portrait.Items, item => item.RevisionId == cataloguePortrait);
            Assert.Contains(portrait.Items, item => item.RevisionId == proxyPortrait);
            Assert.DoesNotContain(portrait.Items, item => item.RevisionId == proxyLandscape);
            Assert.DoesNotContain(portrait.Items, item => item.RevisionId == square);
            Assert.DoesNotContain(portrait.Items, item => item.RevisionId == unknown);

            SmartCollectionPhotoPage any = await query.QueryAsync(new SmartCollectionFilter(), limit: 20);
            Assert.Equal(6, any.Total);

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Portrait archive",
                new SmartCollectionFilter(orientation: SmartCollectionOrientations.Portrait));
            SmartCollectionDefinition reopened =
                await definitions.GetAsync(saved.Id) ?? throw new InvalidOperationException();
            Assert.Equal(SmartCollectionOrientations.Portrait, reopened.Filter.Orientation);

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand persisted = connection.CreateCommand();
                persisted.CommandText = """
                    SELECT filter_schema_version, filter_json ->> 'orientation'
                    FROM smart_collections
                    WHERE id = @id;
                    """;
                persisted.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, saved.Id.Value);
                await using NpgsqlDataReader reader = await persisted.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(3, reader.GetInt32(0));
                Assert.Equal("portrait", reader.GetString(1));
            }

            SmartCollectionDefinition legacy = await definitions.CreateAsync(
                "Legacy any",
                new SmartCollectionFilter());
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand removeOrientation = connection.CreateCommand();
                removeOrientation.CommandText = """
                    UPDATE smart_collections
                    SET filter_json = filter_json - 'orientation'
                    WHERE id = @id;
                    """;
                removeOrientation.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, legacy.Id.Value);
                Assert.Equal(1, await removeOrientation.ExecuteNonQueryAsync());
            }

            SmartCollectionDefinition reopenedLegacy =
                await definitions.GetAsync(legacy.Id) ?? throw new InvalidOperationException();
            Assert.Equal(SmartCollectionOrientations.Any, reopenedLegacy.Filter.Orientation);

            SmartCollectionSlideshowSnapshot snapshot =
                await query.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal(2, snapshot.RevisionIds.Count);
            Assert.Contains(cataloguePortrait, snapshot.RevisionIds);
            Assert.Contains(proxyPortrait, snapshot.RevisionIds);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAsync(
        PostgresCatalogueDatabase database,
        AssetRevisionId catalogueLandscape,
        AssetRevisionId cataloguePortrait,
        AssetRevisionId square,
        AssetRevisionId unknown,
        AssetRevisionId proxyPortrait,
        AssetRevisionId proxyLandscape)
    {
        Guid source = Guid.NewGuid();
        Guid[] assets = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        DateTimeOffset now = new(2026, 10, 3, 1, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'test', @root, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES
                (@asset1, @source, 'catalogue-landscape.jpg', @now),
                (@asset2, @source, 'catalogue-portrait.jpg', @now),
                (@asset3, @source, 'square.jpg', @now),
                (@asset4, @source, 'unknown.jpg', @now),
                (@asset5, @source, 'proxy-portrait-exif.jpg', @now),
                (@asset6, @source, 'proxy-landscape-fallback.jpg', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES
                (@revision1, @asset1, @hash1, 123, @now, 'image/jpeg', 1600, 900),
                (@revision2, @asset2, @hash2, 123, @now, 'image/jpeg', 900, 1600),
                (@revision3, @asset3, @hash3, 123, @now, 'image/jpeg', 1000, 1000),
                (@revision4, @asset4, @hash4, 123, @now, 'image/jpeg', NULL, NULL),
                (@revision5, @asset5, @hash5, 123, @now, 'image/jpeg', 1600, 900),
                (@revision6, @asset6, @hash6, 123, @now, 'image/jpeg', NULL, NULL);

            INSERT INTO archive_review_proxy_profiles (
                profile_id, protocol_version, encoder, format, jpeg_quality,
                maximum_long_edge, resize_policy, canonical_definition, recorded_at_utc)
            VALUES (
                'orientation-test', 'v1', 'test', 'jpeg', 85,
                1600, 'fit', 'orientation test proxy', @now);

            INSERT INTO asset_revision_review_proxies (
                asset_revision_id, profile_id, encoded_byte_length, content_sha256,
                width, height, generated_at_utc, relative_path)
            VALUES
                (@revision5, 'orientation-test', 100, @proxyHash1, 675, 1200, @now, 'review/orientation/proxy-portrait.jpg'),
                (@revision6, 'orientation-test', 100, @proxyHash2, 1200, 675, @now, 'review/orientation/proxy-landscape.jpg');
            """;
        command.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, source);
        command.Parameters.AddWithValue("root", NpgsqlDbType.Text, $"smart-orientation-{source:N}");
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);

        for (int index = 0; index < assets.Length; index++)
        {
            command.Parameters.AddWithValue($"asset{index + 1}", NpgsqlDbType.Uuid, assets[index]);
            command.Parameters.AddWithValue($"hash{index + 1}", NpgsqlDbType.Text, new string((char)('1' + index), 64));
        }

        AssetRevisionId[] revisions =
            [catalogueLandscape, cataloguePortrait, square, unknown, proxyPortrait, proxyLandscape];
        for (int index = 0; index < revisions.Length; index++)
        {
            command.Parameters.AddWithValue(
                $"revision{index + 1}",
                NpgsqlDbType.Uuid,
                revisions[index].Value);
        }

        command.Parameters.AddWithValue("proxyHash1", NpgsqlDbType.Text, new string('a', 64));
        command.Parameters.AddWithValue("proxyHash2", NpgsqlDbType.Text, new string('b', 64));
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
