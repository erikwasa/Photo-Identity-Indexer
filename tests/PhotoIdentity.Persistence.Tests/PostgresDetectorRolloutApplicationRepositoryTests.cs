using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Core.Catalogue;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresDetectorRolloutApplicationRepositoryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Application_IsAtomicAndReplaySafe_WhenLivePostgresIsConfigured(bool reviewed, bool existing)
    {
        string? adminString = Environment.GetEnvironmentVariable("PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
        {
            return;
        }

        string databaseName = $"photoidentity_rollout_apply_{Guid.NewGuid():N}";
        await using NpgsqlConnection admin = new(new NpgsqlConnectionStringBuilder(adminString) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using PostgresCatalogueDatabase database = new(new NpgsqlConnectionStringBuilder(adminString)
            {
                Database = databaseName,
                Pooling = false,
            }.ConnectionString);
            Assert.Null((await database.TryInitializeAsync()).Error);
            Assert.Null((await database.TryInitializeAsync()).Error);
            ProcessingRunId run = ProcessingRunId.New();
            AssetRevisionId revision = AssetRevisionId.New();
            FaceOccurrenceId face = FaceOccurrenceId.New();
            DateTimeOffset now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
            await SeedAsync(database, run, revision, face, now);
            ICatalogueStoreInitializer initializer = database;
            await initializer.InitializeAsync();
            IAssetRevisionLookupRepository assets = new PostgresAssetRevisionLookupRepository(database);
            AssetRevisionLookup loaded = (await assets.GetRevisionAsync(revision))!;
            Assert.Equal("photo.jpg", loaded.SourceKey);
            Assert.Equal(revision, (await assets.FindRevisionAsync(loaded.SourceKey, loaded.ContentHash))!.RevisionId);
            Assert.Null(await assets.GetRevisionAsync(AssetRevisionId.New()));
            Assert.Null(await assets.FindRevisionAsync("missing.jpg", loaded.ContentHash));

            IDetectorReconciliationPlanRepository plans = new PostgresDetectorReconciliationPlanRepository(database);
            IDetectorRolloutReviewRepository reviews = new PostgresDetectorRolloutReviewRepository(database);
            IDetectorRolloutApplicationRepository application = new PostgresDetectorRolloutApplicationRepository(database);
            DetectorPipelineDefinition pipeline = Pipeline();
            await plans.RegisterPipelineAsync(run, pipeline, now);
            Assert.Equal(pipeline.ComputeHash(), await application.GetPipelineHashAsync(run));
            CatalogueDetectorCandidateInspection inspection = Inspection(now);
            CandidateFaceDetectionAnchor candidate = new(0, inspection.BoundingBox, inspection.Landmarks);
            FaceDetectionReconciliationDisposition disposition = reviewed
                ? FaceDetectionReconciliationDisposition.Ambiguous
                : existing ? FaceDetectionReconciliationDisposition.ExistingOccurrence : FaceDetectionReconciliationDisposition.NewOccurrence;
            FaceDetectionReconciliationPlan plan = new(
                [new(0, disposition, !reviewed && existing ? face : null, reviewed || existing ? [face] : [])], []);
            await plans.SavePlanAsync(run, revision, pipeline.ComputeHash(), [candidate], plan, now);
            await reviews.SaveInspectionAsync(run, revision, 0, inspection);
            if (reviewed)
            {
                Assert.Equal(1, (await application.GetSummaryAsync(run)).AwaitingReviewCount);
                Assert.Single(await application.GetPendingReviewsAsync(run));
                await Assert.ThrowsAsync<InvalidOperationException>(() => application.ApplyReviewedCandidateAsync(run, revision, 0));
                await reviews.RecordResolutionAsync(run, revision, 0, DetectorReconciliationResolutionKind.Deferred, null, "reviewer", now);
                Assert.Equal(1, (await application.ApplyResolvedAsync(run)).DeferredCount);
                await reviews.RecordResolutionAsync(run, revision, 0, existing
                    ? DetectorReconciliationResolutionKind.ExistingOccurrence : DetectorReconciliationResolutionKind.NewOccurrence,
                    existing ? face : null, "reviewer", now);
                Assert.Equal(1, (await application.GetSummaryAsync(run)).ReadyToApplyCount);
            }

            FaceOccurrenceId[] applied = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => reviewed
                ? application.ApplyReviewedCandidateAsync(run, revision, 0)
                : application.ApplyUnambiguousInspectionAsync(run, revision, 0, inspection)));
            Assert.All(applied, value => Assert.Equal(applied[0], value));
            Assert.Equal(existing, applied[0] == face);
            CatalogueDetectorRolloutSummary summary = await application.GetSummaryAsync(run);
            Assert.Equal(1, summary.CandidateCount);
            Assert.Equal(1, summary.AppliedCount);
            Assert.Equal(0, summary.AwaitingReviewCount);
            Assert.Equal(0, summary.ReadyToApplyCount);
            Assert.Empty(await application.GetPendingReviewsAsync(run));
            Assert.Equal(applied[0], (await application.GetOccurrenceAnchorAsync(applied[0]))!.FaceOccurrenceId);
            Assert.Equal(existing ? 1 : 2, (await application.GetExistingAnchorsAsync(revision, pipeline.ComputeHash())).Count);
            Assert.Equal(applied[0], (await plans.GetPlanAsync(run, revision))!.Candidates[0].AppliedFaceOccurrenceId);

            await using NpgsqlConnection connection = await database.OpenConnectionAsync();
            await using NpgsqlCommand counts = connection.CreateCommand();
            counts.CommandText = "SELECT count(*) FROM face_occurrences;";
            Assert.Equal(existing ? 1L : 2L, await counts.ExecuteScalarAsync());
            counts.CommandText = "SELECT count(*) FROM face_crops;";
            Assert.Equal(1L, await counts.ExecuteScalarAsync());
            counts.CommandText = "SELECT count(*) FROM embeddings;";
            Assert.Equal(1L, await counts.ExecuteScalarAsync());
            counts.CommandText = "SELECT count(*) FROM person_labels;";
            Assert.Equal(1L, await counts.ExecuteScalarAsync());

            // Force a crop primary-key collision after allocating another face and observation.
            // The transaction must roll back every write, including ordinal allocation.
            ProcessingRunId conflictingRun = ProcessingRunId.New();
            await using NpgsqlCommand createRun = connection.CreateCommand();
            createRun.CommandText = "INSERT INTO processing_runs (id, status, configuration_json, started_at_utc) VALUES (@run, 'running', '{}', @now);";
            createRun.Parameters.AddWithValue("run", conflictingRun.Value);
            createRun.Parameters.AddWithValue("now", now);
            await createRun.ExecuteNonQueryAsync();
            await plans.RegisterPipelineAsync(conflictingRun, pipeline, now);
            await plans.SavePlanAsync(conflictingRun, revision, pipeline.ComputeHash(), [candidate],
                new([new(0, FaceDetectionReconciliationDisposition.NewOccurrence, null, [])], []), now);
            await Assert.ThrowsAsync<PostgresException>(() => application.ApplyUnambiguousInspectionAsync(conflictingRun, revision, 0, inspection));
            Assert.Null((await plans.GetPlanAsync(conflictingRun, revision))!.Candidates[0].AppliedFaceOccurrenceId);
            counts.CommandText = "SELECT count(*) FROM face_occurrences;";
            Assert.Equal(existing ? 1L : 2L, await counts.ExecuteScalarAsync());
            counts.CommandText = "SELECT count(*) FROM face_observations WHERE detector_pipeline_hash IS NOT NULL;";
            Assert.Equal(1L, await counts.ExecuteScalarAsync());
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.ApplyReviewedCandidateAsync(run, revision, 0, cancelled.Token));
        }
        finally
        {
            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static DetectorPipelineDefinition Pipeline(double threshold = 0.6) => new(
        "test-detector-v1", new ModelId("detector"), new Sha256Digest(new string('a', 64)),
        "test-runtime", threshold, "full-image", "fixed", 640, 640, "fixed",
        null, null, "RGB", "float32", 1, [0, 0, 0], 0.3, 5000, null, null, null, "none");
    private static CatalogueDetectorCandidateInspection Inspection(DateTimeOffset now) => new(
        new ModelId("detector"), new Sha256Digest(new string('a', 64)), 0.91,
        new NormalizedBoundingBox(0.1, 0.1, 0.2, 0.2),
        new NormalizedFaceLandmarks(new(0.16, 0.17), new(0.24, 0.17), new(0.20, 0.21), new(0.17, 0.26), new(0.23, 0.26)),
        FaceCropId.New(), new AlignmentProtocolId("five-point-v1"), new Sha256Digest(new string('c', 64)),
        "test/candidate.png", 112, 112, new ModelId("embedder"), new Sha256Digest(new string('b', 64)),
        new EmbeddingVector([0.6f, 0.8f, 0f]), now);

    private static async Task SeedAsync(PostgresCatalogueDatabase database, ProcessingRunId run, AssetRevisionId revision,
        FaceOccurrenceId face, DateTimeOffset now)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc) VALUES (@source, 'test', 'test-root', @now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc) VALUES (@asset, @source, 'photo.jpg', @now);
            INSERT INTO asset_revisions (id, asset_id, content_sha256, size_bytes, observed_at_utc)
                VALUES (@revision, @asset, @hash, 1, @now);
            INSERT INTO processing_runs (id, status, configuration_json, started_at_utc)
                VALUES (@run, 'running', '{}', @now);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc) VALUES (@face, @revision, 0, @now);
            INSERT INTO face_observations (face_occurrence_id, detector_model_id, detector_model_hash, confidence,
                bounding_box_json, landmarks_json, observed_at_utc)
                VALUES (@face, 'baseline', @hash, 0.8, '[0.1,0.1,0.2,0.2]',
                    '[[0.16,0.17],[0.24,0.17],[0.20,0.21],[0.17,0.26],[0.23,0.26]]', @now);
            INSERT INTO people (id, display_name, created_at_utc) VALUES (@person, 'Test person', @now);
            INSERT INTO person_labels (face_occurrence_id, person_id, label_kind, assigned_by, assigned_at_utc)
                VALUES (@face, @person, 'confirmed', 'test', @now);
            """;
        command.Parameters.AddWithValue("person", Guid.NewGuid());
        command.Parameters.AddWithValue("source", Guid.NewGuid());
        command.Parameters.AddWithValue("asset", Guid.NewGuid());
        command.Parameters.AddWithValue("revision", revision.Value);
        command.Parameters.AddWithValue("run", run.Value);
        command.Parameters.AddWithValue("face", face.Value);
        command.Parameters.AddWithValue("hash", new string('a', 64));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
    }
}
