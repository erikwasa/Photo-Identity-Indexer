using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Source_Tests;

public sealed class OneDriveFileAttributeStatusProviderTests
{
    [Fact]
    public void Attributes_distinguish_local_online_only_and_hydrating_content()
    {
        Assert.Equal(
            AssetAvailability.Local,
            OneDriveFileAttributeStatusProvider.Classify(FileAttributes.Normal));
        Assert.Equal(
            AssetAvailability.OnlineOnly,
            OneDriveFileAttributeStatusProvider.Classify(FileAttributes.Offline));
        Assert.Equal(
            AssetAvailability.OnlineOnly,
            OneDriveFileAttributeStatusProvider.Classify(
                OneDriveFileAttributeStatusProvider.RecallOnDataAccess));
        Assert.Equal(
            AssetAvailability.Downloading,
            OneDriveFileAttributeStatusProvider.Classify(
                OneDriveFileAttributeStatusProvider.RecallOnOpen |
                OneDriveFileAttributeStatusProvider.Pinned));
    }

    [Fact]
    public void Files_on_demand_state_preserves_pin_ownership_signals()
    {
        OneDriveFilesOnDemandState pinnedLocal = WindowsOneDriveFilesOnDemandPlatform.Classify(
            WindowsOneDriveFilesOnDemandPlatform.Pinned);
        Assert.Equal(AssetAvailability.Local, pinnedLocal.Availability);
        Assert.True(pinnedLocal.IsPinned);
        Assert.False(pinnedLocal.IsUnpinned);

        OneDriveFilesOnDemandState onlineOnly = WindowsOneDriveFilesOnDemandPlatform.Classify(
            WindowsOneDriveFilesOnDemandPlatform.RecallOnDataAccess |
            WindowsOneDriveFilesOnDemandPlatform.Unpinned);
        Assert.Equal(AssetAvailability.OnlineOnly, onlineOnly.Availability);
        Assert.False(onlineOnly.IsPinned);
        Assert.True(onlineOnly.IsUnpinned);

        OneDriveFilesOnDemandState downloading = WindowsOneDriveFilesOnDemandPlatform.Classify(
            WindowsOneDriveFilesOnDemandPlatform.RecallOnOpen |
            WindowsOneDriveFilesOnDemandPlatform.Pinned);
        Assert.Equal(AssetAvailability.Downloading, downloading.Availability);
        Assert.True(downloading.IsPinned);
    }

    [Fact]
    public void Traversal_skips_reparse_directories_without_filtering_reparse_files()
    {
        Assert.True(OneDriveSyncAssetSource.ShouldTraverseDirectory(FileAttributes.Directory));
        Assert.False(OneDriveSyncAssetSource.ShouldTraverseDirectory(
            FileAttributes.Directory | FileAttributes.ReparsePoint));
    }
}
