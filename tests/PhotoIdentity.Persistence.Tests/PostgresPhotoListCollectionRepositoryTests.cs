using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoListCollectionRepositoryTests
{
    [Fact]
    public async Task Repository_preserves_order_and_omits_unavailable_items_from_snapshot()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_photo_list_collections_{Guid.NewGuid():N}";
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
            Assert.Equal(31, initialization.Health.SchemaVersion);

            AssetRevisionId first = AssetRevisionId.New();
            AssetRevisionId second = AssetRevisionId.New();
            AssetRevisionId third = AssetRevisionId.New();
            Guid firstAsset = Guid.NewGuid();
            await SeedAsync(
                database,
                firstAsset,
                first,
                Guid.NewGuid(),
                second,
                Guid.NewGuid(),
                third);

            PostgresPhotoListCollectionRepository repository =
                new(database, TimeProvider.System);

            PhotoListCollectionDefinition created = await repository.CreateAsync(
                "  Family   picks  ",
                [third, first, second]);
            Assert.Equal("Family picks", created.Name);
            Assert.Equal([third, first, second], created.RevisionIds);

            PhotoListCollectionDefinition listed =
                Assert.Single(await repository.ListAsync());
            Assert.Equal(created.Id, listed.Id);
            Assert.Equal([third, first, second], listed.RevisionIds);

            PhotoListCollectionSlideshowSnapshot initialSnapshot =
                await repository.CreateSlideshowSnapshotAsync(created.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal([third, first, second], initialSnapshot.RevisionIds);

            await Assert.ThrowsAsync<PhotoListCollectionNameConflictException>(
                () => repository.CreateAsync("family picks", []));

            await using (NpgsqlConnection connection =
                         await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand markMissing = connection.CreateCommand();
                markMissing.CommandText = """
                    UPDATE assets
                    SET deleted_at_utc = @now
                    WHERE id = @asset_id;
                    """;
                markMissing.Parameters.AddWithValue(
                    "now",
                    NpgsqlDbType.TimestampTz,
                    DateTimeOffset.UtcNow);
                markMissing.Parameters.AddWithValue(
                    "asset_id",
                    NpgsqlDbType.Uuid,
                    firstAsset);
                Assert.Equal(1, await markMissing.ExecuteNonQueryAsync());
            }

            PhotoListCollectionDefinition reopened =
                await repository.GetAsync(created.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal([third, first, second], reopened.RevisionIds);

            PhotoListCollectionSlideshowSnapshot safeSnapshot =
                await repository.CreateSlideshowSnapshotAsync(created.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal([third, second], safeSnapshot.RevisionIds);

            await Assert.ThrowsAsync<PhotoListCollectionRevisionUnavailableException>(
                () => repository.UpdateAsync(
                    created.Id,
                    "Family picks",
                    [first, second]));

            PhotoListCollectionDefinition updated =
                await repository.UpdateAsync(
                    created.Id,
                    "Weekend picks",
                    [second, third])
                ?? throw new InvalidOperationException();
            Assert.Equal("Weekend picks", updated.Name);
            Assert.Equal([second, third], updated.RevisionIds);
            Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);

            PhotoListCollectionSlideshowSnapshot reorderedSnapshot =
                await repository.CreateSlideshowSnapshotAsync(created.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal([second, third], reorderedSnapshot.RevisionIds);

            await Assert.ThrowsAsync<PhotoListCollectionRevisionUnavailableException>(
                () => repository.CreateAsync(
                    "Missing revision",
                    [AssetRevisionId.New()]));

            await using (NpgsqlConnection connection =
                         await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand smartCollectionCount = connection.CreateCommand();
                smartCollectionCount.CommandText = "SELECT COUNT(*) FROM smart_collections;";
                Assert.Equal(
                    0L,
                    Convert.ToInt64(await smartCollectionCount.ExecuteScalarAsync()));
            }

            Assert.True(await repository.DeleteAsync(created.Id));
            Assert.False(await repository.DeleteAsync(created.Id));
            Assert.Null(await repository.GetAsync(created.Id));
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
        Guid firstAsset,
        AssetRevisionId first,
        Guid secondAsset,
        AssetRevisionId second,
        Guid thirdAsset,
        AssetRevisionId third)
    {
        Guid source = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'local-folder', @root, @now);

            INSERT INTO assets (
                id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES
                (@asset1, @source, 'first.jpg', @now, @now),
                (@asset2, @source, 'second.jpg', @now, @now),
                (@asset3, @source, 'third.jpg', @now, @now);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES
                (@revision1, @asset1, repeat('1', 64), 100, @now, 'image/jpeg', 100, 100),
                (@revision2, @asset2, repeat('2', 64), 100, @now, 'image/jpeg', 100, 100),
                (@revision3, @asset3, repeat('3', 64), 100, @now, 'image/jpeg', 100, 100);
            """;
        command.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, source);
        command.Parameters.AddWithValue("root", NpgsqlDbType.Text, $"photo-list-test-{source:N}");
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("asset1", NpgsqlDbType.Uuid, firstAsset);
        command.Parameters.AddWithValue("asset2", NpgsqlDbType.Uuid, secondAsset);
        command.Parameters.AddWithValue("asset3", NpgsqlDbType.Uuid, thirdAsset);
        command.Parameters.AddWithValue("revision1", NpgsqlDbType.Uuid, first.Value);
        command.Parameters.AddWithValue("revision2", NpgsqlDbType.Uuid, second.Value);
        command.Parameters.AddWithValue("revision3", NpgsqlDbType.Uuid, third.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        """ + identifier.Replace(""", """", StringComparison.Ordinal) + """;
}
