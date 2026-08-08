using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenCvSharp;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CollectionProxyContentApplicationTests
{
    [Fact]
    public async Task Preview_and_thumbnail_use_proxy_when_original_is_not_local()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceRoot = Path.Combine(directory, "source");
            string proxyRoot = Path.Combine(directory, "derivatives");
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(proxyRoot);

            DateTimeOffset now = new(2026, 8, 9, 0, 15, 0, TimeSpan.Zero);
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
            CatalogueAsset asset = new(AssetId.New(), source.Id, "family/online-only.jpg", now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                12_345,
                now,
                "image/jpeg",
                2400,
                1600);
            CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            ReviewProxyProfile profile = new("jpeg-1600-q78", 1600, 78);
            byte[] proxyBytes = CreateJpeg();
            string relativeProxyPath = Path.Combine(
                "review-proxies",
                profile.Id,
                $"{persistedRevision.Id}.jpg");
            string proxyPath = Path.Combine(proxyRoot, relativeProxyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)!);
            await File.WriteAllBytesAsync(proxyPath, proxyBytes);

            SqliteArchiveReviewProxyRepository proxyRepository = new(database);
            await proxyRepository.RegisterProfileAsync(profile, now);
            await proxyRepository.RecordCompletionAsync(new ArchiveReviewProxyRecord(
                persistedRevision.Id,
                profile.Id,
                proxyBytes.LongLength,
                new Sha256Digest(Convert.ToHexString(SHA256.HashData(proxyBytes)).ToLowerInvariant()),
                1600,
                1067,
                now,
                relativeProxyPath));

            await using CollectionProxyApiFactory factory = new(
                databasePath,
                proxyRoot,
                profile.Id);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage preview = await client.GetAsync(
                $"/api/collections/photos/{persistedRevision.Id}/preview");
            preview.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", preview.Content.Headers.ContentType?.MediaType);
            Assert.Equal(proxyBytes, await preview.Content.ReadAsByteArrayAsync());

            using HttpResponseMessage thumbnail = await client.GetAsync(
                $"/api/collections/photos/{persistedRevision.Id}/thumbnail");
            thumbnail.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType?.MediaType);
            Assert.NotEmpty(await thumbnail.Content.ReadAsByteArrayAsync());

            using HttpResponseMessage original = await client.GetAsync(
                $"/api/collections/photos/{persistedRevision.Id}/original");
            Assert.Equal(HttpStatusCode.NotFound, original.StatusCode);

            using HttpResponseMessage legacyContent = await client.GetAsync(
                $"/api/collections/photos/{persistedRevision.Id}/content");
            Assert.Equal(HttpStatusCode.NotFound, legacyContent.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static byte[] CreateJpeg()
    {
        using Mat image = new(new Size(1600, 1067), MatType.CV_8UC3, new Scalar(40, 80, 120));
        Cv2.Rectangle(
            image,
            new Rect(250, 180, 800, 500),
            new Scalar(180, 120, 40),
            thickness: 8);
        Cv2.ImEncode(
            ".jpg",
            image,
            out byte[] encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 78));
        return encoded;
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

    private sealed class CollectionProxyApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string _proxyRoot;
        private readonly string _profileId;

        public CollectionProxyApiFactory(
            string databasePath,
            string proxyRoot,
            string profileId)
        {
            _databasePath = databasePath;
            _proxyRoot = proxyRoot;
            _profileId = profileId;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting("PhotoIdentity:ReviewProxyRoot", _proxyRoot);
            builder.UseSetting("PhotoIdentity:ReviewProxyProfileId", _profileId);
        }
    }
}
