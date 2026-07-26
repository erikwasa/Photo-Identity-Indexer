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
}
