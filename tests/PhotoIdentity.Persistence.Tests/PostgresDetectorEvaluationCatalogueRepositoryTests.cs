using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresDetectorEvaluationCatalogueRepositoryTests
{
    [Fact]
    public async Task Repository_ReadsRunsPhotosAndDetections_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_detector_eval_{Guid.NewGuid():N}";
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

            ProcessingRunId runId = ProcessingRunId.New();
            AssetRevisionId firstRevision = AssetRevisionId.New();
            AssetRevisionId secondRevision = AssetRevisionId.New();
            FaceOccurrenceId faceId = FaceOccurrenceId.New();
            await SeedRunAsync(
                testBuilder.ConnectionString,
                runId,
                firstRevision,
                secondRevision,
                faceId);

            PostgresDetectorEvaluationCatalogueRepository repository = new(database);

            CatalogueDetectorEvaluationRun run = Assert.Single(await repository.GetRunsAsync());
            Assert.Equal(runId, run.Id);
            Assert.Equal("completed", run.Status);
            Assert.Equal(2, run.PhotoCount);
            Assert.Equal(1, run.DetectionCount);

            CatalogueDetectorEvaluationPhotoPage page = await repository.GetPhotosAsync(runId, offset: 0, limit: 10);
            Assert.Equal(2, page.Total);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal("alpha.jpg", page.Items[0].PhotoName);
            Assert.Equal("succeeded", page.Items[0].JobStatus);
            Assert.Equal(firstRevision, page.Items[0].RevisionId);
            CatalogueDetectorEvaluationDetection detection = Assert.Single(page.Items[0].Detections);
            Assert.Equal(faceId, detection.Id);
            Assert.Equal(0, detection.Ordinal);
            Assert.Equal(0.87, detection.Confidence);
            Assert.Equal(0.1, detection.BoundingBox.X);
            Assert.Equal(0.2, detection.BoundingBox.Y);
            Assert.Equal(0.3, detection.BoundingBox.Width);
            Assert.Equal(0.4, detection.BoundingBox.Height);

            Assert.Equal("beta.jpg", page.Items[1].PhotoName);
            Assert.Empty(page.Items[1].Detections);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRunAsync(
        string connectionString,
        ProcessingRunId runId,
        AssetRevisionId firstRevision,
        AssetRevisionId secondRevision,
        FaceOccurrenceId faceId)
    {
        Guid sourceId = Guid.NewGuid();
        Guid firstAsset = Guid.NewGuid();
        Guid secondAsset = Guid.NewGuid();
        DateTimeOffset started = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset completed = started.AddMinutes(5);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', @root_locator, @started);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES
                (@first_asset_id, @source_id, 'detector/alpha.jpg', @started),
                (@second_asset_id, @source_id, 'detector/beta.jpg', @started);

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
                (@first_revision_id, @first_asset_id, @first_hash, 100, @started, 'image/jpeg', 640, 480),
                (@second_revision_id, @second_asset_id, @second_hash, 100, @started, 'image/jpeg', 800, 600);

            INSERT INTO processing_runs (
                id,
                status,
                configuration_json,
                started_at_utc,
                completed_at_utc)
            VALUES (@run_id, 'completed', '{}', @started, @completed);

            INSERT INTO processing_jobs (
                id,
                processing_run_id,
                asset_revision_id,
                status,
                attempt_count,
                available_at_utc,
                started_at_utc,
                completed_at_utc,
                idempotency_key)
            VALUES
                (@first_job_id, @run_id, @first_revision_id, 'succeeded', 1, @started, @started, @completed, @first_job_key),
                (@second_job_id, @run_id, @second_revision_id, 'succeeded', 1, @started, @started, @completed, @second_job_key);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES (@face_id, @first_revision_id, 0, @started);

            INSERT INTO face_observations (
                face_occurrence_id,
                detector_model_id,
                detector_model_hash,
                confidence,
                bounding_box_json,
                landmarks_json,
                observed_at_utc)
            VALUES (
                @face_id,
                'detector',
                @model_hash,
                0.87,
                @bounding_box,
                '[]',
                @completed);
            """;
        seed.Parameters.AddWithValue("source_id", NpgsqlDbType.Uuid, sourceId);
        seed.Parameters.AddWithValue("root_locator", NpgsqlDbType.Text, $"detector-eval-root-{sourceId:N}");
        seed.Parameters.AddWithValue("first_asset_id", NpgsqlDbType.Uuid, firstAsset);
        seed.Parameters.AddWithValue("second_asset_id", NpgsqlDbType.Uuid, secondAsset);
        seed.Parameters.AddWithValue("first_revision_id", NpgsqlDbType.Uuid, firstRevision.Value);
        seed.Parameters.AddWithValue("second_revision_id", NpgsqlDbType.Uuid, secondRevision.Value);
        seed.Parameters.AddWithValue("first_hash", NpgsqlDbType.Text, new string('a', 64));
        seed.Parameters.AddWithValue("second_hash", NpgsqlDbType.Text, new string('b', 64));
        seed.Parameters.AddWithValue("run_id", NpgsqlDbType.Uuid, runId.Value);
        seed.Parameters.AddWithValue("first_job_id", NpgsqlDbType.Uuid, Guid.NewGuid());
        seed.Parameters.AddWithValue("second_job_id", NpgsqlDbType.Uuid, Guid.NewGuid());
        seed.Parameters.AddWithValue("first_job_key", NpgsqlDbType.Text, $"job-{Guid.NewGuid():N}");
        seed.Parameters.AddWithValue("second_job_key", NpgsqlDbType.Text, $"job-{Guid.NewGuid():N}");
        seed.Parameters.AddWithValue("face_id", NpgsqlDbType.Uuid, faceId.Value);
        seed.Parameters.AddWithValue("model_hash", NpgsqlDbType.Text, new string('c', 64));
        NpgsqlParameter boundingBox = seed.Parameters.Add("bounding_box", NpgsqlDbType.Jsonb);
        boundingBox.Value = "[0.1,0.2,0.3,0.4]";
        seed.Parameters.AddWithValue("started", NpgsqlDbType.TimestampTz, started);
        seed.Parameters.AddWithValue("completed", NpgsqlDbType.TimestampTz, completed);
        await seed.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
