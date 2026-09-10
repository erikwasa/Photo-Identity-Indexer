using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresFaceInspectionRepositoryTests
{
    [Fact]
    public async Task SaveInspectionAsync_PersistsAtomicallyAndReplaysNaturalKeys_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_face_inspection_{Guid.NewGuid():N}";
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

            AssetRevisionId revisionId = AssetRevisionId.New();
            await SeedRevisionAsync(testBuilder.ConnectionString, revisionId);
            PostgresFaceInspectionRepository repository = new(database);
            DateTimeOffset now = new(2026, 9, 10, 4, 0, 0, TimeSpan.Zero);
            Sha256Digest cropHash = new(new string('c', 64));

            FaceInspectionWrite first = CreateInspection(
                FaceOccurrenceId.New(),
                FaceCropId.New(),
                revisionId,
                cropHash,
                confidence: 0.95,
                storagePath: "faces/first.png",
                now);
            await repository.SaveInspectionAsync(first);

            FaceInspectionWrite replay = CreateInspection(
                FaceOccurrenceId.New(),
                FaceCropId.New(),
                revisionId,
                cropHash,
                confidence: 0.91,
                storagePath: "faces/replayed.png",
                now.AddMinutes(1));
            await repository.SaveInspectionAsync(replay);

            await using NpgsqlConnection verify = new(testBuilder.ConnectionString);
            await verify.OpenAsync();
            Assert.Equal(1, await CountAsync(
                verify,
                "SELECT COUNT(*) FROM face_occurrences WHERE asset_revision_id = @revision;",
                revisionId.Value));
            Assert.Equal(1, await CountAsync(
                verify,
                """
                SELECT COUNT(*)
                FROM face_crops
                INNER JOIN face_occurrences ON face_occurrences.id = face_crops.face_occurrence_id
                WHERE face_occurrences.asset_revision_id = @revision;
                """,
                revisionId.Value));
            Assert.Equal(1, await CountAsync(
                verify,
                """
                SELECT COUNT(*)
                FROM embeddings
                INNER JOIN face_crops ON face_crops.id = embeddings.face_crop_id
                INNER JOIN face_occurrences ON face_occurrences.id = face_crops.face_occurrence_id
                WHERE face_occurrences.asset_revision_id = @revision;
                """,
                revisionId.Value));

            await using NpgsqlCommand observation = verify.CreateCommand();
            observation.CommandText = """
                SELECT face_observations.confidence, face_crops.storage_path
                FROM face_occurrences
                INNER JOIN face_observations ON face_observations.face_occurrence_id = face_occurrences.id
                INNER JOIN face_crops ON face_crops.face_occurrence_id = face_occurrences.id
                WHERE face_occurrences.asset_revision_id = @revision;
                """;
            observation.Parameters.AddWithValue("revision", revisionId.Value);
            await using NpgsqlDataReader reader = await observation.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0.91, reader.GetDouble(0), precision: 8);
            Assert.Equal("faces/replayed.png", reader.GetString(1));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static FaceInspectionWrite CreateInspection(
        FaceOccurrenceId occurrenceId,
        FaceCropId cropId,
        AssetRevisionId revisionId,
        Sha256Digest cropHash,
        double confidence,
        string storagePath,
        DateTimeOffset observedAtUtc) =>
        new(
            occurrenceId,
            revisionId,
            ordinal: 0,
            observedAtUtc,
            new ModelId("centerface-test"),
            new Sha256Digest(new string('d', 64)),
            confidence,
            new NormalizedBoundingBox(0.1, 0.2, 0.3, 0.4),
            new NormalizedFaceLandmarks(
                new NormalizedPoint(0.2, 0.3),
                new NormalizedPoint(0.4, 0.3),
                new NormalizedPoint(0.3, 0.4),
                new NormalizedPoint(0.24, 0.52),
                new NormalizedPoint(0.36, 0.52)),
            cropId,
            new AlignmentProtocolId("sface-five-point-v1"),
            cropHash,
            storagePath,
            112,
            112,
            new ModelId("sface-test"),
            new Sha256Digest(new string('e', 64)),
            new EmbeddingVector([1f, 0f]));

    private static async Task SeedRevisionAsync(string connectionString, AssetRevisionId revisionId)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 10, 3, 0, 0, TimeSpan.Zero);
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'local-folder', '/archive', @now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES (@asset, @source, 'photo.jpg', @now, @now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES (
                @revision, @asset, @hash, 2048, @now, 'image/jpeg', 1920, 1080);
            """;
        command.Parameters.AddWithValue("source", sourceId);
        command.Parameters.AddWithValue("asset", assetId);
        command.Parameters.AddWithValue("revision", revisionId.Value);
        command.Parameters.AddWithValue("hash", new string('a', 64));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(
        NpgsqlConnection connection,
        string sql,
        Guid revisionId)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("revision", revisionId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
