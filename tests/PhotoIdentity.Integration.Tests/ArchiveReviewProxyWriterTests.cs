using System.Security.Cryptography;
using OpenCvSharp;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveReviewProxyWriterTests
{
    [Fact]
    public async Task Generate_writes_hashes_records_and_reuses_a_verified_proxy_without_source_access()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceRoot = Path.Combine(directory, "source");
            string derivativeRoot = Path.Combine(directory, "derivatives");
            Directory.CreateDirectory(sourceRoot);
            string sourcePath = Path.Combine(sourceRoot, "photo.jpg");
            await WriteTestJpegAsync(sourcePath);

            DateTimeOffset now = new(2026, 8, 8, 21, 0, 0, TimeSpan.Zero);
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
            CatalogueAsset asset = new(AssetId.New(), source.Id, "photo.jpg", now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                new FileInfo(sourcePath).Length,
                now,
                "image/jpeg",
                2000,
                1000);
            CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            ReviewProxyProfile profile = new("candidate-1600-q82", 1600, 82);
            ArchiveReviewProxyWriter writer = new(database);
            ArchiveReviewProxyRecord first = await writer.GenerateAsync(
                persistedRevision.Id,
                sourcePath,
                sourceRoot,
                derivativeRoot,
                profile,
                now.AddMinutes(1));

            string storedPath = Path.Combine(derivativeRoot, first.RelativePath);
            Assert.True(File.Exists(storedPath));
            byte[] storedBytes = await File.ReadAllBytesAsync(storedPath);
            Assert.Equal(storedBytes.LongLength, first.EncodedByteLength);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(storedBytes)).ToLowerInvariant(),
                first.ContentHash.ToString());
            Assert.Equal(1600, first.Width);
            Assert.Equal(800, first.Height);
            Assert.DoesNotContain("photo.jpg", first.RelativePath, StringComparison.OrdinalIgnoreCase);

            File.Delete(sourcePath);
            ArchiveReviewProxyRecord replay = await writer.GenerateAsync(
                persistedRevision.Id,
                sourcePath,
                sourceRoot,
                derivativeRoot,
                profile,
                now.AddMinutes(5));

            Assert.Equal(first, replay);
            Assert.Equal(now.AddMinutes(1), replay.GeneratedAtUtc);
            Assert.Equal(
                first,
                await new SqliteArchiveReviewProxyRepository(database).GetAsync(
                    persistedRevision.Id,
                    profile.Id));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                writer.GenerateAsync(
                    persistedRevision.Id,
                    sourcePath,
                    sourceRoot,
                    derivativeRoot,
                    new ReviewProxyProfile(profile.Id, 1600, 75),
                    now.AddMinutes(6)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Generate_rejects_a_derivative_root_inside_the_authoritative_source_root()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceRoot = Path.Combine(directory, "source");
            string sourcePath = Path.Combine(sourceRoot, "photo.jpg");
            string derivativeRoot = Path.Combine(sourceRoot, "generated");
            Directory.CreateDirectory(sourceRoot);
            await WriteTestJpegAsync(sourcePath);

            DateTimeOffset now = new(2026, 8, 8, 21, 0, 0, TimeSpan.Zero);
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
            CatalogueAsset asset = new(AssetId.New(), source.Id, "photo.jpg", now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                new FileInfo(sourcePath).Length,
                now,
                "image/jpeg",
                2000,
                1000);
            CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                new ArchiveReviewProxyWriter(database).GenerateAsync(
                    persistedRevision.Id,
                    sourcePath,
                    sourceRoot,
                    derivativeRoot,
                    new ReviewProxyProfile("candidate-1600-q82", 1600, 82),
                    now.AddMinutes(1)));

            Assert.Contains("outside the authoritative source root", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(derivativeRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task WriteTestJpegAsync(string path)
    {
        using Mat image = new(new Size(2000, 1000), MatType.CV_8UC3, new Scalar(25, 90, 170));
        Cv2.Rectangle(
            image,
            new Rect(250, 200, 1000, 500),
            new Scalar(220, 150, 30),
            thickness: 12);
        Cv2.ImEncode(
            ".jpg",
            image,
            out byte[] encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 94));
        await File.WriteAllBytesAsync(path, encoded);
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
}
