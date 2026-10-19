using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.OneDriveSync;

/// <summary>
/// A verified, private local copy of an already hydrated video. Only the
/// staging directory is deleted: the original is never opened for writing.
/// </summary>
public sealed class VideoStagedLocalCopy(string directory, string filePath)
    : IAsyncDisposable
{
    public string FilePath { get; } = filePath;

    public async ValueTask DisposeAsync()
    {
        // The caller must have stopped all child tools before disposing.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 39)
            {
                await Task.Delay(125);
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && attempt < 39)
            {
                await Task.Delay(125);
            }
        }
    }
}

/// <summary>
/// Explicit-call input stage for verified local MOV/MP4 only. Makes a bounded,
/// hash-checked private copy so FFmpeg never opens the original OneDrive path.
/// A safety probe is required before and after copying; the worker must
/// continue polling it during processing. This class does not authorize
/// a job, start a tool or publish a derivative.
/// </summary>
public sealed class OneDriveVideoLocalStager(OneDriveVideoAssetSource source)
{
    public async Task<VideoStagedLocalCopy> StageAsync(
        SourceAssetReference reference,
        long expectedBytes,
        string expectedSha256,
        string scratchRoot,
        Func<CancellationToken, Task<bool>> authorized,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(authorized);
        if (expectedBytes is < 1 or > 1024L * 1024 * 1024 ||
            expectedSha256 is null || expectedSha256.Length != 64 ||
            expectedSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Bounded video source evidence is invalid.");
        if (!Path.IsPathFullyQualified(scratchRoot) || !Directory.Exists(scratchRoot))
            throw new ArgumentException("An existing absolute scratch root is required.");
        if (OperatingSystem.IsWindows() && scratchRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Remote scratch roots are not permitted.");

        string local = source.GetLocalPath(reference); // refuses online-only sources
        if (!File.Exists(local) || new FileInfo(local).LinkTarget is not null)
            throw new IOException("Source video is missing or linked.");
        AssertSafeAncestors(local, directory: false);
        AssertSafeAncestors(source.RootPath, directory: true);
        AssertSafeAncestors(scratchRoot, directory: true);
        if (Overlaps(source.RootPath, scratchRoot))
            throw new ArgumentException("Scratch cannot overlap the source archive.");
        if (!await authorized(token))
            throw new InvalidOperationException("Video source permission was revoked.");
        if (await source.GetAvailabilityAsync(reference, token) != AssetAvailability.Local)
            throw new IOException("Video source is not fully local.");

        FileInfo observation = new(local);
        if (observation.Length != expectedBytes)
            throw new IOException("Video source size differs from the leased revision.");
        DateTime beforeUtc = observation.LastWriteTimeUtc;

        string staging = VideoScratchOwnership.Create(scratchRoot, "video-input");
        string destination = Path.Combine(staging, "input" +
            Path.GetExtension(local).ToLowerInvariant());
        try
        {
            await using (FileStream from = OpenWithoutRecall(local))
            await using (FileStream to = new(destination, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, 65536,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] block = ArrayPool<byte>.Shared.Rent(65536);
                try
                {
                    long copied = 0;
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        int count = await from.ReadAsync(block.AsMemory(0, 65536), token);
                        if (count == 0) break;
                        copied += count;
                        if (copied > expectedBytes)
                            throw new IOException("Video copy exceeded the leased source size.");
                        await to.WriteAsync(block.AsMemory(0, count), token);
                        hash.AppendData(block.AsSpan(0, count));
                    }
                    if (copied != expectedBytes ||
                        !string.Equals(
                            Convert.ToHexString(hash.GetHashAndReset()),
                            expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Video content no longer matches the leased revision.");
                    await to.FlushAsync(token);
                }
                finally { ArrayPool<byte>.Shared.Return(block); }
            }

            observation.Refresh();
            if (!observation.Exists || observation.Length != expectedBytes ||
                observation.LastWriteTimeUtc != beforeUtc ||
                await source.GetAvailabilityAsync(reference, token) != AssetAvailability.Local ||
                !await authorized(token))
                throw new IOException("Video source changed or permission was revoked while copying.");
            return new VideoStagedLocalCopy(staging, destination);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static FileStream OpenWithoutRecall(string path)
    {
        if (!OperatingSystem.IsWindows())
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // FILE_FLAG_OPEN_NO_RECALL prevents network/cloud retrieval requests for
        // this handle. If a Cloud Files provider cannot serve local bytes,
        // reads fail rather than intentionally issuing a hydration request.
        SafeFileHandle handle = CreateFileW(path, 0x80000000, 0x1, IntPtr.Zero,
            3, 0x00100000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException("Already-local video bytes could not be opened without recall.");
        }
        return new FileStream(handle, FileAccess.Read, 65536, isAsync: false);
    }

    private static bool Overlaps(string root, string scratch)
    {
        StringComparison cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string a = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string b = Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar);
        return a.Equals(b, cmp) ||
            a.StartsWith(b + Path.DirectorySeparatorChar, cmp) ||
            b.StartsWith(a + Path.DirectorySeparatorChar, cmp);
    }

    private static void AssertSafeAncestors(string path, bool directory)
    {
        string? cursor = Path.GetFullPath(directory
            ? path : Path.GetDirectoryName(path) ?? path);
        while (cursor is not null)
        {
            if (new DirectoryInfo(cursor).LinkTarget is not null)
                throw new IOException("Video source or scratch traverses a directory link.");
            if (Path.GetPathRoot(cursor) == cursor) break;
            cursor = Path.GetDirectoryName(cursor);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access,
        uint share, IntPtr security, uint creation, uint flags, IntPtr templateFile);
}
