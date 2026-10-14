using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Source_Tests;

public sealed class OneDriveVideoAssetSourceTests
{
    [Fact]
    public async Task Video_source_is_separate_from_default_photo_source_and_preserves_source_keys()
    {
        string root = NewRoot();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "sample.MOV"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(root, "sample.mp4"), [4, 5, 6]);
            await File.WriteAllBytesAsync(Path.Combine(root, "photo.jpg"), [7, 8, 9]);
            await File.WriteAllBytesAsync(Path.Combine(root, "notes.txt"), [0]);
            SourceId sourceId = SourceId.New();
            OneDriveVideoAssetSource videos = new(sourceId, root);
            OneDriveSyncAssetSource photos = new(sourceId, root);

            List<SourceAsset> discovered = [];
            await foreach (SourceAsset video in videos.EnumerateAsync(
                new SourceScanOptions(), CancellationToken.None))
                discovered.Add(video);

            Assert.Equal(2, discovered.Count);
            Assert.Contains(discovered, v => v.RelativePath == "sample.MOV" &&
                                               v.MediaType == "video/quicktime" &&
                                               v.Reference.SourceId == sourceId);
            Assert.Contains(discovered, v => v.RelativePath == "sample.mp4" &&
                                               v.MediaType == "video/mp4");
            OneDriveSyncScanReport photoReport = await photos.ScanAsync(new SourceScanOptions());
            Assert.Single(photoReport.Assets);
            Assert.Equal("photo.jpg", photoReport.Assets[0].RelativePath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Online_only_video_is_enumerated_but_cannot_be_opened_or_used_by_pilot()
    {
        string root = NewRoot();
        try
        {
            string videoPath = Path.Combine(root, "online.mov");
            await File.WriteAllBytesAsync(videoPath, [1, 2, 3]);
            SourceId id = SourceId.New();
            OneDriveVideoAssetSource video = new(id, root,
                new FakeStatus(videoPath, AssetAvailability.OnlineOnly));
            SourceAssetReference key = new(id, "online.mov");

            Assert.Equal(AssetAvailability.OnlineOnly,
                await video.GetAvailabilityAsync(key, CancellationToken.None));
            await Assert.ThrowsAsync<OneDriveHydrationRequiredException>(() =>
                video.OpenContentAsync(key, CancellationToken.None));
            Assert.Throws<IOException>(() => video.GetLocalPath(key));
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(videoPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Video_local_path_rejects_traversal_other_sources_and_non_video_files()
    {
        string root = NewRoot();
        try
        {
            OneDriveVideoAssetSource source = new(SourceId.New(), root);
            Assert.Throws<ArgumentException>(() =>
                source.GetLocalPath(new SourceAssetReference(source.SourceId, "../outside.mp4")));
            Assert.Throws<ArgumentException>(() =>
                source.GetLocalPath(new SourceAssetReference(SourceId.New(), "inside.mp4")));
            Assert.Throws<ArgumentException>(() =>
                source.GetLocalPath(new SourceAssetReference(source.SourceId, "portrait.jpg")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "photoidentity-video-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeStatus(string path, AssetAvailability status) : IOneDriveFileStatusProvider
    {
        public OneDriveFileStatus GetStatus(string value) =>
            new(Path.GetFullPath(value) == Path.GetFullPath(path) ? status : AssetAvailability.Unavailable);
    }
}
