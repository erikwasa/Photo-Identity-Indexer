using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Source_Tests;

public sealed class HeicSourceRecognitionTests
{
    [Fact]
    public async Task Local_and_OneDrive_scanners_recognize_heic_and_heif_but_not_unverified_raw()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "iphone.heic"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(directory, "export.HEIF"), [2]);
            await File.WriteAllBytesAsync(Path.Combine(directory, "future.dng"), [3]);

            LocalFolderAssetSource local = new(SourceId.New(), directory);
            LocalFolderScanReport localReport = await local.ScanAsync(new SourceScanOptions());

            Assert.Equal(2, localReport.Assets.Count);
            Assert.Equal("image/heic", localReport.Assets.Single(asset => asset.RelativePath == "iphone.heic").MediaType);
            Assert.Equal("image/heif", localReport.Assets.Single(asset => asset.RelativePath == "export.HEIF").MediaType);
            Assert.Equal("future.dng", Assert.Single(localReport.UnsupportedFiles).RelativePath);

            OneDriveSyncAssetSource oneDrive = new(SourceId.New(), directory);
            OneDriveSyncScanReport oneDriveReport = await oneDrive.ScanAsync(new SourceScanOptions());

            Assert.Equal(2, oneDriveReport.Assets.Count);
            Assert.Equal("image/heic", oneDriveReport.Assets.Single(asset => asset.RelativePath == "iphone.heic").MediaType);
            Assert.Equal("image/heif", oneDriveReport.Assets.Single(asset => asset.RelativePath == "export.HEIF").MediaType);
            Assert.Equal("future.dng", Assert.Single(oneDriveReport.UnsupportedFiles).RelativePath);
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
