using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoCaptureMetadataTests
{
    [Fact]
    public void Camera_time_remains_unspecified()
    {
        PhotoCaptureMetadata metadata = new(new DateTime(2025, 5, 10, 13, 45, 22, DateTimeKind.Local));
        Assert.Equal(DateTimeKind.Unspecified, metadata.TakenAtLocal!.Value.Kind);
    }
}
