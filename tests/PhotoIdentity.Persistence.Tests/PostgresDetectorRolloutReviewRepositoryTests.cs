using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresDetectorRolloutReviewRepositoryTests
{
    [Fact]
    public async Task Review_PreservesPayloadHistoryAndConcurrentReplay_WhenLivePostgresIsConfigured()
    {
        string? adminString = Environment.GetEnvironmentVariable("PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
        {
            return;
        }

        string databaseName = $"photoidentity_rollout_review_{Guid.NewGuid():N}";
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

            IDetectorRolloutReviewRepository repository = new PostgresDetectorRolloutReviewRepository(database);
            CatalogueDetectorCandidateInspection inspection = Inspection(now.AddTicks(7));
            CatalogueDetectorCandidateInspection[] saved = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => repository.SaveInspectionAsync(run, revision, 0, inspection)));
            Assert.All(saved, value => Assert.Equal(inspection.CropId, value.CropId));
            CatalogueDetectorCandidateInspection read = (await repository.GetInspectionAsync(run, revision, 0))!;
            Assert.Equal(inspection.Embedding.Values.ToArray(), read.Embedding.Values.ToArray());
            Assert.Equal(inspection.BoundingBox, read.BoundingBox);
            Assert.Equal(now, read.ObservedAtUtc);
            Assert.Null(await repository.GetInspectionAsync(run, revision, 99));
            Assert.Null(await repository.GetReviewAsync(run, revision, 99));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveInspectionAsync(run, revision, 0, Inspection(now)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RecordResolutionAsync(
                run, revision, 0, DetectorReconciliationResolutionKind.ExistingOccurrence, FaceOccurrenceId.New(), "reviewer", now));

            CatalogueDetectorReconciliationResolution[] retries = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => repository.RecordResolutionAsync(run, revision, 0,
                    DetectorReconciliationResolutionKind.ExistingOccurrence, face, " reviewer ", now, " same face ")));
            Assert.All(retries, value => Assert.Equal(retries[0].Id, value.Id));
            Assert.Single(await repository.GetResolutionHistoryAsync(run, revision, 0));
            CatalogueDetectorReconciliationResolution deferred = await repository.RecordResolutionAsync(
                run, revision, 0, DetectorReconciliationResolutionKind.Deferred, null, "reviewer", now.AddMinutes(1));
            CatalogueDetectorReconciliationResolution newFace = await repository.RecordResolutionAsync(
                run, revision, 0, DetectorReconciliationResolutionKind.NewOccurrence, null, "reviewer", now.AddMinutes(2));
            Assert.True(newFace.Id > deferred.Id);
            Assert.Equal(3, (await repository.GetResolutionHistoryAsync(run, revision, 0)).Count);
            CatalogueDetectorReconciliationReview pending = Assert.Single(await repository.GetPendingAmbiguousAsync(run));
            Assert.Equal(newFace.Id, pending.LatestResolution!.Id);
            Assert.Equal(face, Assert.Single(pending.Candidate.PossibleFaceOccurrenceIds));
            Assert.NotNull(pending.Inspection);

            await using NpgsqlConnection connection = await database.OpenConnectionAsync();
            await using NpgsqlCommand counts = connection.CreateCommand();
            counts.CommandText = "SELECT (SELECT count(*) FROM face_observations) + (SELECT count(*) FROM face_crops) + (SELECT count(*) FROM embeddings);";
            Assert.Equal(0L, await counts.ExecuteScalarAsync());
            await using NpgsqlCommand apply = connection.CreateCommand();
            apply.CommandText = "UPDATE detector_reconciliation_candidates SET applied_face_occurrence_id = @face, applied_at_utc = @now;";
            apply.Parameters.AddWithValue("face", face.Value);
            apply.Parameters.AddWithValue("now", now);
            await apply.ExecuteNonQueryAsync();
            Assert.Empty(await repository.GetPendingAmbiguousAsync(run));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveInspectionAsync(run, revision, 0, inspection));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RecordResolutionAsync(
                run, revision, 0, DetectorReconciliationResolutionKind.Deferred, null, "reviewer", now));
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetReviewAsync(run, revision, 0, cancelled.Token));
        }
        finally
        {
            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

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
            INSERT INTO detector_pipelines (pipeline_hash, detector_model_id, detector_model_hash, canonical_definition, recorded_at_utc)
                VALUES (@hash, 'detector', @hash, 'test-pipeline', @now);
            INSERT INTO processing_run_detector_pipelines (processing_run_id, pipeline_hash, recorded_at_utc)
                VALUES (@run, @hash, @now);
            INSERT INTO detector_reconciliation_plans (processing_run_id, asset_revision_id, pipeline_hash, planned_at_utc)
                VALUES (@run, @revision, @hash, @now);
            INSERT INTO detector_reconciliation_candidates
                (processing_run_id, asset_revision_id, candidate_index, disposition, bounding_box_json, landmarks_json)
                VALUES (@run, @revision, 0, 'ambiguous', '[0.1,0.1,0.2,0.2]', '[[0.16,0.17],[0.24,0.17],[0.20,0.21],[0.17,0.26],[0.23,0.26]]');
            INSERT INTO detector_reconciliation_candidate_options
                (processing_run_id, asset_revision_id, candidate_index, face_occurrence_id) VALUES (@run, @revision, 0, @face);
            """;
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
