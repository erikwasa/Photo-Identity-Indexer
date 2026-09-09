using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoMetadataBackfillRepositoryTests
{
    [Fact]
    public async Task GetRefreshCandidatesAsync_MatchesMissingCurrentAndForceSemantics_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_metadata_backfill_{Guid.NewGuid():N}";
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

            AssetRevisionId revisionId = AssetRevisionId.From(Guid.NewGuid());
            await SeedRevisionAsync(testBuilder.ConnectionString, Guid.Parse(revisionId.ToString()));

            PostgresPhotoMetadataBackfillRepository repository = new(database);
            IReadOnlyList<PhotoMetadataBackfillRefreshCandidate> missing =
                await repository.GetRefreshCandidatesAsync(
                    limit: 10,
                    offset: 0,
                    PhotoMetadataExtractionContract.CurrentVersion,
                    force: false);

            PhotoMetadataBackfillRefreshCandidate candidate = Assert.Single(missing);
            Assert.Equal(revisionId, candidate.RevisionId);
            Assert.True(candidate.IsNew);

            PostgresPhotoCaptureMetadataRepository capture = new(database);
            await capture.SavePhotoMetadataAsync(
                revisionId,
                new PhotoCaptureMetadata(
                    takenAtLocal: new DateTime(2026, 9, 9, 14, 30, 0),
                    utcOffset: TimeSpan.FromHours(2),
                    latitude: 59.3293,
                    longitude: 18.0686),
                DateTimeOffset.UtcNow);

            PostgresPhotoMetadataInspectionRepository inspections = new(database);
            await inspections.MarkAsync(
                revisionId,
                PhotoMetadataExtractionContract.CurrentVersion,
                DateTimeOffset.UtcNow);

            Assert.Empty(await repository.GetRefreshCandidatesAsync(
                limit: 10,
                offset: 0,
                PhotoMetadataExtractionContract.CurrentVersion,
                force: false));

            IReadOnlyList<PhotoMetadataBackfillRefreshCandidate> forced =
                await repository.GetRefreshCandidatesAsync(
                    limit: 10,
                    offset: 0,
                    PhotoMetadataExtractionContract.CurrentVersion,
                    force: true);
            Assert.False(Assert.Single(forced).IsNew);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRevisionAsync(string connectionString, Guid revisionId)
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
            VALUES (@source_id, 'local-folder', 'C:\\photos', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'metadata/photo.jpg', @now);

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
                456,
                @now,
                'image/jpeg',
                1024,
                768);
            """;
        seed.Parameters.AddWithValue("source_id", sourceId);
        seed.Parameters.AddWithValue("asset_id", assetId);
        seed.Parameters.AddWithValue("revision_id", revisionId);
        seed.Parameters.AddWithValue("content_sha256", new string('b', 64));
        seed.Parameters.AddWithValue("now", now);
        await seed.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
