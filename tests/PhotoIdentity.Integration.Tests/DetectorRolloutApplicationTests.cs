using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorRolloutApplicationTests
{
    [Fact]
    public async Task Pending_review_exposes_only_planner_options_and_resolution_does_not_mutate_catalogue()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            TestState state = await SeedAmbiguousStateAsync(databasePath);
            await using RolloutApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            DetectorRolloutPendingReviewResponse[] pending =
                await client.GetFromJsonAsync<DetectorRolloutPendingReviewResponse[]>(
                    $"/api/detector-rollout/runs/{state.RunId}/pending")
                ?? [];

            DetectorRolloutPendingReviewResponse item = Assert.Single(pending);
            Assert.Equal(state.RevisionId.ToString(), item.AssetRevisionId);
            Assert.Equal(state.CandidateIndex, item.CandidateIndex);
            Assert.Equal(
                state.ExistingFaceIds.OrderBy(value => value.ToString()).Select(value => value.ToString()),
                item.Options.Select(value => value.FaceOccurrenceId).OrderBy(value => value));
            Assert.Null(item.LatestResolution);

            await using (SqliteConnection before = await state.Database.OpenConnectionAsync())
            {
                Assert.Equal(2L, await CountAsync(before, "face_occurrences"));
                Assert.Equal(2L, await CountAsync(before, "face_observations"));
                Assert.Equal(0L, await CountAsync(before, "face_crops"));
                Assert.Equal(0L, await CountAsync(before, "person_labels"));
            }

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/detector-rollout/runs/{state.RunId}/revisions/{state.RevisionId}/candidates/{state.CandidateIndex}/resolve",
                new SaveDetectorRolloutResolutionRequest(
                    "new",
                    null,
                    "maintainer",
                    "legitimate additional face"));
            response.EnsureSuccessStatusCode();
            DetectorRolloutResolutionResponse saved =
                (await response.Content.ReadFromJsonAsync<DetectorRolloutResolutionResponse>())!;
            Assert.Equal("new", saved.Kind);
            Assert.Null(saved.FaceOccurrenceId);

            DetectorRolloutRunResponse summary =
                (await client.GetFromJsonAsync<DetectorRolloutRunResponse>(
                    $"/api/detector-rollout/runs/{state.RunId}"))!;
            Assert.Equal(0, summary.AwaitingReviewCount);
            Assert.Equal(1, summary.ReadyToApplyCount);
            Assert.False(summary.RolloutComplete);

            await using SqliteConnection after = await state.Database.OpenConnectionAsync();
            Assert.Equal(2L, await CountAsync(after, "face_occurrences"));
            Assert.Equal(2L, await CountAsync(after, "face_observations"));
            Assert.Equal(0L, await CountAsync(after, "face_crops"));
            Assert.Equal(0L, await CountAsync(after, "person_labels"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Existing_resolution_rejects_arbitrary_face_identifier()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            TestState state = await SeedAmbiguousStateAsync(databasePath);
            await using RolloutApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/detector-rollout/runs/{state.RunId}/revisions/{state.RevisionId}/candidates/{state.CandidateIndex}/resolve",
                new SaveDetectorRolloutResolutionRequest(
                    "existing",
                    FaceOccurrenceId.New().ToString(),
                    "maintainer"));

            Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
            string payload = await response.Content.ReadAsStringAsync();
            Assert.Contains("not one of", payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<TestState> SeedAmbiguousStateAsync(string databasePath)
    {
        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 8, 7, 20, 45, 0, TimeSpan.Zero);
        string sourceId = Guid.NewGuid().ToString();
        string assetId = Guid.NewGuid().ToString();
        AssetRevisionId revisionId = AssetRevisionId.New();
        ProcessingRunId runId = ProcessingRunId.New();
        FaceOccurrenceId first = FaceOccurrenceId.New();
        FaceOccurrenceId second = FaceOccurrenceId.New();
        NormalizedBoundingBox candidateBox = Box(0.20, 0.20);
        NormalizedFaceLandmarks candidateLandmarks = Landmarks(0.20, 0.20);

        await using (SqliteConnection connection = await database.OpenConnectionAsync())
        {
            using SqliteTransaction transaction = connection.BeginTransaction();
            await ExecuteAsync(connection, transaction,
                "INSERT INTO sources (id, kind, root_locator, created_at_utc) VALUES ($id, 'local-folder', $root, $now);",
                ("$id", sourceId),
                ("$root", directoryRoot(databasePath)),
                ("$now", now.ToString("O")));
            await ExecuteAsync(connection, transaction,
                "INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc) VALUES ($id, $source_id, 'photo.jpg', $now, $now);",
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

            await InsertExistingAsync(connection, transaction, first, revisionId, 0, candidateBox, candidateLandmarks, now);
            await InsertExistingAsync(
                connection,
                transaction,
                second,
                revisionId,
                1,
                Box(0.205, 0.205),
                Landmarks(0.205, 0.205),
                now);
            transaction.Commit();
        }

        SqliteDetectorRolloutRepository rollout = new(database);
        CatalogueDetectorPipelineRegistration registration = await rollout.RegisterPipelineAsync(runId, Pipeline(), now);
        ExistingFaceDetectionAnchor[] existing =
        [
            new(first, candidateBox, candidateLandmarks),
            new(second, Box(0.205, 0.205), Landmarks(0.205, 0.205)),
        ];
        const int candidateIndex = 0;
        CandidateFaceDetectionAnchor candidate = new(candidateIndex, candidateBox, candidateLandmarks);
        FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(existing, [candidate]);
        await rollout.SavePlanAsync(
            runId,
            revisionId,
            registration.PipelineHash,
            [candidate],
            plan,
            now);
        await new SqliteDetectorRolloutReviewRepository(database).SaveInspectionAsync(
            runId,
            revisionId,
            candidateIndex,
            Inspection(candidateBox, candidateLandmarks, now));
        return new TestState(database, runId, revisionId, [first, second], candidateIndex);
    }

    private static async Task InsertExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId occurrenceId,
        AssetRevisionId revisionId,
        int ordinal,
        NormalizedBoundingBox box,
        NormalizedFaceLandmarks landmarks,
        DateTimeOffset now)
    {
        await ExecuteAsync(connection, transaction,
            "INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc) VALUES ($id, $revision_id, $ordinal, $now);",
            ("$id", occurrenceId.ToString()),
            ("$revision_id", revisionId.ToString()),
            ("$ordinal", ordinal),
            ("$now", now.ToString("O")));
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO face_observations (
                face_occurrence_id, detector_model_id, detector_model_hash,
                confidence, bounding_box_json, landmarks_json, observed_at_utc)
            VALUES ($id, 'yunet-2023mar-fp32', $hash, 0.9, $box, $landmarks, $now);
            """,
            ("$id", occurrenceId.ToString()),
            ("$hash", Digest('e').ToString()),
            ("$box", JsonSerializer.Serialize(new[] { box.X, box.Y, box.Width, box.Height })),
            ("$landmarks", JsonSerializer.Serialize(new[]
            {
                new[] { landmarks.LeftEye.X, landmarks.LeftEye.Y },
                new[] { landmarks.RightEye.X, landmarks.RightEye.Y },
                new[] { landmarks.Nose.X, landmarks.Nose.Y },
                new[] { landmarks.MouthLeft.X, landmarks.MouthLeft.Y },
                new[] { landmarks.MouthRight.X, landmarks.MouthRight.Y },
            })),
            ("$now", now.ToString("O")));
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
        DateTimeOffset now) =>
        new(
            new ModelId("centerface-2019-fp32"),
            Digest('a'),
            0.91,
            box,
            landmarks,
            FaceCropId.New(),
            new AlignmentProtocolId("sface-five-point-v1"),
            Digest('c'),
            "rollouts/test/candidate.png",
            112,
            112,
            new ModelId("sface-2021dec-fp32"),
            Digest('b'),
            new EmbeddingVector([0.6f, 0.8f, 0f]),
            now);

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

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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

    private static string directoryRoot(string databasePath) =>
        Path.GetDirectoryName(databasePath) ?? Path.GetTempPath();

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RolloutApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public RolloutApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }

    private sealed record TestState(
        SqliteCatalogueDatabase Database,
        ProcessingRunId RunId,
        AssetRevisionId RevisionId,
        IReadOnlyList<FaceOccurrenceId> ExistingFaceIds,
        int CandidateIndex);
}
