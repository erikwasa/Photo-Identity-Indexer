using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Source_Tests;

public sealed class OneDriveSyncAssetSourceTests
{
    [Fact]
    public async Task Scan_distinguishes_local_online_downloading_and_failed_availability()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string local = await WriteAsync(directory, "local.jpg", [1]);
            string online = await WriteAsync(directory, "online.png", [2]);
            string downloading = await WriteAsync(directory, "downloading.jpg", [3]);
            string failed = await WriteAsync(directory, "failed.jpeg", [4]);
            await File.WriteAllTextAsync(Path.Combine(directory, "notes.txt"), "unsupported");

            FakeStatusProvider statuses = new();
            statuses.Set(local, AssetAvailability.Local);
            statuses.Set(online, AssetAvailability.OnlineOnly);
            statuses.Set(downloading, AssetAvailability.Downloading);
            statuses.Set(failed, AssetAvailability.Error, "attribute read failed");
            OneDriveSyncAssetSource source = new(SourceId.New(), directory, statuses);

            OneDriveSyncScanReport report = await source.ScanAsync(new SourceScanOptions());

            Assert.Equal(4, report.Assets.Count);
            Assert.Equal(
                AssetAvailability.Local,
                report.Assets.Single(asset => asset.RelativePath == "local.jpg").Availability);
            Assert.Equal(
                AssetAvailability.OnlineOnly,
                report.Assets.Single(asset => asset.RelativePath == "online.png").Availability);
            Assert.Equal(
                AssetAvailability.Downloading,
                report.Assets.Single(asset => asset.RelativePath == "downloading.jpg").Availability);
            Assert.Equal(
                AssetAvailability.Error,
                report.Assets.Single(asset => asset.RelativePath == "failed.jpeg").Availability);
            Assert.Contains(
                report.AvailabilityFailures,
                failure => failure.RelativePath == "failed.jpeg" && failure.Error == "attribute read failed");
            Assert.Equal("notes.txt", Assert.Single(report.UnsupportedFiles).RelativePath);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Open_and_stage_refuse_online_only_content_without_hydrating_it()
    {
        string sourceDirectory = CreateTemporaryDirectory();
        string stagingDirectory = CreateTemporaryDirectory();
        try
        {
            string path = await WriteAsync(sourceDirectory, "online.jpg", [9, 8, 7]);
            FakeStatusProvider statuses = new();
            statuses.Set(path, AssetAvailability.OnlineOnly);
            SourceId sourceId = SourceId.New();
            OneDriveSyncAssetSource source = new(sourceId, sourceDirectory, statuses);
            SourceAssetReference asset = new(sourceId, "online.jpg");

            OneDriveHydrationRequiredException openException = await Assert.ThrowsAsync<OneDriveHydrationRequiredException>(
                () => source.OpenContentAsync(asset, CancellationToken.None));
            Assert.Equal(AssetAvailability.OnlineOnly, openException.Availability);

            OneDriveSyncAssetStager stager = new(source);
            await Assert.ThrowsAsync<OneDriveHydrationRequiredException>(
                () => stager.StageAsync(asset, new StagingOptions(stagingDirectory), CancellationToken.None));
            Assert.Empty(Directory.EnumerateFileSystemEntries(stagingDirectory));
        }
        finally
        {
            DeleteTemporaryDirectory(sourceDirectory);
            DeleteTemporaryDirectory(stagingDirectory);
        }
    }

