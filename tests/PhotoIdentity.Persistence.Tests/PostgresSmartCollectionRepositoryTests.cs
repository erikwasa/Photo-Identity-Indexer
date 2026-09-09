using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSmartCollectionRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsDefinitionsAndEnforcesUniqueNames_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_smart_collections_{Guid.NewGuid():N}";
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
            Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion, initialization.Health.SchemaVersion);

            PostgresSmartCollectionRepository repository = new(database, TimeProvider.System);
            PersonId firstPerson = PersonId.New();
            PersonId secondPerson = PersonId.New();

            SmartCollectionDefinition created = await repository.CreateAsync(
                "  Summer\t 2025  ",
                new SmartCollectionFilter(
                    people: [secondPerson, firstPerson],
                    peopleMatch: SmartCollectionMatchModes.Any,
                    tags: [" Trips / Italy ", " Family "],
                    tagMatch: SmartCollectionMatchModes.All,
                    location: new SmartCollectionGeoBounds(40, 10, 44, 15),
                    taken: SmartCollectionDateRange.Parse("2025/05/01-2025/05/10"),
                    locationPlace: "Sweden/Stockholm"));

            Assert.Equal("Summer 2025", created.Name);
            Assert.Equal(SmartCollectionMatchModes.Any, created.Filter.PeopleMatch);
            Assert.Equal(SmartCollectionMatchModes.All, created.Filter.TagMatch);
            Assert.Equal(["family", "trips/italy"], created.Filter.Tags);
            Assert.Equal("places/sweden/stockholm", created.Filter.LocationPlace);
            Assert.Equal(new DateOnly(2025, 5, 1), created.Filter.Taken?.From);
            Assert.Equal(new DateOnly(2025, 5, 10), created.Filter.Taken?.To);

            SmartCollectionDefinition listed = Assert.Single(await repository.ListAsync());
            Assert.Equal(created.Id, listed.Id);
            SmartCollectionDefinition? reopened = await repository.GetAsync(created.Id);
            Assert.NotNull(reopened);
            Assert.Equal(created.Filter.Tags, reopened.Filter.Tags);
            Assert.Equal(
                created.Filter.People.Select(person => person.ToString()),
                reopened.Filter.People.Select(person => person.ToString()));
            Assert.Equal(created.Filter.LocationPlace, reopened.Filter.LocationPlace);

            await Assert.ThrowsAsync<SmartCollectionNameConflictException>(() => repository.CreateAsync(
                "summer 2025",
                new SmartCollectionFilter()));

            SmartCollectionDefinition? updated = await repository.UpdateAsync(
                created.Id,
                " Italy archive ",
                new SmartCollectionFilter(
                    tags: ["Trips/Italy"],
                    tagMatch: SmartCollectionMatchModes.Any,
                    taken: SmartCollectionDateRange.Parse("2020-2021")));
            Assert.NotNull(updated);
            Assert.Equal("Italy archive", updated.Name);
            Assert.Equal(["trips/italy"], updated.Filter.Tags);
            Assert.Equal(new DateOnly(2020, 1, 1), updated.Filter.Taken?.From);
            Assert.Equal(new DateOnly(2021, 12, 31), updated.Filter.Taken?.To);
            Assert.True((created.CreatedAtUtc - updated.CreatedAtUtc).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.True(updated.UpdatedAtUtc >= created.UpdatedAtUtc);

            Assert.Null(await repository.UpdateAsync(
                SmartCollectionId.New(),
                "Missing",
                new SmartCollectionFilter()));
            Assert.True(await repository.DeleteAsync(created.Id));
            Assert.False(await repository.DeleteAsync(created.Id));
            Assert.Null(await repository.GetAsync(created.Id));

            await using NpgsqlConnection verificationConnection = new(testBuilder.ConnectionString);
            await verificationConnection.OpenAsync();
            await using NpgsqlCommand countDefinitions = verificationConnection.CreateCommand();
            countDefinitions.CommandText = "SELECT COUNT(*) FROM smart_collections;";
            Assert.Equal(0L, Convert.ToInt64(await countDefinitions.ExecuteScalarAsync()));
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task QueryAndSnapshot_EvaluateCurrentCatalogue_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_smart_collection_query_{Guid.NewGuid():N}";
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

            PersonId alice = PersonId.New();
            AssetRevisionId matching = AssetRevisionId.New();
            AssetRevisionId wrongLocation = AssetRevisionId.New();
            await SeedQueryRevisionAsync(
                testBuilder.ConnectionString,
                matching,
                alice,
                sourceKey: "smart-query/matching.jpg",
                contentHash: new string('a', 64),
                tag: "trips/italy",
                place: "places/sweden/stockholm",
                latitude: 41.9028,
                longitude: 12.4964,
                takenAtLocal: new DateTime(2025, 5, 5, 14, 30, 0, DateTimeKind.Unspecified));
            await SeedQueryRevisionAsync(
                testBuilder.ConnectionString,
                wrongLocation,
                alice,
                sourceKey: "smart-query/wrong-location.jpg",
                contentHash: new string('b', 64),
                tag: "trips/italy",
                place: "places/norway/oslo",
                latitude: 59.3293,
                longitude: 18.0686,
                takenAtLocal: new DateTime(2025, 5, 5, 14, 30, 0, DateTimeKind.Unspecified));

            PostgresSmartCollectionRepository definitions = new(database, TimeProvider.System);
            PostgresSmartCollectionQueryRepository query = new(database, definitions, TimeProvider.System);
            SmartCollectionFilter filter = new(
                people: [alice],
                peopleMatch: SmartCollectionMatchModes.All,
                tags: ["Trips/Italy"],
                tagMatch: SmartCollectionMatchModes.All,
                location: new SmartCollectionGeoBounds(40, 10, 44, 15),
                taken: SmartCollectionDateRange.Parse("2025/05/01-2025/05/10"),
                locationPlace: "Sweden");

            SmartCollectionPhotoPage result = await query.QueryAsync(filter);

            SmartCollectionPhoto item = Assert.Single(result.Items);
            Assert.Equal(matching, item.RevisionId);
            Assert.Equal(1, result.Total);
            Assert.Equal(new DateTime(2025, 5, 5, 14, 30, 0), item.TakenAtLocal);
            Assert.Equal(41.9028, item.Latitude);
            Assert.Equal(12.4964, item.Longitude);

            SmartCollectionDefinition saved = await definitions.CreateAsync("Sweden trip", filter);
            SmartCollectionSlideshowSnapshot snapshot =
                await query.CreateSlideshowSnapshotAsync(saved.Id)
                ?? throw new InvalidOperationException();
            Assert.Equal(saved.Id, snapshot.CollectionId);
            Assert.Equal("Sweden trip", snapshot.CollectionName);
            Assert.Equal(matching, Assert.Single(snapshot.RevisionIds));
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedQueryRevisionAsync(
        string connectionString,
        AssetRevisionId revisionId,
        PersonId personId,
        string sourceKey,
        string contentHash,
        string tag,
        string place,
        double latitude,
        double longitude,
        DateTime takenAtLocal)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', @root_locator, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, @source_key, @now);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES (
                @revision_id,
                @asset_id,
                @content_sha256,
                123,
                @now,
                'image/jpeg',
                1024,
                768);

            INSERT INTO people (id, display_name, created_at_utc)
            VALUES (@person_id, 'Alice', @now)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO photo_person_actions (asset_revision_id, person_id, action_kind, actor, created_at_utc)
            VALUES (@revision_id, @person_id, 'add', 'test', @now);

            INSERT INTO photo_tags (normalized_name, display_name, created_by, created_at_utc)
            VALUES (@tag, @tag_display, 'test', @now)
            ON CONFLICT (normalized_name) DO UPDATE SET display_name = EXCLUDED.display_name
            RETURNING id;
            """;
        seed.Parameters.AddWithValue("source_id", NpgsqlDbType.Uuid, sourceId);
        seed.Parameters.AddWithValue("root_locator", NpgsqlDbType.Text, $"smart-query-root-{sourceId:N}");
        seed.Parameters.AddWithValue("asset_id", NpgsqlDbType.Uuid, assetId);
        seed.Parameters.AddWithValue("source_key", NpgsqlDbType.Text, sourceKey);
        seed.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        seed.Parameters.AddWithValue("content_sha256", NpgsqlDbType.Text, contentHash);
        seed.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        seed.Parameters.AddWithValue("tag", NpgsqlDbType.Text, tag);
        seed.Parameters.AddWithValue("tag_display", NpgsqlDbType.Text, tag);
        seed.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        long tagId = Convert.ToInt64(await seed.ExecuteScalarAsync());

        await using NpgsqlCommand seedRemainder = connection.CreateCommand();
        seedRemainder.CommandText =
            """
            INSERT INTO photo_tag_actions (asset_revision_id, tag_id, action_kind, actor, created_at_utc)
            VALUES (@revision_id, @tag_id, 'add', 'test', @now);

            INSERT INTO photo_tags (normalized_name, display_name, created_by, created_at_utc)
            VALUES ('places', 'Places', 'test', @now)
            ON CONFLICT (normalized_name) DO NOTHING;

            INSERT INTO photo_tags (normalized_name, display_name, created_by, created_at_utc)
            VALUES (@place, @place_display, 'test', @now)
            ON CONFLICT (normalized_name) DO UPDATE SET display_name = EXCLUDED.display_name
            RETURNING id;
            """;
        seedRemainder.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        seedRemainder.Parameters.AddWithValue("tag_id", NpgsqlDbType.Bigint, tagId);
        seedRemainder.Parameters.AddWithValue("place", NpgsqlDbType.Text, place);
        seedRemainder.Parameters.AddWithValue("place_display", NpgsqlDbType.Text, place);
        seedRemainder.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        long placeTagId = Convert.ToInt64(await seedRemainder.ExecuteScalarAsync());

        await using NpgsqlCommand seedMetadata = connection.CreateCommand();
        seedMetadata.CommandText =
            """
            INSERT INTO photo_place_actions (
                asset_revision_id, tag_id, action_kind, source_kind, provider, actor, created_at_utc)
            VALUES (@revision_id, @place_tag_id, 'set', 'manual', NULL, 'test', @now);

            INSERT INTO photo_capture_metadata (
                asset_revision_id,
                taken_at_local,
                utc_offset_minutes,
                latitude,
                longitude,
                extracted_at_utc)
            VALUES (
                @revision_id,
                @taken_at_local,
                NULL,
                @latitude,
                @longitude,
                @now);
            """;
        seedMetadata.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        seedMetadata.Parameters.AddWithValue("place_tag_id", NpgsqlDbType.Bigint, placeTagId);
        seedMetadata.Parameters.AddWithValue("taken_at_local", NpgsqlDbType.Timestamp, takenAtLocal);
        seedMetadata.Parameters.AddWithValue("latitude", NpgsqlDbType.Double, latitude);
        seedMetadata.Parameters.AddWithValue("longitude", NpgsqlDbType.Double, longitude);
        seedMetadata.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await seedMetadata.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
