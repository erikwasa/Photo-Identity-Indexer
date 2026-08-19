using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorEvaluationApplicationTests
{
    [Fact]
    public async Task Detector_evaluation_lists_all_run_photos_and_streams_originals_without_paths()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sessionRoot = Path.Combine(directory, "private-sessions");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            SeededEvaluation seeded = await SeedEvaluationAsync(database, directory);

            await using DetectorEvaluationApiFactory factory = new(databasePath, sessionRoot);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage runsResponse = await client.GetAsync("/api/detector-evaluation/runs");
            runsResponse.EnsureSuccessStatusCode();
            Assert.Contains(
                "no-store",
                runsResponse.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            string runsJson = await runsResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(seeded.SourceRoot, runsJson, StringComparison.OrdinalIgnoreCase);

            DetectorEvaluationRunResponse[] runs = Assert.IsType<DetectorEvaluationRunResponse[]>(
                await runsResponse.Content.ReadFromJsonAsync<DetectorEvaluationRunResponse[]>());
            DetectorEvaluationRunResponse run = Assert.Single(runs);
            Assert.Equal(seeded.RunId.ToString(), run.Id);
            Assert.Equal(2, run.PhotoCount);
            Assert.Equal(1, run.DetectionCount);

            string photosUrl = $"/api/detector-evaluation/photos?runId={seeded.RunId}&offset=0&limit=10";
            using HttpResponseMessage photosResponse = await client.GetAsync(photosUrl);
            photosResponse.EnsureSuccessStatusCode();
            string photosJson = await photosResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(seeded.SourceRoot, photosJson, StringComparison.OrdinalIgnoreCase);

            DetectorEvaluationPhotoPageResponse page = Assert.IsType<DetectorEvaluationPhotoPageResponse>(
                await photosResponse.Content.ReadFromJsonAsync<DetectorEvaluationPhotoPageResponse>());
            Assert.Equal(2, page.Total);
            Assert.Equal(2, page.Items.Count);

            DetectorEvaluationPhotoResponse detectedPhoto = page.Items[0];
            Assert.Equal("R001__group.jpg", detectedPhoto.PhotoName);
            DetectorEvaluationDetectionResponse detection = Assert.Single(detectedPhoto.Detections);
            Assert.Equal(1, detection.FaceNumber);
            Assert.Equal(0.97, detection.Confidence, 6);
            Assert.Equal(0.1, detection.BoundingBox.X, 6);
            Assert.Equal(0.2, detection.BoundingBox.Y, 6);
            Assert.Equal(0.3, detection.BoundingBox.Width, 6);
            Assert.Equal(0.4, detection.BoundingBox.Height, 6);

            DetectorEvaluationPhotoResponse emptyPhoto = page.Items[1];
            Assert.Equal("R002__empty.jpg", emptyPhoto.PhotoName);
            Assert.Empty(emptyPhoto.Detections);

            using HttpResponseMessage contentResponse = await client.GetAsync(detectedPhoto.ContentUrl);
            contentResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", contentResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(seeded.DetectedPhotoBytes, await contentResponse.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Detector_evaluation_sessions_persist_resume_and_export_private_ground_truth()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sessionRoot = Path.Combine(directory, "private-sessions");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededEvaluation seeded = await SeedEvaluationAsync(database, directory);

            DetectorEvaluationSessionSummaryResponse created;
            await using (DetectorEvaluationApiFactory factory = new(databasePath, sessionRoot))
            using (HttpClient client = factory.CreateClient())
            {
                CreateDetectorEvaluationSessionRequest createRequest = new(
                    "M16 baseline",
                    seeded.RunId.ToString(),
                    [
                        new DetectorEvaluationManifestEntryRequest(
                            "R001",
                            "R001__group.jpg",
                            "Representative",
                            "Pilot representative",
                            "Group",
                            1,
                            null),
                        new DetectorEvaluationManifestEntryRequest(
                            "R002",
                            "R002__empty.jpg",
                            "Difficult",
                            "External difficult",
                            "Small / distant",
                            1,
                            null),
                    ]);

                using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                    "/api/detector-evaluation/sessions",
                    createRequest);
                createResponse.EnsureSuccessStatusCode();
                created = Assert.IsType<DetectorEvaluationSessionSummaryResponse>(
                    await createResponse.Content.ReadFromJsonAsync<DetectorEvaluationSessionSummaryResponse>());
                Assert.Equal(2, created.PhotoCount);
                Assert.Equal(0, created.CompletedPhotoCount);

                DetectorEvaluationSessionResponse session = Assert.IsType<DetectorEvaluationSessionResponse>(
                    await client.GetFromJsonAsync<DetectorEvaluationSessionResponse>(
                        $"/api/detector-evaluation/sessions/{created.Id}"));
                Assert.Equal(2, session.Photos.Count);

                DetectorEvaluationSessionPhotoResponse detectedPhoto = session.Photos[0];
                DetectorEvaluationSessionDetectionResponse detection = Assert.Single(detectedPhoto.Detections);
                SaveDetectorEvaluationPhotoReviewRequest detectedReview = new(
                    [new DetectorEvaluationDetectionJudgementRequest(detection.Id, "correct")],
                    [],
                    null,
                    "Confirmed on the full photo.");
                using HttpResponseMessage detectedSave = await client.PutAsJsonAsync(
                    $"/api/detector-evaluation/sessions/{created.Id}/photos/{detectedPhoto.RevisionId}",
                    detectedReview);
                detectedSave.EnsureSuccessStatusCode();

                DetectorEvaluationSessionPhotoResponse emptyPhoto = session.Photos[1];
                SaveDetectorEvaluationPhotoReviewRequest emptyReview = new(
                    [],
                    [
                        new DetectorEvaluationMissedFaceRequest(
                            Guid.NewGuid().ToString("D"),
                            new DetectorEvaluationBoundingBoxResponse(0.15, 0.2, 0.1, 0.14)),
                    ],
                    "Small / distant",
                    "Missed face marked directly on the photo.");
                using HttpResponseMessage emptySave = await client.PutAsJsonAsync(
                    $"/api/detector-evaluation/sessions/{created.Id}/photos/{emptyPhoto.RevisionId}",
                    emptyReview);
                emptySave.EnsureSuccessStatusCode();
            }

            Assert.Single(Directory.GetFiles(sessionRoot, "*.json", SearchOption.TopDirectoryOnly));

            await using DetectorEvaluationApiFactory restartedFactory = new(databasePath, sessionRoot);
            using HttpClient restartedClient = restartedFactory.CreateClient();
            using HttpResponseMessage sessionResponse = await restartedClient.GetAsync(
                $"/api/detector-evaluation/sessions/{created.Id}");
            await sessionResponse.EnsureSuccessWithDiagnosticBodyAsync("resumed detector-evaluation session request");
            string sessionJson = await sessionResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(seeded.SourceRoot, sessionJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sessionRoot, sessionJson, StringComparison.OrdinalIgnoreCase);

            DetectorEvaluationSessionResponse resumed = Assert.IsType<DetectorEvaluationSessionResponse>(
                await sessionResponse.Content.ReadFromJsonAsync<DetectorEvaluationSessionResponse>());
            Assert.Equal(2, resumed.CompletedPhotoCount);
            Assert.All(resumed.Photos, photo => Assert.True(photo.IsComplete));
            Assert.Equal(1, resumed.Photos[0].CorrectDetections);
            Assert.Single(resumed.Photos[1].MissedFaces);

            using HttpResponseMessage exportResponse = await restartedClient.GetAsync(
                $"/api/detector-evaluation/sessions/{created.Id}/export.csv");
            exportResponse.EnsureSuccessStatusCode();
            Assert.Equal("text/csv", exportResponse.Content.Headers.ContentType?.MediaType);
            string csv = await exportResponse.Content.ReadAsStringAsync();
            Assert.Contains("Sample ID,Image Name", csv, StringComparison.Ordinal);
            Assert.Contains("R001,R001__group.jpg", csv, StringComparison.Ordinal);
            Assert.Contains("R002,R002__empty.jpg", csv, StringComparison.Ordinal);
            Assert.Contains("Small / distant", csv, StringComparison.Ordinal);
            Assert.DoesNotContain(seeded.SourceRoot, csv, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public void Detector_evaluation_manifest_parser_finds_excel_sheet_headers_after_preamble()
    {
        const string csv = """
            Detector Recall Pilot;;;;;;
            Complete Countable Faces before review;;;;;;
            Keep this file private;;;;;;
            Sample ID;Image Name;Sample Group;Primary Category;Countable Faces;Source Group;Source SHA-256
            R001;R001__group.jpg;Representative;Group;5;Pilot representative;
            D001;D001__small.jpg;Difficult;Small / distant;2;External difficult;
            """;

        IReadOnlyList<DetectorEvaluationManifestEntryRequest> entries = DetectorEvaluationManifestCsv.Parse(csv);

        Assert.Equal(2, entries.Count);
        Assert.Equal("R001__group.jpg", entries[0].ImageName);
        Assert.Equal(5, entries[0].CountableFaces);
        Assert.Equal("External difficult", entries[1].SourceGroup);
    }

    private static async Task<SeededEvaluation> SeedEvaluationAsync(
        SqliteCatalogueDatabase database,
        string directory)
    {
        DateTimeOffset now = new(2026, 8, 3, 0, 30, 0, TimeSpan.Zero);
        string sourceRoot = Path.Combine(directory, "private-evaluation-photos");
        Directory.CreateDirectory(sourceRoot);

        byte[] detectedPhotoBytes = [1, 2, 3, 4, 5];
        byte[] emptyPhotoBytes = [6, 7, 8];
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "R001__group.jpg"), detectedPhotoBytes);
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "R002__empty.jpg"), emptyPhotoBytes);

        SourceId sourceId = SourceId.New();
        CatalogueSource source = new(sourceId, "local-folder", sourceRoot, now);
        SqliteAssetCatalogueRepository assetRepository = new(database);

        AssetId detectedAssetId = AssetId.New();
        CatalogueAssetRevision detectedRevision = await assetRepository.SaveRevisionAsync(
            source,
            new CatalogueAsset(detectedAssetId, sourceId, "R001__group.jpg", now),
            Revision(detectedAssetId, detectedPhotoBytes));

        AssetId emptyAssetId = AssetId.New();
        CatalogueAssetRevision emptyRevision = await assetRepository.SaveRevisionAsync(
            source,
            new CatalogueAsset(emptyAssetId, sourceId, "R002__empty.jpg", now),
            Revision(emptyAssetId, emptyPhotoBytes));

        ProcessingRunId runId = ProcessingRunId.New();
        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO processing_runs (
                id,
                status,
                configuration_json,
                started_at_utc,
                completed_at_utc)
            VALUES (
                $run_id,
                'completed',
                '{}',
                $created_at_utc,
                $created_at_utc);

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
                ($first_job_id, $run_id, $first_revision_id, 'succeeded', 1, $created_at_utc, $created_at_utc, $created_at_utc, $first_key),
                ($second_job_id, $run_id, $second_revision_id, 'succeeded', 1, $created_at_utc, $created_at_utc, $created_at_utc, $second_key);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($face_id, $first_revision_id, 0, $created_at_utc);

            INSERT INTO face_observations (
                face_occurrence_id,
                detector_model_id,
                detector_model_hash,
                confidence,
                bounding_box_json,
                landmarks_json,
                observed_at_utc)
            VALUES (
                $face_id,
                'test-detector',
                $model_hash,
                0.97,
                '[0.1,0.2,0.3,0.4]',
                '[]',
                $created_at_utc);
            """;
        command.Parameters.AddWithValue("$run_id", runId.ToString());
        command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
        command.Parameters.AddWithValue("$first_job_id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$second_job_id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$first_revision_id", detectedRevision.Id.ToString());
        command.Parameters.AddWithValue("$second_revision_id", emptyRevision.Id.ToString());
        command.Parameters.AddWithValue("$first_key", $"evaluation:{runId}:{detectedRevision.Id}");
        command.Parameters.AddWithValue("$second_key", $"evaluation:{runId}:{emptyRevision.Id}");
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$model_hash", new string('b', 64));
        await command.ExecuteNonQueryAsync();

        return new SeededEvaluation(runId, sourceRoot, detectedPhotoBytes);

        CatalogueAssetRevision Revision(AssetId assetId, byte[] bytes)
        {
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(hash),
                bytes.Length,
                now,
                "image/jpeg",
                1200,
                800);
        }
    }

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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record SeededEvaluation(
        ProcessingRunId RunId,
        string SourceRoot,
        byte[] DetectedPhotoBytes);

    private sealed class DetectorEvaluationApiFactory : PhotoIdentityApiTestFactory
    {
        public DetectorEvaluationApiFactory(string databasePath, string sessionRoot)
            : base(
                databasePath,
                builder => builder.UseSetting("PhotoIdentity:DetectorEvaluationRoot", sessionRoot))
        {
        }
    }
}