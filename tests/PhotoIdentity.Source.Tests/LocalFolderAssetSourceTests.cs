using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.Local;
using Xunit;

namespace PhotoIdentity_Source_Tests;

public sealed class LocalFolderAssetSourceTests
{
    [Fact]
    public async Task Scan_reports_supported_and_unsupported_files_with_stable_keys()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            await File.WriteAllBytesAsync(Path.Combine(directory, "photo.JPG"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(directory, "nested", "portrait.png"), [4, 5]);
            await File.WriteAllTextAsync(Path.Combine(directory, "notes.txt"), "not an image");

            SourceId sourceId = SourceId.New();
            LocalFolderAssetSource source = new(sourceId, directory);

            LocalFolderScanReport report = await source.ScanAsync(new SourceScanOptions());

            Assert.Equal(2, report.Assets.Count);
            Assert.Equal("nested/portrait.png", report.Assets[0].Reference.ItemKey);
            Assert.Equal("image/png", report.Assets[0].MediaType);
            Assert.Equal("photo.JPG", report.Assets[1].Reference.ItemKey);
            Assert.Equal("image/jpeg", report.Assets[1].MediaType);
            UnsupportedSourceFile unsupported = Assert.Single(report.UnsupportedFiles);
            Assert.Equal("notes.txt", unsupported.RelativePath);
            Assert.Contains(".txt", unsupported.Reason, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Non_recursive_scan_does_not_include_nested_files()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            await File.WriteAllBytesAsync(Path.Combine(directory, "top.jpg"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(directory, "nested", "hidden.png"), [2]);

            LocalFolderAssetSource source = new(SourceId.New(), directory);
            LocalFolderScanReport report = await source.ScanAsync(
                new SourceScanOptions(Recursive: false));

            SourceAsset asset = Assert.Single(report.Assets);
            Assert.Equal("top.jpg", asset.RelativePath);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Content_and_availability_use_the_source_owned_relative_key()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string filePath = Path.Combine(directory, "photo.jpg");
            byte[] content = [9, 8, 7];
            await File.WriteAllBytesAsync(filePath, content);

            SourceId sourceId = SourceId.New();
            LocalFolderAssetSource source = new(sourceId, directory);
            SourceAssetReference reference = new(sourceId, "photo.jpg");

            Assert.Equal(
                AssetAvailability.Local,
                await source.GetAvailabilityAsync(reference, CancellationToken.None));
            await using Stream stream = await source.OpenContentAsync(reference, CancellationToken.None);
            using MemoryStream copy = new();
            await stream.CopyToAsync(copy);
            Assert.Equal(content, copy.ToArray());

            File.Delete(filePath);
            Assert.Equal(
                AssetAvailability.Unavailable,
                await source.GetAvailabilityAsync(reference, CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Source_rejects_cross_source_and_parent_traversal_references()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            LocalFolderAssetSource source = new(SourceId.New(), directory);

            await Assert.ThrowsAsync<ArgumentException>(
                () => source.OpenContentAsync(
                    new SourceAssetReference(SourceId.New(), "photo.jpg"),
                    CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(
                () => source.OpenContentAsync(
                    new SourceAssetReference(source.SourceId, "../outside.jpg"),
                    CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
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
}
