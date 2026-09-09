using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoPlaceRepositoryTests
{
    [Fact]
    public async Task ManualAndAutomaticPlaces_PreservePrecedenceAndIdempotency_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_places_{Guid.NewGuid():N}";
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

            AssetRevisionId revisionId = AssetRevisionId.From(Guid.NewGuid());
            AssetRevisionId automaticRevisionId = AssetRevisionId.From(Guid.NewGuid());
            await SeedRevisionAsync(
                testBuilder.ConnectionString,
                Guid.Parse(revisionId.ToString()),
                contentHash: new string('a', 64),
                sourceKey: "places/manual-photo.jpg");
            await SeedRevisionAsync(
                testBuilder.ConnectionString,
                Guid.Parse(automaticRevisionId.ToString()),
                contentHash: new string('b', 64),
                sourceKey: "places/automatic-photo.jpg");

            PostgresPhotoPlaceRepository repository = new(database, TimeProvider.System);
            IPhotoPlaceRepository places = repository;
            IAutomaticPhotoPlaceRepository automatic = repository;

            PhotoPlaceState manual = await places.SetManualPlaceAsync(
                revisionId,
                "Sweden/Stockholm",
                "maintainer");

            Assert.NotNull(manual.Place);
            Assert.Equal("Sweden/Stockholm", manual.Place.Value);
            Assert.Equal("manual", manual.Place.SourceKind);

            AutomaticPhotoPlaceEligibility eligibility =
                await automatic.GetEligibilityAsync(revisionId);
            Assert.False(eligibility.Allowed);
            Assert.True(eligibility.BlockedByManual);
            Assert.False(eligibility.BlockedByConflict);

            AutomaticPhotoPlaceWriteResult blocked = await automatic.TrySetAsync(
                revisionId,
                "Norway/Oslo",
                "GeoNames",
                "automatic-place-enrichment");
            Assert.False(blocked.Applied);
            Assert.True(blocked.BlockedByManual);
            Assert.Equal("Sweden/Stockholm", blocked.State.Place?.Value);

            PhotoPlaceState cleared = await places.ClearManualPlaceAsync(revisionId, "maintainer");
            Assert.Null(cleared.Place);

            AutomaticPhotoPlaceEligibility clearedEligibility =
                await automatic.GetEligibilityAsync(revisionId);
            Assert.False(clearedEligibility.Allowed);
            Assert.True(clearedEligibility.BlockedByManual);

            AutomaticPhotoPlaceWriteResult stillBlocked = await automatic.TrySetAsync(
                revisionId,
                "Norway/Oslo",
                "GeoNames",
                "automatic-place-enrichment");
            Assert.False(stillBlocked.Applied);
            Assert.True(stillBlocked.BlockedByManual);

            AutomaticPhotoPlaceWriteResult applied = await automatic.TrySetAsync(
                automaticRevisionId,
                "Norway/Oslo",
                "GeoNames",
                "automatic-place-enrichment");
            Assert.True(applied.Applied);
            Assert.False(applied.BlockedByManual);
            Assert.Equal("automatic", applied.State.Place?.SourceKind);
            Assert.Equal("Norway/Oslo", applied.State.Place?.Value);

            AutomaticPhotoPlaceWriteResult unchanged = await automatic.TrySetAsync(
                automaticRevisionId,
                "Norway/Oslo",
                "geonames",
                "automatic-place-enrichment");
            Assert.False(unchanged.Applied);
            Assert.Equal("Norway/Oslo", unchanged.State.Place?.Value);

            IReadOnlyList<PhotoPlaceDefinition> definitions =
                await places.GetDefinitionsAsync();
            Assert.Contains(definitions, place => place.Value == "Sweden/Stockholm");
            Assert.Contains(definitions, place => place.Value == "Norway/Oslo");
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRevisionAsync(
        string connectionString,
        Guid revisionId,
        string contentHash,
        string sourceKey)
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
            """;
        seed.Parameters.AddWithValue("source_id", sourceId);
        seed.Parameters.AddWithValue("root_locator", $"places-root-{sourceId:N}");
        seed.Parameters.AddWithValue("asset_id", assetId);
        seed.Parameters.AddWithValue("revision_id", revisionId);
        seed.Parameters.AddWithValue("content_sha256", contentHash);
        seed.Parameters.AddWithValue("source_key", sourceKey);
        seed.Parameters.AddWithValue("now", now);
        await seed.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
