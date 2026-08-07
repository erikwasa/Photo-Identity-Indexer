using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Integration.Tests;

public sealed class SqliteDetectorRolloutApplicationRepositoryTests
{
    [Fact]
    public async Task Reviewed_existing_resolution_applies_exact_occurrence_without_changing_person_labels()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            TestState state = await CreateAmbiguousStateAsync(databasePath);
            SqliteDetectorRolloutReviewRepository review = new(state.Database);
            CatalogueDetectorCandidateInspection inspection = Inspection(
                state.CandidateBox,
                state.CandidateLandmarks,
                state.Now);
            await review.SaveInspectionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                inspection);
            await review.RecordResolutionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                DetectorReconciliationResolutionKind.ExistingOccurrence,
                state.ExistingFaceIds[1],
                "maintainer",
                state.Now.AddMinutes(1),
                "same physical face");

            SqliteDetectorRolloutApplicationRepository application = new(state.Database);
            CatalogueDetectorRolloutApplyResult applied = await application.ApplyResolvedAsync(state.RunId);
            CatalogueDetectorReconciliationReview current = (await review.GetReviewAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex))!;

            Assert.Equal(1, applied.ConsideredCount);
            Assert.Equal(1, applied.AppliedCount);
            Assert.Equal(state.ExistingFaceIds[1], current.Candidate.AppliedFaceOccurrenceId);

            await using SqliteConnection connection = await state.Database.OpenConnectionAsync();
            Assert.Equal(2L, Convert.ToInt64(await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM face_occurrences;")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM person_labels;")));
            Assert.Equal(state.ExistingFaceIds[0].ToString(), (string?)await ScalarAsync(
                connection,
                "SELECT face_occurrence_id FROM person_labels LIMIT 1;"));
            Assert.Equal(state.PipelineHash.ToString(), (string?)await ScalarAsync(
                connection,
                $"SELECT detector_pipeline_hash FROM face_observations WHERE face_occurrence_id = '{state.ExistingFaceIds[1]}';"));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Reviewed_new_resolution_allocates_after_existing_ordinals_and_is_replay_safe()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            TestState state = await CreateAmbiguousStateAsync(databasePath, existingOrdinals: [0, 5]);
            SqliteDetectorRolloutReviewRepository review = new(state.Database);
            CatalogueDetectorCandidateInspection inspection = Inspection(
                state.CandidateBox,
                state.CandidateLandmarks,
                state.Now);
            await review.SaveInspectionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                inspection);
            await review.RecordResolutionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                DetectorReconciliationResolutionKind.NewOccurrence,
                null,
                "maintainer",
                state.Now.AddMinutes(1),
                "additional legitimate face");

            SqliteDetectorRolloutApplicationRepository application = new(state.Database);
            FaceOccurrenceId first = await application.ApplyReviewedCandidateAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex);
            FaceOccurrenceId second = await application.ApplyReviewedCandidateAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex);

            Assert.Equal(first, second);
            Assert.DoesNotContain(first, state.ExistingFaceIds);

            await using SqliteConnection connection = await state.Database.OpenConnectionAsync();
            Assert.Equal(3L, Convert.ToInt64(await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM face_occurrences;")));
            Assert.Equal(6L, Convert.ToInt64(await ScalarAsync(
                connection,
                $"SELECT ordinal FROM face_occurrences WHERE id = '{first}';")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM person_labels;")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(
                connection,
                $"SELECT COUNT(*) FROM face_crops WHERE face_occurrence_id = '{first}';")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(
                connection,
                $"SELECT COUNT(*) FROM face_observations WHERE face_occurrence_id = '{first}';")));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Summary_distinguishes_awaiting_ready_and_deferred_ambiguous_candidates()
    {
        string databasePath = TemporaryDatabasePath();
        try
        {
            TestState state = await CreateAmbiguousStateAsync(databasePath);
            SqliteDetectorRolloutReviewRepository review = new(state.Database);
            await review.SaveInspectionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                Inspection(state.CandidateBox, state.CandidateLandmarks, state.Now));

            SqliteDetectorRolloutApplicationRepository application = new(state.Database);
            CatalogueDetectorRolloutSummary awaiting = await application.GetSummaryAsync(state.RunId);
            Assert.Equal(1, awaiting.AwaitingReviewCount);
            Assert.Equal(0, awaiting.ReadyToApplyCount);
            Assert.Equal(0, awaiting.DeferredCount);

            await review.RecordResolutionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                DetectorReconciliationResolutionKind.NewOccurrence,
                null,
                "maintainer",
                state.Now.AddMinutes(1));
            CatalogueDetectorRolloutSummary ready = await application.GetSummaryAsync(state.RunId);
            Assert.Equal(0, ready.AwaitingReviewCount);
            Assert.Equal(1, ready.ReadyToApplyCount);
            Assert.Equal(0, ready.DeferredCount);

            await review.RecordResolutionAsync(
                state.RunId,
                state.RevisionId,
                state.CandidateIndex,
                DetectorReconciliationResolutionKind.Deferred,
                null,
                "maintainer",
                state.Now.AddMinutes(2));
            CatalogueDetectorRolloutSummary deferred = await application.GetSummaryAsync(state.RunId);
            Assert.Equal(0, deferred.AwaitingReviewCount);
            Assert.Equal(0, deferred.ReadyToApplyCount);
            Assert.Equal(1, deferred.DeferredCount);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static async Task<TestState> CreateAmbiguousStateAsync(
        string databasePath,
        int[]? existingOrdinals = null)
    {
        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 8, 7, 20, 30, 0, TimeSpan.Zero);
        string sourceId = Guid.NewGuid().ToString();
        string assetId = Guid.NewGuid().ToString();
        AssetRevisionId revisionId = AssetRevisionId.New();
        ProcessingRunId runId = ProcessingRunId.New();
        int[] ordinals = existingOrdinals ?? [0, 1];
        List<FaceOccurrenceId> existing = [];

        await using (SqliteConnection connection = await database.OpenConnectionAsync())
        {
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
                ("$id", revisionId.ToString()),
                ("$asset_id", assetId),
                ("$hash", Digest('d').ToString()),
                ("$now", now.ToString("O")));
            await ExecuteAsync(connection, transaction,
                """
                INSERT INTO processing_runs (
                    id, status, configuration_json, started_at_utc, completed_at_utc, error, cancellation_requested_at_utc)
                VALUES ($id, 'pending', '{}', $now, NULL, NULL, NULL);
                """,
                ("$id", runId.ToString()), ("$now", now.ToString("O")));

            foreach (int ordinal in ordinals)
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

            string personId = Guid.NewGuid().ToString();
            await ExecuteAsync(connection, transaction,
                "INSERT INTO people (id, display_name, created_at_utc) VALUES ($id, 'Existing person', $now);",
                ("$id", personId), ("$now", now.ToString("O")));
            await ExecuteAsync(connection, transaction,
                """
                INSERT INTO person_labels (
                    person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES ($person_id, $face_id, 'manual', 'maintainer', $now);
                """,
                ("$person_id", personId),
                ("$face_id", existing[0].ToString()),
                ("$now", now.ToString("O")));
            transaction.Commit();
        }

        SqliteDetectorRolloutRepository rollout = new(database);
        CatalogueDetectorPipelineRegistration registration = await rollout.RegisterPipelineAsync(runId, Pipeline(), now);
        NormalizedBoundingBox box = Box(0.20, 0.20);
        NormalizedFaceLandmarks landmarks = Landmarks(0.20, 0.20);
        ExistingFaceDetectionAnchor[] existingAnchors =
        [
            new(existing[0], box, landmarks),
            new(existing[1], Box(0.205, 0.205), Landmarks(0.205, 0.205)),
        ];
        const int candidateIndex = 3;
        CandidateFaceDetectionAnchor candidate = new(candidateIndex, box, landmarks);
        FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(existingAnchors, [candidate]);
        Assert.Equal(FaceDetectionReconciliationDisposition.Ambiguous, plan.CandidateDecisions.Single().Disposition);
        await rollout.SavePlanAsync(
            runId,
            revisionId,
            registration.PipelineHash,
            [candidate],
            plan,
            now);

        return new TestState(
            database,
            runId,
            revisionId,
            existing,
            candidateIndex,
            box,
            landmarks,
            registration.PipelineHash,
            now);
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
            "rollouts/test/candidates/candidate-004/aligned.png",
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
        Path.Combine(Path.GetTempPath(), $"photoidentity-rollout-apply-{Guid.NewGuid():N}.db");

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

    private sealed record TestState(
        SqliteCatalogueDatabase Database,
        ProcessingRunId RunId,
        AssetRevisionId RevisionId,
        IReadOnlyList<FaceOccurrenceId> ExistingFaceIds,
        int CandidateIndex,
        NormalizedBoundingBox CandidateBox,
        NormalizedFaceLandmarks CandidateLandmarks,
        Sha256Digest PipelineHash,
        DateTimeOffset Now);
}
