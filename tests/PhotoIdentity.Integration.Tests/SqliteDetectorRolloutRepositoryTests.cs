using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Integration.Tests;

public sealed class SqliteDetectorRolloutRepositoryTests
{
    [Fact]
    public async Task Initialize_upgrades_version_seven_catalogue_with_rollout_schema()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            await using (SqliteConnection connection = new($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    PRAGMA foreign_keys = OFF;
                    CREATE TABLE schema_migrations (version INTEGER NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL);
                    CREATE TABLE processing_runs (
                        id TEXT NOT NULL PRIMARY KEY,
                        status TEXT NOT NULL,
                        configuration_json TEXT NOT NULL,
                        started_at_utc TEXT NOT NULL,
                        completed_at_utc TEXT NULL,
                        error TEXT NULL,
                        cancellation_requested_at_utc TEXT NULL);
                    CREATE TABLE asset_revisions (id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE face_occurrences (
                        id TEXT NOT NULL PRIMARY KEY,
                        asset_revision_id TEXT NOT NULL,
                        ordinal INTEGER NOT NULL,
                        created_at_utc TEXT NOT NULL,
                        UNIQUE (asset_revision_id, ordinal));
                    CREATE TABLE face_observations (
                        face_occurrence_id TEXT NOT NULL,
                        detector_model_id TEXT NOT NULL,
                        detector_model_hash TEXT NOT NULL,
                        confidence REAL NOT NULL,
                        bounding_box_json TEXT NOT NULL,
                        landmarks_json TEXT NOT NULL,
                        observed_at_utc TEXT NOT NULL,
                        PRIMARY KEY (face_occurrence_id, detector_model_id, detector_model_hash));
                    INSERT INTO schema_migrations (version, applied_at_utc) VALUES (7, '2026-08-07T00:00:00Z');
                    PRAGMA user_version = 7;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SqliteConnection upgraded = await database.OpenConnectionAsync();
            using SqliteCommand version = upgraded.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(8L, Convert.ToInt64(await version.ExecuteScalarAsync()));

            using SqliteCommand tables = upgraded.CreateCommand();
            tables.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                      'detector_pipelines',
                      'processing_run_detector_pipelines',
                      'detector_reconciliation_plans',
                      'detector_reconciliation_candidates',
                      'detector_reconciliation_candidate_options',
                      'detector_reconciliation_unmatched_existing');
                """;
            Assert.Equal(6L, Convert.ToInt64(await tables.ExecuteScalarAsync()));

            using SqliteCommand column = upgraded.CreateCommand();
            column.CommandText = "SELECT COUNT(*) FROM pragma_table_info('face_observations') WHERE name = 'detector_pipeline_hash';";
            Assert.Equal(1L, Convert.ToInt64(await column.ExecuteScalarAsync()));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Existing_reconciliation_reuses_explicit_occurrence_and_records_pipeline_hash()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            Seed seed = await SeedAsync(database, existingOrdinals: [0]);
            DetectorPipelineDefinition definition = Pipeline();
            SqliteDetectorRolloutRepository repository = new(database);
            CatalogueDetectorPipelineRegistration registration = await repository.RegisterPipelineAsync(
                seed.RunId,
                definition,
                seed.Now);

            NormalizedBoundingBox box = Box(0.10, 0.10);
            NormalizedFaceLandmarks landmarks = Landmarks(0.10, 0.10);
            ExistingFaceDetectionAnchor existing = new(seed.ExistingFaceIds[0], box, landmarks);
            CandidateFaceDetectionAnchor candidate = new(7, box, landmarks);
            FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan([existing], [candidate]);
            await repository.SavePlanAsync(
                seed.RunId,
                seed.RevisionId,
                registration.PipelineHash,
                [candidate],
                plan,
                seed.Now);

            CatalogueFaceInspection persisted = await repository.ApplyUnambiguousInspectionAsync(
                seed.RunId,
                seed.RevisionId,
                7,
                Inspection(box, landmarks, seed.Now));

            Assert.Equal(seed.ExistingFaceIds[0], persisted.Occurrence.Id);
            Assert.Equal(0, persisted.Occurrence.Ordinal);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand observation = connection.CreateCommand();
            observation.CommandText = """
                SELECT detector_pipeline_hash
                FROM face_observations
                WHERE face_occurrence_id = $face_occurrence_id;
                """;
            observation.Parameters.AddWithValue("$face_occurrence_id", seed.ExistingFaceIds[0].ToString());
            Assert.Equal(registration.PipelineHash.ToString(), (string?)await observation.ExecuteScalarAsync());

            CatalogueDetectorReconciliationPlan stored = (await repository.GetPlanAsync(seed.RunId, seed.RevisionId))!;
            Assert.Equal(seed.ExistingFaceIds[0], stored.Candidates.Single().AppliedFaceOccurrenceId);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task New_reconciliation_allocates_after_existing_ordinals_instead_of_using_candidate_index()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            Seed seed = await SeedAsync(database, existingOrdinals: [0, 5]);
            SqliteDetectorRolloutRepository repository = new(database);
            CatalogueDetectorPipelineRegistration registration = await repository.RegisterPipelineAsync(
                seed.RunId,
                Pipeline(),
                seed.Now);

            NormalizedBoundingBox box = Box(0.70, 0.70);
            NormalizedFaceLandmarks landmarks = Landmarks(0.70, 0.70);
            CandidateFaceDetectionAnchor candidate = new(0, box, landmarks);
            FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan([], [candidate]);
            await repository.SavePlanAsync(
                seed.RunId,
                seed.RevisionId,
                registration.PipelineHash,
                [candidate],
                plan,
                seed.Now);

            CatalogueFaceInspection persisted = await repository.ApplyUnambiguousInspectionAsync(
                seed.RunId,
                seed.RevisionId,
                0,
                Inspection(box, landmarks, seed.Now));

            Assert.DoesNotContain(persisted.Occurrence.Id, seed.ExistingFaceIds);
            Assert.Equal(6, persisted.Occurrence.Ordinal);
            Assert.NotEqual(0, persisted.Occurrence.Ordinal);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Ambiguous_reconciliation_cannot_mutate_catalogue_before_human_resolution()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            Seed seed = await SeedAsync(database, existingOrdinals: [0, 1]);
            SqliteDetectorRolloutRepository repository = new(database);
            CatalogueDetectorPipelineRegistration registration = await repository.RegisterPipelineAsync(
                seed.RunId,
                Pipeline(),
                seed.Now);

            NormalizedBoundingBox box = Box(0.20, 0.20);
            NormalizedFaceLandmarks landmarks = Landmarks(0.20, 0.20);
            ExistingFaceDetectionAnchor[] existing =
            [
                new(seed.ExistingFaceIds[0], box, landmarks),
                new(seed.ExistingFaceIds[1], Box(0.205, 0.205), Landmarks(0.205, 0.205)),
            ];
            CandidateFaceDetectionAnchor candidate = new(3, box, landmarks);
            FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(existing, [candidate]);
            Assert.Equal(FaceDetectionReconciliationDisposition.Ambiguous, plan.CandidateDecisions.Single().Disposition);
            await repository.SavePlanAsync(
                seed.RunId,
                seed.RevisionId,
                registration.PipelineHash,
                [candidate],
                plan,
                seed.Now);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.ApplyUnambiguousInspectionAsync(
                    seed.RunId,
                    seed.RevisionId,
                    3,
                    Inspection(box, landmarks, seed.Now)));
            Assert.Contains("human resolution", error.Message, StringComparison.OrdinalIgnoreCase);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM face_observations WHERE detector_model_id = 'centerface-2019-fp32';";
            Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync()));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static DetectorPipelineDefinition Pipeline() =>
        new(
            implementationId: "centerface-opencv-dnn-v1",
            detectorModelId: new ModelId("centerface-2019-fp32"),
            detectorModelHash: Digest('a'),
            runtime: "opencv-dnn",
            confidenceThreshold: 0.5,
            pipelineMode: "single-pass",
            resizePolicy: "direct-resize-bounded-dynamic-multiple-of",
            inputWidth: 640,
            inputHeight: 640,
            inputShapePolicy: "dynamic-multiple-of",
            inputMultipleOf: 32,
            maximumLongEdge: 1600,
            colourOrder: "RGB",
            dataType: "float32",
            inputScale: 1.0,
            inputMean: [0.0, 0.0, 0.0],
            detectorNmsThreshold: 0.30,
            detectorTopK: 5000,
            tileSize: null,
            tileOverlap: null,
            mergeNmsThreshold: null,
            rotationPolicy: "none");

    private static CatalogueDetectorCandidateInspection Inspection(
        NormalizedBoundingBox box,
        NormalizedFaceLandmarks landmarks,
        DateTimeOffset observedAtUtc) =>
        new(
            new ModelId("centerface-2019-fp32"),
            Digest('a'),
            0.9,
            box,
            landmarks,
            FaceCropId.New(),
            new AlignmentProtocolId("sface-five-point-v1"),
            Digest('c'),
            "runs/test/aligned.png",
            112,
            112,
            new ModelId("sface-2021dec-fp32"),
            Digest('b'),
            new EmbeddingVector([1f, 0f, 0f]),
            observedAtUtc);

    private static async Task<Seed> SeedAsync(SqliteCatalogueDatabase database, int[] existingOrdinals)
    {
        DateTimeOffset now = new(2026, 8, 7, 18, 0, 0, TimeSpan.Zero);
        string sourceId = Guid.NewGuid().ToString();
        string assetId = Guid.NewGuid().ToString();
        AssetRevisionId revisionId = AssetRevisionId.From(Guid.NewGuid());
        ProcessingRunId runId = ProcessingRunId.From(Guid.NewGuid());
        List<FaceOccurrenceId> existing = [];

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction,
            "INSERT INTO sources (id, kind, root_locator, created_at_utc) VALUES ($id, 'local-folder', 'C:/photos', $now);",
            ("$id", sourceId), ("$now", now.ToString("O")));
        await ExecuteAsync(connection, transaction,
            "INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc) VALUES ($id, $source_id, 'image.jpg', $now, $now);",
            ("$id", assetId), ("$source_id", sourceId), ("$now", now.ToString("O")));
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES ($id, $asset_id, $hash, 123, $now, 'image/jpeg', 1000, 800);
            """,
            ("$id", revisionId.ToString()), ("$asset_id", assetId), ("$hash", Digest('d').ToString()), ("$now", now.ToString("O")));
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO processing_runs (
                id, status, configuration_json, started_at_utc, completed_at_utc, error, cancellation_requested_at_utc)
            VALUES ($id, 'pending', '{}', $now, NULL, NULL, NULL);
            """,
            ("$id", runId.ToString()), ("$now", now.ToString("O")));

        foreach (int ordinal in existingOrdinals)
        {
            FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
            existing.Add(occurrenceId);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc) VALUES ($id, $revision_id, $ordinal, $now);",
                ("$id", occurrenceId.ToString()),
                ("$revision_id", revisionId.ToString()),
                ("$ordinal", ordinal),
                ("$now", now.ToString("O")));
        }

        transaction.Commit();
        return new Seed(runId, revisionId, existing, now);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static NormalizedBoundingBox Box(double x, double y) => new(x, y, 0.20, 0.20);

    private static NormalizedFaceLandmarks Landmarks(double x, double y) =>
        new(
            new NormalizedPoint(x + 0.06, y + 0.07),
            new NormalizedPoint(x + 0.14, y + 0.07),
            new NormalizedPoint(x + 0.10, y + 0.11),
            new NormalizedPoint(x + 0.07, y + 0.16),
            new NormalizedPoint(x + 0.13, y + 0.16));

    private static Sha256Digest Digest(char value) => new(new string(value, 64));

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"photoidentity-rollout-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record Seed(
        ProcessingRunId RunId,
        AssetRevisionId RevisionId,
        IReadOnlyList<FaceOccurrenceId> ExistingFaceIds,
        DateTimeOffset Now);
}
