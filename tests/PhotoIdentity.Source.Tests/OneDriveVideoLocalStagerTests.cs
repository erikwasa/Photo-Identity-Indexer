using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Source_Tests;

public sealed class OneDriveVideoLocalStagerTests
{
    [Fact]
    public async Task Copies_only_local_revision_bytes_and_removes_private_copy()
    {
        string sourceRoot = TempRoot("video-stage-source");
        string scratch = TempRoot("video-stage-scratch");
        string original = Path.Combine(sourceRoot, "local.mp4");
        byte[] bytes = Enumerable.Range(0, 50000).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(original, bytes);
        try
        {
            SourceId id = SourceId.New();
            OneDriveVideoAssetSource source = new(id, sourceRoot);
            string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var stage = new OneDriveVideoLocalStager(source);
            int observations = 0;

            string stagedPath;
            await using (VideoStagedLocalCopy copy = await stage.StageAsync(
                new SourceAssetReference(id, "local.mp4"),
                bytes.Length, digest, scratch,
                _ => { observations++; return Task.FromResult(true); }))
            {
                stagedPath = copy.FilePath;
                Assert.True(File.Exists(stagedPath));
                Assert.Equal(bytes, await File.ReadAllBytesAsync(stagedPath));
                Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
                Assert.True(observations >= 2);
            }
            Assert.False(File.Exists(stagedPath));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally
        {
            Directory.Delete(sourceRoot, recursive: true);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_or_changed_hash_and_revoked_permission_leave_no_scratch()
    {
        string sourceRoot = TempRoot("video-stage-source");
        string scratch = TempRoot("video-stage-scratch");
        byte[] bytes = [1, 2, 3, 4, 5];
        await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "clip.mov"), bytes);
        try
        {
            SourceId id = SourceId.New();
            OneDriveVideoAssetSource source = new(id, sourceRoot);
            var stage = new OneDriveVideoLocalStager(source);
            SourceAssetReference reference = new(id, "clip.mov");
            await Assert.ThrowsAsync<IOException>(() =>
                stage.StageAsync(reference, bytes.Length, new string('0', 64),
                    scratch, _ => Task.FromResult(true)));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));

            int checks = 0;
            await Assert.ThrowsAsync<IOException>(() =>
                stage.StageAsync(reference, bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    scratch, _ => Task.FromResult(++checks == 1)));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(
                Path.Combine(sourceRoot, "clip.mov")));
        }
        finally
        {
            Directory.Delete(sourceRoot, recursive: true);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_source_overlapping_scratch_or_uncertain_availability()
    {
        string root = TempRoot("video-stage-overlap");
        byte[] bytes = [10, 20];
        string original = Path.Combine(root, "online.mp4");
        await File.WriteAllBytesAsync(original, bytes);
        try
        {
            SourceId id = SourceId.New();
            var local = new OneDriveVideoAssetSource(id, root);
            var stage = new OneDriveVideoLocalStager(local);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await Assert.ThrowsAsync<ArgumentException>(() => stage.StageAsync(
                new SourceAssetReference(id, "online.mp4"),
                bytes.Length, hash, root, _ => Task.FromResult(true)));
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string TempRoot(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(),
            prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
