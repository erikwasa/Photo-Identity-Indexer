using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCvSharp;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewFaceDetailImageApplicationTests
{
    [Fact]
    public async Task Face_details_prefers_verified_local_original_and_exposes_containing_revision()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SeededFace seeded = await SeedAsync(directory);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.Local, false, false));

            await using ReviewApiFactory factory = new(
                seeded.DatabasePath,
                seeded.ProxyRoot,
                seeded.ProfileId,
                platform);
            using HttpClient client = factory.CreateClient();

            ReviewFaceDetailsResponse details = await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                $"/api/review/faces/{seeded.FaceId}")
                ?? throw new InvalidOperationException("Face details response was empty.");

            Assert.Equal(seeded.RevisionId.ToString(), details.AssetRevisionId);
            Assert.EndsWith("?size=960", details.Face.ImageUrl, StringComparison.Ordinal);

            using HttpResponseMessage galleryResponse = await client.GetAsync(
                $"/api/review/faces/{seeded.FaceId}/image?size=360");
            galleryResponse.EnsureSuccessStatusCode();
            using Mat gallery = Cv2.ImDecode(
                await galleryResponse.Content.ReadAsByteArrayAsync(),
                ImreadModes.Color);
            Assert.Equal(360, gallery.Cols);
            Assert.Equal(360, gallery.Rows);

            using HttpResponseMessage detailsImageResponse = await client.GetAsync(details.Face.ImageUrl);
            detailsImageResponse.EnsureSuccessStatusCode();
            using Mat detailsImage = Cv2.ImDecode(
                await detailsImageResponse.Content.ReadAsByteArrayAsync(),
                ImreadModes.Color);
            Assert.Equal(960, detailsImage.Cols);
            Assert.Equal(960, detailsImage.Rows);
            Assert.Equal(0, platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Face_details_does_not_hydrate_online_only_original_and_falls_back_to_review_proxy()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SeededFace seeded = await SeedAsync(directory);
            FakeFilesOnDemandPlatform platform = new(
                new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true));

            await using ReviewApiFactory factory = new(
                seeded.DatabasePath,
                seeded.ProxyRoot,
                seeded.ProfileId,
                platform);
            using HttpClient client = factory.CreateClient();

            ReviewFaceDetailsResponse details = await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                $"/api/review/faces/{seeded.FaceId}")
                ?? throw new InvalidOperationException("Face details response was empty.");
            using HttpResponseMessage detailsImageResponse = await client.GetAsync(details.Face.ImageUrl);
            detailsImageResponse.EnsureSuccessStatusCode();
            using Mat detailsImage = Cv2.ImDecode(
                await detailsImageResponse.Content.ReadAsByteArrayAsync(),
                ImreadModes.Color);

            Assert.Equal(800, detailsImage.Cols);
            Assert.Equal(800, detailsImage.Rows);
            Assert.Equal(0, platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededFace> SeedAsync(string directory)
    {
        string databasePath = Path.Combine(directory, "catalogue.db");
        string sourceRoot = Path.Combine(directory, "private-photos");
        string sourceDirectory = Path.Combine(sourceRoot, "family");
        string sourcePath = Path.Combine(sourceDirectory, "photo.jpg");
        string proxyRoot = Path.Combine(directory, "review-proxies");
        const string profileId = "detail-test-1600-q90";
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(proxyRoot);

        byte[] originalBytes;
        using (Mat original = new(new Size(2400, 1600), MatType.CV_8UC3, new Scalar(25, 65, 105)))
        {
            Cv2.Rectangle(original, new Rect(600, 320, 960, 960), new Scalar(190, 110, 45), thickness: -1);
            Cv2.ImEncode(
                ".jpg",
                original,
                out originalBytes,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, 94));
        }
        await File.WriteAllBytesAsync(sourcePath, originalBytes);

        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 8, 15, 7, 30, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, "family/photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant()),
            originalBytes.LongLength,
            now,
            "image/jpeg",
            2400,
            1600);
        CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);

        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        await using (SqliteConnection connection = await database.OpenConnectionAsync())
        {
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
                    'detail-test-detector',
                    $model_hash,
                    0.98,
                    '[0.25,0.20,0.40,0.60]',
                    '[]',
                    $created_at_utc);
                """;
            command.Parameters.AddWithValue("$face_id", faceId.ToString());
            command.Parameters.AddWithValue("$revision_id", persistedRevision.Id.ToString());
            command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
            command.Parameters.AddWithValue("$model_hash", new string('b', 64));
            await command.ExecuteNonQueryAsync();
        }

        ReviewProxyProfile profile = new(profileId, 1600, 90);
        SqliteArchiveReviewProxyRepository proxyRepository = new(database);
        await proxyRepository.RegisterProfileAsync(profile, now);
        byte[] proxyBytes;
        using (Mat proxy = new(new Size(1200, 800), MatType.CV_8UC3, new Scalar(45, 75, 95)))
        {
            Cv2.ImEncode(
                ".jpg",
                proxy,
                out proxyBytes,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, 90));
        }

        string relativeProxyPath = $"{profileId}/{persistedRevision.Id}.jpg";
        string physicalProxyPath = Path.Combine(
            proxyRoot,
            relativeProxyPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalProxyPath)!);
        await File.WriteAllBytesAsync(physicalProxyPath, proxyBytes);
        await proxyRepository.RecordCompletionAsync(new ArchiveReviewProxyRecord(
            persistedRevision.Id,
            profileId,
            proxyBytes.LongLength,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(proxyBytes)).ToLowerInvariant()),
            1200,
            800,
            now,
            relativeProxyPath));

        return new SeededFace(
            databasePath,
            proxyRoot,
            profileId,
            faceId,
            persistedRevision.Id);
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

    private sealed record SeededFace(
        string DatabasePath,
        string ProxyRoot,
        string ProfileId,
        FaceOccurrenceId FaceId,
        AssetRevisionId RevisionId);

    private sealed class ReviewApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string _proxyRoot;
        private readonly string _profileId;
        private readonly FakeFilesOnDemandPlatform _platform;

        public ReviewApiFactory(
            string databasePath,
            string proxyRoot,
            string profileId,
            FakeFilesOnDemandPlatform platform)
        {
            _databasePath = databasePath;
            _proxyRoot = proxyRoot;
            _profileId = profileId;
            _platform = platform;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting("PhotoIdentity:ReviewProxyRoot", _proxyRoot);
            builder.UseSetting("PhotoIdentity:ReviewProxyProfileId", _profileId);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOneDriveFilesOnDemandPlatform>();
                services.AddSingleton<IOneDriveFilesOnDemandPlatform>(_platform);
            });
        }
    }

    private sealed class FakeFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        public FakeFilesOnDemandPlatform(OneDriveFilesOnDemandState state)
        {
            State = state;
        }

        public OneDriveFilesOnDemandState State { get; set; }
        public int HydrationRequests { get; private set; }

        public OneDriveFilesOnDemandState GetState(string path) => State;

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HydrationRequests++;
            State = new OneDriveFilesOnDemandState(AssetAvailability.Downloading, true, false);
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);
            return Task.CompletedTask;
        }
    }
}
