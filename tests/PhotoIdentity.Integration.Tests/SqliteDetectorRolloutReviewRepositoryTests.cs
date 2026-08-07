using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Integration.Tests;

public sealed class SqliteDetectorRolloutReviewRepositoryTests
{
    [Fact]
    public async Task Initialize_adds_durable_candidate_payload_and_resolution_tables()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(
                SqliteCatalogueDatabase.CurrentSchemaVersion,
                Convert.ToInt64(await ScalarAsync(connection, "PRAGMA user_version;")));
            Assert.Equal(
                1L,
                Convert.ToInt64(await ScalarAsync(
                    connection,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'detector_reconciliation_candidate_inspections';")));
            Assert.Equal(
                1L,
                Convert.ToInt64(await ScalarAsync(
                    connection,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'detector_reconciliation_resolution_actions';")));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Candidate_inspection_round_trips_before_canonical_face_mutation()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            TestState state = await CreateAmbiguousStateAsync(databasePath);
            SqliteDetectorRolloutReviewRepository review = new(state.Database);
            CatalogueDetectorCandidateInspection inspection = Inspection(state.CandidateBox, state.CandidateLandmarks, state.Now);

            CatalogueDetectorCandidateInspection stored = await review.SaveInspectionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                inspection);
            CatalogueDetectorCandidateInspection reread = (await review.GetInspectionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex))!;

            Assert.Equal(inspection.DetectorModelId, stored.DetectorModelId);
            Assert.Equal(inspection.DetectorModelHash, stored.DetectorModelHash);
            Assert.Equal(inspection.CropContentHash, reread.CropContentHash);
            Assert.Equal(inspection.Embedding.Values.ToArray(), reread.Embedding.Values.ToArray());

            await using SqliteConnection connection = await state.Database.OpenConnectionAsync();
            Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM face_observations;")));
            Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM face_crops;")));
            Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(connection, "SELECT COUNT(*) FROM embeddings;")));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Ambiguous_resolution_is_append_only_and_can_select_persisted_option_or_defer()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            TestState state = await CreateAmbiguousStateAsync(databasePath);
            SqliteDetectorRolloutReviewRepository review = new(state.Database);

            CatalogueDetectorReconciliationResolution first = await review.RecordResolutionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                DetectorReconciliationResolutionKind.ExistingOccurrence,
                state.ExistingFaceIds[1],
                "maintainer",
                state.Now,
                "same physical face");
            CatalogueDetectorReconciliationResolution latest = await review.RecordResolutionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                DetectorReconciliationResolutionKind.Deferred,
                null,
                "maintainer",
                state.Now.AddMinutes(1),
                "inspect source photo again");

