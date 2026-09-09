using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresDetectorReconciliationPlanRepositoryTests
{
    [Fact]
    public async Task Plans_PreserveProvenanceAndRejectChangedReplay_WhenLivePostgresIsConfigured()
    {
        string? adminString = Environment.GetEnvironmentVariable("PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
        {
            return;
        }

        string databaseName = $"photoidentity_rollout_plan_{Guid.NewGuid():N}";
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

            IDetectorReconciliationPlanRepository repository = new PostgresDetectorReconciliationPlanRepository(database);
            DetectorPipelineDefinition pipeline = Pipeline();
            CatalogueDetectorPipelineRegistration[] registrations = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => repository.RegisterPipelineAsync(run, pipeline, now)));
            Assert.All(registrations, value => Assert.Equal(pipeline.ComputeHash(), value.PipelineHash));
            Assert.Equal(pipeline.ToCanonicalText(), registrations[0].CanonicalDefinition);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RegisterPipelineAsync(run, Pipeline(0.7), now));
            Assert.Null(await repository.GetPlanAsync(run, revision));

            CatalogueDetectorCandidateInspection geometry = Inspection(now);
            CandidateFaceDetectionAnchor candidate = new(7, geometry.BoundingBox, geometry.Landmarks);
            FaceDetectionReconciliationPlan plan = new(
                [new(7, FaceDetectionReconciliationDisposition.Ambiguous, null, [face])], [face]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SavePlanAsync(
                run, revision, new Sha256Digest(new string('f', 64)), [candidate], plan, now));
            CatalogueDetectorReconciliationPlan[] saved = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => repository.SavePlanAsync(run, revision, pipeline.ComputeHash(), [candidate], plan, now.AddTicks(7))));
            Assert.All(saved, value => Assert.Equal(7, Assert.Single(value.Candidates).CandidateIndex));
            CatalogueDetectorReconciliationPlan read = (await repository.GetPlanAsync(run, revision))!;
            Assert.Equal(now, read.PlannedAtUtc);
            Assert.Equal(face, Assert.Single(read.ExistingOccurrencesWithoutCandidate));
            Assert.Equal(face, Assert.Single(Assert.Single(read.Candidates).PossibleFaceOccurrenceIds));
            Assert.Equal(candidate.BoundingBox, read.Candidates[0].BoundingBox);

            CandidateFaceDetectionAnchor extra = candidate with { CandidateIndex = 8 };
            FaceDetectionReconciliationPlan expanded = new(
                [.. plan.CandidateDecisions, new(8, FaceDetectionReconciliationDisposition.NewOccurrence, null, [])], [face]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SavePlanAsync(
                run, revision, pipeline.ComputeHash(), [candidate, extra], expanded, now));
            Assert.Single((await repository.GetPlanAsync(run, revision))!.Candidates);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SavePlanAsync(
                run, revision, pipeline.ComputeHash(), [candidate], plan, now.AddSeconds(1)));
            await Assert.ThrowsAsync<ArgumentException>(() => repository.SavePlanAsync(
                run, revision, pipeline.ComputeHash(), [], plan, now));

            // Applied plans cannot be re-saved; their application evidence remains readable.
            await using NpgsqlConnection connection = await database.OpenConnectionAsync();
            await using NpgsqlCommand apply = connection.CreateCommand();
            apply.CommandText = "UPDATE detector_reconciliation_candidates SET applied_face_occurrence_id = @face, applied_at_utc = @now;";
            apply.Parameters.AddWithValue("face", face.Value);
            apply.Parameters.AddWithValue("now", now);
            await apply.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SavePlanAsync(
                run, revision, pipeline.ComputeHash(), [candidate], plan, now));
            Assert.Equal(face, Assert.Single((await repository.GetPlanAsync(run, revision))!.Candidates).AppliedFaceOccurrenceId);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetPlanAsync(run, revision, cancelled.Token));

            // Failed rebinding rolls back the unused pipeline registration.
            await using NpgsqlCommand count = connection.CreateCommand();
            count.CommandText = "SELECT count(*) FROM detector_pipelines;";
            Assert.Equal(1L, await count.ExecuteScalarAsync());

            ProcessingRunId secondRun = ProcessingRunId.New();
            await using NpgsqlCommand createRun = connection.CreateCommand();
            createRun.CommandText = "INSERT INTO processing_runs (id, status, configuration_json, started_at_utc) VALUES (@run, 'running', '{}', @now);";
            createRun.Parameters.AddWithValue("run", secondRun.Value);
            createRun.Parameters.AddWithValue("now", now);
            await createRun.ExecuteNonQueryAsync();
            await repository.RegisterPipelineAsync(secondRun, pipeline, now);
            FaceDetectionReconciliationPlan unambiguous = new(
                [new(7, FaceDetectionReconciliationDisposition.ExistingOccurrence, face, [face]),
                 new(8, FaceDetectionReconciliationDisposition.NewOccurrence, null, [])], []);
            CatalogueDetectorReconciliationPlan storedUnambiguous = await repository.SavePlanAsync(
                secondRun, revision, pipeline.ComputeHash(), [extra, candidate], unambiguous, now);
            Assert.Equal(face, storedUnambiguous.Candidates[0].ProposedFaceOccurrenceId);
            Assert.Equal(FaceDetectionReconciliationDisposition.NewOccurrence, storedUnambiguous.Candidates[1].Disposition);
            Assert.Empty(storedUnambiguous.Candidates[1].PossibleFaceOccurrenceIds);
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
