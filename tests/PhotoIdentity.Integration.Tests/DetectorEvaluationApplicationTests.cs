using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
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
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            SeededEvaluation seeded = await SeedEvaluationAsync(database, directory);

            await using DetectorEvaluationApiFactory factory = new(databasePath);
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

    private sealed class DetectorEvaluationApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public DetectorEvaluationApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