            IReadOnlyList<CatalogueDetectorReconciliationResolution> history = await review.GetResolutionHistoryAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex);
            CatalogueDetectorReconciliationReview current = (await review.GetReviewAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex))!;

            Assert.Equal(2, history.Count);
            Assert.Equal(first.Id, history[0].Id);
            Assert.Equal(DetectorReconciliationResolutionKind.Deferred, latest.Kind);
            Assert.Equal(latest.Id, current.LatestResolution!.Id);
            Assert.Null(current.LatestResolution.FaceOccurrenceId);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Existing_resolution_rejects_face_outside_persisted_ambiguity_options()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            TestState state = await CreateAmbiguousStateAsync(databasePath);
            SqliteDetectorRolloutReviewRepository review = new(state.Database);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.RecordResolutionAsync(
                    state.RunId,
                    state.RevisionId,
                    state.CandidateIndex,
                    DetectorReconciliationResolutionKind.ExistingOccurrence,
                    FaceOccurrenceId.New(),
                    "maintainer",
                    state.Now));

            Assert.Contains("not one of", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await review.GetResolutionHistoryAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Human_resolution_is_rejected_for_unambiguous_candidate()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            BasicSeed seed = await SeedAsync(database, [0]);
            SqliteDetectorRolloutRepository rollout = new(database);
            CatalogueDetectorPipelineRegistration registration = await rollout.RegisterPipelineAsync(seed.RunId, Pipeline(), seed.Now);
            NormalizedBoundingBox box = Box(0.1, 0.1);
            NormalizedFaceLandmarks landmarks = Landmarks(0.1, 0.1);
            ExistingFaceDetectionAnchor existing = new(seed.ExistingFaceIds[0], box, landmarks);
            CandidateFaceDetectionAnchor candidate = new(0, box, landmarks);
            await rollout.SavePlanAsync(
                seed.RunId,
                seed.RevisionId,
                registration.PipelineHash,
                [candidate],
                FaceDetectionReconciliationPlanner.Plan([existing], [candidate]),
                seed.Now);

            SqliteDetectorRolloutReviewRepository review = new(database);
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.RecordResolutionAsync(
                    seed.RunId,
                    seed.RevisionId,
                    0,
                    DetectorReconciliationResolutionKind.ExistingOccurrence,
                    seed.ExistingFaceIds[0],
                    "maintainer",
                    seed.Now));

            Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static async Task<TestState> CreateAmbiguousStateAsync(string databasePath)
    {
        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        BasicSeed seed = await SeedAsync(database, [0, 1]);
        SqliteDetectorRolloutRepository rollout = new(database);
        CatalogueDetectorPipelineRegistration registration = await rollout.RegisterPipelineAsync(seed.RunId, Pipeline(), seed.Now);

        NormalizedBoundingBox box = Box(0.20, 0.20);
        NormalizedFaceLandmarks landmarks = Landmarks(0.20, 0.20);
        ExistingFaceDetectionAnchor[] existing =
        [
            new(seed.ExistingFaceIds[0], box, landmarks),
            new(seed.ExistingFaceIds[1], Box(0.205, 0.205), Landmarks(0.205, 0.205)),
        ];
        const int candidateIndex = 3;
        CandidateFaceDetectionAnchor candidate = new(candidateIndex, box, landmarks);
        FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(existing, [candidate]);
        Assert.Equal(FaceDetectionReconciliationDisposition.Ambiguous, plan.CandidateDecisions.Single().Disposition);
        await rollout.SavePlanAsync(
            seed.RunId,
            seed.RevisionId,
            registration.PipelineHash,
            [candidate],
            plan,
            seed.Now);

        return new TestState(
            database,
            seed.RunId,
            seed.RevisionId,
            seed.ExistingFaceIds,
            candidateIndex,
            box,
            landmarks,
            seed.Now);
    }

    private static async Task<BasicSeed> SeedAsync(SqliteCatalogueDatabase database, int[] existingOrdinals)
    {
        DateTimeOffset now = new(2026, 8, 7, 19, 30, 0, TimeSpan.Zero);
        string sourceId = Guid.NewGuid().ToString();
        string assetId = Guid.NewGuid().ToString();
        AssetRevisionId revisionId = AssetRevisionId.New();
        ProcessingRunId runId = ProcessingRunId.New();
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
        return new BasicSeed(runId, revisionId, existing, now);
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
            0.91,
            box,
            landmarks,
            FaceCropId.New(),
            new AlignmentProtocolId("sface-five-point-v1"),
            Digest('c'),
            "runs/test/candidates/candidate-004/aligned.png",
            112,
            112,
            new ModelId("sface-2021dec-fp32"),
            Digest('b'),
            new EmbeddingVector([0.6f, 0.8f, 0f]),
            observedAtUtc);

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

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
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
        Path.Combine(Path.GetTempPath(), $"photoidentity-rollout-review-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record BasicSeed(
        ProcessingRunId RunId,
        AssetRevisionId RevisionId,
        IReadOnlyList<FaceOccurrenceId> ExistingFaceIds,
        DateTimeOffset Now);

    private sealed record TestState(
        SqliteCatalogueDatabase Database,
        ProcessingRunId RunId,
        AssetRevisionId RevisionId,
        IReadOnlyList<FaceOccurrenceId> ExistingFaceIds,
        int CandidateIndex,
        NormalizedBoundingBox CandidateBox,
        NormalizedFaceLandmarks CandidateLandmarks,
        DateTimeOffset Now);
}
