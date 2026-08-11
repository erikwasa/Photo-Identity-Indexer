using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using OpenCvSharp;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewApplicationTests
{
    [Fact]
    public async Task Review_actions_persist_and_undo_restores_the_previous_state()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededReviewFace seeded = await SeedReviewFaceAsync(database, directory);
            SqliteReviewRepository repository = new(database);
            DateTimeOffset now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
            CatalogueReviewPerson person = await repository.CreatePersonAsync("Ada Lovelace", now);

            await repository.AssignAsync(seeded.Id, person.Id, "human:test", now.AddMinutes(1), "Confirmed manually.");
            CatalogueReviewFace assigned = Assert.IsType<CatalogueReviewFace>(await repository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Assigned, assigned.State);
            Assert.Equal(person, assigned.Person);

            await repository.RejectAsync(seeded.Id, "human:test", now.AddMinutes(2), "Temporary correction.");

            SqliteCatalogueDatabase restartedDatabase = new(databasePath);
            await restartedDatabase.InitializeAsync();
            SqliteReviewRepository restartedRepository = new(restartedDatabase);
            CatalogueReviewFace rejected = Assert.IsType<CatalogueReviewFace>(
                await restartedRepository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Rejected, rejected.State);
            Assert.Null(rejected.Person);

            CatalogueReviewAction firstUndo = Assert.IsType<CatalogueReviewAction>(
                await restartedRepository.UndoLatestAsync(
                    seeded.Id,
                    "human:test",
                    now.AddMinutes(3),
                    "Restore the prior assignment."));
            Assert.Equal(CatalogueReviewActionKinds.Undo, firstUndo.Kind);
            CatalogueReviewFace restored = Assert.IsType<CatalogueReviewFace>(
                await restartedRepository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Assigned, restored.State);
            Assert.Equal(person, restored.Person);

            _ = await restartedRepository.UndoLatestAsync(
                seeded.Id,
                "human:test",
                now.AddMinutes(4));
            CatalogueReviewFace unreviewed = Assert.IsType<CatalogueReviewFace>(
                await restartedRepository.GetFaceAsync(seeded.Id));
            Assert.Equal(CatalogueReviewStates.Unreviewed, unreviewed.State);
            Assert.Null(unreviewed.Person);

            IReadOnlyList<CatalogueReviewAction> actions = await restartedRepository.GetActionsAsync(seeded.Id);
            Assert.Equal(4, actions.Count);
            Assert.Equal(2, actions.Count(action => action.ReversedAtUtc is not null));
            Assert.Equal(2, actions.Count(action => action.Kind == CatalogueReviewActionKinds.Undo));

            SqliteIdentityCatalogueRepository identityRepository = new(restartedDatabase);
            CatalogueHumanLabel label = Assert.Single(await identityRepository.GetHumanLabelsAsync(seeded.Id));
            Assert.Equal(person.Id, label.PersonId);
            Assert.Equal("Confirmed manually.", label.Note);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Review_api_hides_internal_paths_streams_opaque_images_and_disables_caching()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededReviewFace seeded = await SeedReviewFaceAsync(database, directory);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

            using HttpResponseMessage galleryResponse = await client.GetAsync("/api/review/faces?state=all");
            galleryResponse.EnsureSuccessStatusCode();
            Assert.Contains(
                "no-store",
                galleryResponse.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            string galleryJson = await galleryResponse.Content.ReadAsStringAsync();
            Assert.Contains("secret-photo.jpg", galleryJson, StringComparison.Ordinal);
            Assert.DoesNotContain(seeded.SourceRoot, galleryJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(seeded.CropPath, galleryJson, StringComparison.OrdinalIgnoreCase);

            HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                "/api/review/people",
                new CreatePersonRequest("Grace Hopper"));
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            ReviewPersonResponse person = Assert.IsType<ReviewPersonResponse>(
                await createResponse.Content.ReadFromJsonAsync<ReviewPersonResponse>());

            HttpResponseMessage assignResponse = await client.PostAsJsonAsync(
                $"/api/review/faces/{seeded.Id}/assign",
                new AssignFaceRequest(person.Id, "pixel-reviewer", "Reviewed on phone."));
            assignResponse.EnsureSuccessStatusCode();

            using HttpResponseMessage detailsResponse = await client.GetAsync($"/api/review/faces/{seeded.Id}");
            detailsResponse.EnsureSuccessStatusCode();
            Assert.Contains(
                "no-store",
                detailsResponse.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            string detailsJson = await detailsResponse.Content.ReadAsStringAsync();
            Assert.Contains("Grace Hopper", detailsJson, StringComparison.Ordinal);
            Assert.Contains("pixel-reviewer", detailsJson, StringComparison.Ordinal);
            Assert.DoesNotContain(seeded.SourceRoot, detailsJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(seeded.CropPath, detailsJson, StringComparison.OrdinalIgnoreCase);

            using HttpResponseMessage imageResponse = await client.GetAsync($"/api/review/faces/{seeded.Id}/image");
            imageResponse.EnsureSuccessStatusCode();
            Assert.Contains(
                "no-store",
                imageResponse.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            byte[] imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();
            Assert.Equal(seeded.CropBytes, imageBytes);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Review_api_renders_proxy_backed_face_previews_without_upscaling()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string proxyRoot = Path.Combine(directory, "review-proxies");
            Directory.CreateDirectory(proxyRoot);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededReviewFace seeded = await SeedReviewFaceAsync(database, directory);
            const string profileId = "review-test-1600-q90";
            await SeedReviewProxyAsync(database, seeded.RevisionId, proxyRoot, profileId);

            await using ReviewApiFactory factory = new(databasePath, proxyRoot, profileId);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage galleryImageResponse = await client.GetAsync(
                $"/api/review/faces/{seeded.Id}/image?size=360");
            galleryImageResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", galleryImageResponse.Content.Headers.ContentType?.MediaType);
            byte[] galleryBytes = await galleryImageResponse.Content.ReadAsByteArrayAsync();
            Assert.NotEqual(seeded.CropBytes, galleryBytes);
            using Mat galleryImage = Cv2.ImDecode(galleryBytes, ImreadModes.Color);
            Assert.False(galleryImage.Empty());
            Assert.Equal(360, galleryImage.Cols);
            Assert.Equal(360, galleryImage.Rows);

            using HttpResponseMessage detailsResponse = await client.GetAsync($"/api/review/faces/{seeded.Id}");
            detailsResponse.EnsureSuccessStatusCode();
            ReviewFaceDetailsResponse details = Assert.IsType<ReviewFaceDetailsResponse>(
                await detailsResponse.Content.ReadFromJsonAsync<ReviewFaceDetailsResponse>());
            Assert.True(details.Face.ImageUrl.EndsWith("?size=960", StringComparison.Ordinal));
            Assert.DoesNotContain(proxyRoot, details.Face.ImageUrl, StringComparison.OrdinalIgnoreCase);

            using HttpResponseMessage detailsImageResponse = await client.GetAsync(details.Face.ImageUrl);
            detailsImageResponse.EnsureSuccessStatusCode();
            byte[] detailsBytes = await detailsImageResponse.Content.ReadAsByteArrayAsync();
            using Mat detailsImage = Cv2.ImDecode(detailsBytes, ImreadModes.Color);
            Assert.False(detailsImage.Empty());
            Assert.Equal(800, detailsImage.Cols);
            Assert.Equal(800, detailsImage.Rows);

            using HttpResponseMessage invalidSizeResponse = await client.GetAsync(
                $"/api/review/faces/{seeded.Id}/image?size=40");
            Assert.Equal(HttpStatusCode.BadRequest, invalidSizeResponse.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Review_api_streams_batch_relative_crops_from_the_processing_output_root()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededReviewFace seeded = await SeedReviewFaceAsync(
                database,
                directory,
                useBatchRelativeCropPath: true);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage imageResponse = await client.GetAsync($"/api/review/faces/{seeded.Id}/image");

            imageResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/png", imageResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(seeded.CropBytes, await imageResponse.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededReviewFace> SeedReviewFaceAsync(
        SqliteCatalogueDatabase database,
        string directory,
        bool useBatchRelativeCropPath = false)
    {
        DateTimeOffset now = new(2026, 7, 26, 7, 50, 0, TimeSpan.Zero);
        string sourceRoot = Path.Combine(directory, "private-photos");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "family"));
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "family", "secret-photo.jpg"), [1, 2, 3]);

        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(sourceId, "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(assetId, sourceId, "family/secret-photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string('a', 64)),
            3,
            now,
            "image/jpeg",
            1200,
            800);
        CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);

        FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
        byte[] cropBytes = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4];
        string cropPath;
        string storedCropPath;
        if (useBatchRelativeCropPath)
        {
            Guid runId = Guid.NewGuid();
            string outputRoot = Path.Combine(directory, "batch-output");
            storedCropPath = Path.Combine(
                    "runs",
                    runId.ToString(),
                    "assets",
                    persistedRevision.Id.ToString(),
                    "faces",
                    "face-001",
                    "aligned.png")
                .Replace('\\', '/');
            cropPath = Path.Combine(
                outputRoot,
                storedCropPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(cropPath)!);

            await using SqliteConnection runConnection = await database.OpenConnectionAsync();
            using SqliteCommand runCommand = runConnection.CreateCommand();
            runCommand.CommandText = """
                INSERT INTO processing_runs (
                    id,
                    status,
                    configuration_json,
                    started_at_utc,
                    completed_at_utc)
                VALUES (
                    $run_id,
                    'completed',
                    $configuration_json,
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
                VALUES (
                    $job_id,
                    $run_id,
                    $revision_id,
                    'succeeded',
                    1,
                    $created_at_utc,
                    $created_at_utc,
                    $created_at_utc,
                    $idempotency_key);
                """;
            runCommand.Parameters.AddWithValue("$run_id", runId.ToString());
            runCommand.Parameters.AddWithValue(
                "$configuration_json",
                JsonSerializer.Serialize(new { outputRoot }));
            runCommand.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
            runCommand.Parameters.AddWithValue("$job_id", Guid.NewGuid().ToString());
            runCommand.Parameters.AddWithValue("$revision_id", persistedRevision.Id.ToString());
            runCommand.Parameters.AddWithValue("$idempotency_key", $"review-test:{runId}:{persistedRevision.Id}");
            await runCommand.ExecuteNonQueryAsync();
        }
        else
        {
            string cropDirectory = Path.Combine(directory, "private-crops");
            Directory.CreateDirectory(cropDirectory);
            cropPath = Path.Combine(cropDirectory, "aligned-face.png");
            storedCropPath = cropPath;
        }

        await File.WriteAllBytesAsync(cropPath, cropBytes);
        string cropHash = Convert.ToHexString(SHA256.HashData(cropBytes)).ToLowerInvariant();

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($face_id, $revision_id, 0, $created_at_utc);

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
                '[0.25,0.20,0.40,0.60]',
                '[]',
                $created_at_utc);

            INSERT INTO face_crops (
                id,
                face_occurrence_id,
                crop_protocol,
                content_sha256,
                storage_path,
                width,
                height,
                created_at_utc)
            VALUES (
                $crop_id,
                $face_id,
                'review-test',
                $crop_hash,
                $crop_path,
                112,
                112,
                $created_at_utc);
            """;
        command.Parameters.AddWithValue("$face_id", occurrenceId.ToString());
        command.Parameters.AddWithValue("$revision_id", persistedRevision.Id.ToString());
        command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
        command.Parameters.AddWithValue("$model_hash", new string('b', 64));
        command.Parameters.AddWithValue("$crop_id", FaceCropId.New().ToString());
        command.Parameters.AddWithValue("$crop_hash", cropHash);
        command.Parameters.AddWithValue("$crop_path", storedCropPath);
        await command.ExecuteNonQueryAsync();

        return new SeededReviewFace(
            occurrenceId,
            persistedRevision.Id,
            sourceRoot,
            cropPath,
            cropBytes);
    }

    private static async Task SeedReviewProxyAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        string proxyRoot,
        string profileId)
    {
        DateTimeOffset now = new(2026, 7, 26, 7, 55, 0, TimeSpan.Zero);
        ReviewProxyProfile profile = new(profileId, 1600, 90);
        SqliteArchiveReviewProxyRepository repository = new(database);
        await repository.RegisterProfileAsync(profile, now);

        byte[] proxyBytes;
        using (Mat image = new(new Size(1200, 800), MatType.CV_8UC3, new Scalar(40, 60, 80)))
        {
            Cv2.Rectangle(image, new Rect(300, 160, 480, 480), new Scalar(210, 220, 230), thickness: -1);
            Cv2.ImEncode(
                ".jpg",
                image,
                out proxyBytes,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, 90));
        }

        string relativePath = $"{profileId}/{revisionId}.jpg";
        string physicalPath = Path.Combine(proxyRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllBytesAsync(physicalPath, proxyBytes);
        string proxyHash = Convert.ToHexString(SHA256.HashData(proxyBytes)).ToLowerInvariant();

        await repository.RecordCompletionAsync(new ArchiveReviewProxyRecord(
            revisionId,
            profileId,
            proxyBytes.LongLength,
            new Sha256Digest(proxyHash),
            1200,
            800,
            now,
            relativePath));
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

    private sealed record SeededReviewFace(
        FaceOccurrenceId Id,
        AssetRevisionId RevisionId,
        string SourceRoot,
        string CropPath,
        byte[] CropBytes);

    private sealed class ReviewApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string? _reviewProxyRoot;
        private readonly string? _reviewProxyProfileId;

        public ReviewApiFactory(
            string databasePath,
            string? reviewProxyRoot = null,
            string? reviewProxyProfileId = null)
        {
            _databasePath = databasePath;
            _reviewProxyRoot = reviewProxyRoot;
            _reviewProxyProfileId = reviewProxyProfileId;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            if (!string.IsNullOrWhiteSpace(_reviewProxyRoot))
            {
                builder.UseSetting("PhotoIdentity:ReviewProxyRoot", _reviewProxyRoot);
            }

            if (!string.IsNullOrWhiteSpace(_reviewProxyProfileId))
            {
                builder.UseSetting("PhotoIdentity:ReviewProxyProfileId", _reviewProxyProfileId);
            }
        }
    }
}