    [Fact]
    public async Task Stage_creates_verified_content_fingerprinted_copy_and_reuses_it()
    {
        string sourceDirectory = CreateTemporaryDirectory();
        string stagingDirectory = CreateTemporaryDirectory();
        try
        {
            byte[] content = [1, 3, 3, 7, 9];
            await WriteAsync(sourceDirectory, "family.JPG", content);
            SourceId sourceId = SourceId.New();
            OneDriveSyncAssetSource source = new(sourceId, sourceDirectory);
            OneDriveSyncAssetStager stager = new(source);
            SourceAssetReference asset = new(sourceId, "family.JPG");
            StagingOptions options = new(stagingDirectory);

            StagedAsset first = await stager.StageAsync(asset, options, CancellationToken.None);
            StagedAsset second = await stager.StageAsync(asset, options, CancellationToken.None);

            string expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            string fileName = Path.GetFileName(first.LocalPath);
            Assert.Equal(expectedHash, first.ContentHash.ToString());
            Assert.Equal(content.Length, first.SizeBytes);
            Assert.Equal(first.LocalPath, second.LocalPath);
            Assert.StartsWith($"{expectedHash}-", fileName, StringComparison.Ordinal);
            Assert.EndsWith(".jpg", fileName, StringComparison.Ordinal);
            Assert.Equal(content, await File.ReadAllBytesAsync(first.LocalPath));
            Assert.True(File.Exists(first.LocalPath + OneDriveSyncAssetStager.VerificationManifestSuffix));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(stagingDirectory),
                path => path.EndsWith(".partial", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTemporaryDirectory(sourceDirectory);
            DeleteTemporaryDirectory(stagingDirectory);
        }
    }

    [Fact]
    public async Task Cleanup_deletes_only_a_currently_verified_staging_copy()
    {
        string sourceDirectory = CreateTemporaryDirectory();
        string stagingDirectory = CreateTemporaryDirectory();
        try
        {
            string sourcePath = await WriteAsync(sourceDirectory, "family.png", [5, 4, 3, 2, 1]);
            SourceId sourceId = SourceId.New();
            OneDriveSyncAssetSource source = new(sourceId, sourceDirectory);
            OneDriveSyncAssetStager stager = new(source);
            StagingOptions options = new(stagingDirectory);
            StagedAsset staged = await stager.StageAsync(
                new SourceAssetReference(sourceId, "family.png"),
                options,
                CancellationToken.None);

            await stager.CleanupAsync(staged, options, CancellationToken.None);

            Assert.False(File.Exists(staged.LocalPath));
            Assert.False(File.Exists(staged.LocalPath + OneDriveSyncAssetStager.VerificationManifestSuffix));
            Assert.True(File.Exists(sourcePath));
        }
        finally
        {
            DeleteTemporaryDirectory(sourceDirectory);
            DeleteTemporaryDirectory(stagingDirectory);
        }
    }

    [Fact]
    public async Task Cleanup_refuses_tampered_unverified_and_source_paths()
    {
        string sourceDirectory = CreateTemporaryDirectory();
        string stagingDirectory = CreateTemporaryDirectory();
        try
        {
            string sourcePath = await WriteAsync(sourceDirectory, "family.jpg", [7, 7, 7]);
            SourceId sourceId = SourceId.New();
            OneDriveSyncAssetSource source = new(sourceId, sourceDirectory);
            OneDriveSyncAssetStager stager = new(source);
            StagingOptions options = new(stagingDirectory);
            SourceAssetReference reference = new(sourceId, "family.jpg");
            StagedAsset staged = await stager.StageAsync(reference, options, CancellationToken.None);
            await File.WriteAllBytesAsync(staged.LocalPath, [0]);

            await Assert.ThrowsAsync<StagingVerificationException>(
                () => stager.CleanupAsync(staged, options, CancellationToken.None));
            Assert.True(File.Exists(staged.LocalPath));
            Assert.True(File.Exists(sourcePath));

            string arbitraryPath = Path.Combine(stagingDirectory, "arbitrary.jpg");
            byte[] arbitrary = [6, 6];
            await File.WriteAllBytesAsync(arbitraryPath, arbitrary);
            StagedAsset unverified = new(
                reference,
                arbitraryPath,
                arbitrary.Length,
                new Sha256Digest(Convert.ToHexString(SHA256.HashData(arbitrary)).ToLowerInvariant()));
            await Assert.ThrowsAsync<StagingVerificationException>(
                () => stager.CleanupAsync(unverified, options, CancellationToken.None));
            Assert.True(File.Exists(arbitraryPath));

            StagedAsset sourceAsStage = new(
                reference,
                sourcePath,
                3,
                new Sha256Digest(Convert.ToHexString(SHA256.HashData([7, 7, 7])).ToLowerInvariant()));
            await Assert.ThrowsAsync<StagingVerificationException>(
                () => stager.CleanupAsync(sourceAsStage, options, CancellationToken.None));
            Assert.True(File.Exists(sourcePath));
        }
        finally
        {
            DeleteTemporaryDirectory(sourceDirectory);
            DeleteTemporaryDirectory(stagingDirectory);
        }
    }

    [Fact]
    public async Task Staging_requires_verification_and_a_directory_outside_the_source_root()
    {
        string sourceDirectory = CreateTemporaryDirectory();
        string stagingDirectory = CreateTemporaryDirectory();
        try
        {
            await WriteAsync(sourceDirectory, "family.jpg", [1, 2, 3]);
            SourceId sourceId = SourceId.New();
            OneDriveSyncAssetSource source = new(sourceId, sourceDirectory);
            OneDriveSyncAssetStager stager = new(source);
            SourceAssetReference reference = new(sourceId, "family.jpg");

            await Assert.ThrowsAsync<ArgumentException>(
                () => stager.StageAsync(
                    reference,
                    new StagingOptions(Path.Combine(sourceDirectory, "staging")),
                    CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(
                () => stager.StageAsync(
                    reference,
                    new StagingOptions(stagingDirectory, verifyContentHash: false),
                    CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryDirectory(sourceDirectory);
            DeleteTemporaryDirectory(stagingDirectory);
        }
    }

    private static async Task<string> WriteAsync(string directory, string relativePath, byte[] content)
    {
        string path = Path.Combine(directory, relativePath);
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllBytesAsync(path, content);
        return path;
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Source.Tests",
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

    private sealed class FakeStatusProvider : IOneDriveFileStatusProvider
    {
        private readonly Dictionary<string, OneDriveFileStatus> _statuses = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public OneDriveFileStatus GetStatus(string path) =>
            _statuses.TryGetValue(Path.GetFullPath(path), out OneDriveFileStatus? status)
                ? status
                : new OneDriveFileStatus(File.Exists(path) ? AssetAvailability.Local : AssetAvailability.Unavailable);

        public void Set(string path, AssetAvailability availability, string? error = null) =>
            _statuses[Path.GetFullPath(path)] = new OneDriveFileStatus(availability, error);
    }
}
