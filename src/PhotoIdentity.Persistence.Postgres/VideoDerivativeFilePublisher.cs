using System.Security.Cryptography;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Unregistered, explicit-call publication boundary for a future approved
/// playable-MP4 adapter. No encoding, FFmpeg invocation, hosted worker,
/// API endpoint or video enable command exists here.
/// </summary>
public sealed class VideoDerivativeFilePublisher(PostgresCatalogueDatabase database)
{
    public const string CacheMarker = ".photoidentity-video-derivative-root";
    public const string CacheMarkerContents = "wi0188-video-derivative-root-v1";

    /// <summary>
    /// Verify a completed app-owned output, then rename it within the SAME
    /// root/volume before committing receipt + success in one PostgreSQL
    /// transaction. The file is intentionally not served until a later
    /// independently revalidated consumer exists.
    ///
    /// A crash after rename but before commit can leave an UNREFERENCED file.
    /// Such files are never exposed; future controlled cleanup must reclaim
    /// them. A database-commit exception has an ambiguous outcome: preserve
    /// the moved bytes, never delete an artifact that might be committed.
    /// </summary>
    public async Task<bool> PublishAsync(
        VideoProcessingJobLease lease, string privateCacheRoot,
        string completedToolOutput, CancellationToken token = default,
        VideoProxyRetentionLimits? publicationQuota = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        publicationQuota?.Validate();
        if (lease.DerivativeKind != "PlayableProxy")
            throw new ArgumentException("Only approved playable-proxy contracts may use this publisher.");
        string root = await VideoDerivativeCacheGuard.ValidateAsync(
            database, privateCacheRoot, token);
        VideoScratchCapacityGuard.EnsureCapacity(root, lease.TemporaryBytes);

        if (!Path.IsPathFullyQualified(completedToolOutput))
            throw new ArgumentException("Video output must be an absolute owned path.");
        string file = Path.GetFullPath(completedToolOutput);
        string parent = Path.GetDirectoryName(file)
            ?? throw new IOException("Video tool output parent is missing.");
        string name = Path.GetFileName(parent);
        if (!IsToolWorkspace(name) ||
            !string.Equals(file, Path.Combine(parent, "output.mp4"), Comparison) ||
            !string.Equals(Path.GetDirectoryName(parent), root, Comparison))
            throw new InvalidOperationException("Only owned direct-child video tool output may be published.");
        EnsureNoReparse(parent);
        EnsureNoReparse(file);
        string workspaceMarker = Path.Combine(parent, VideoScratchOwnership.MarkerName);
        if (!File.Exists(workspaceMarker) ||
            (File.GetAttributes(workspaceMarker) & FileAttributes.ReparsePoint) != 0 ||
            await File.ReadAllTextAsync(workspaceMarker, token) != VideoScratchOwnership.MarkerContents)
            throw new InvalidOperationException("Video output workspace ownership cannot be verified.");


        // Open without sharing READ/WRITE to prevent an encoder or competing
        // local caller from mutating bytes through a normal Windows handle.
        // Sharing DELETE permits the SAME-volume rename while still open.
        await using FileStream locked = new(file, FileMode.Open, FileAccess.Read,
            FileShare.Delete, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long size = locked.Length;
        if (size is < 1 or > 2147483648L || size > lease.TemporaryBytes)
            throw new InvalidOperationException("Video derivative exceeds its bounded output reservation.");
        if (publicationQuota is not null &&
            size > publicationQuota.MaximumProxyBytes)
            throw new VideoProxyPublicationQuotaExceededException();
        string sha = Convert.ToHexString(
            await SHA256.HashDataAsync(locked, token)).ToLowerInvariant();
        token.ThrowIfCancellationRequested();

        // The publisher creates only this private, deterministic path. If a
        // previous crash left a file, fail closed; never overwrite or reuse it.
        string relative = VideoDerivativeArtifactPath.ForLease(lease);
        string directory = Path.Combine(root, "video-derivatives", lease.Id.ToString("N"));
        Directory.CreateDirectory(directory);
        EnsureNoReparse(directory);
        string destination = Path.Combine(directory, lease.LeaseToken.ToString("N") + ".mp4");
        File.Move(file, destination, overwrite: false);

        bool committed;
        try
        {
            committed = await new PostgresVideoPublishedDerivativeRepository(database)
                .TryCommitAsync(lease, relative, sha, size, token,
                    publicationQuota);
        }
        catch (VideoProxyPublicationQuotaExceededException)
        {
            // The PostgreSQL transaction has definitely NOT committed a
            // receipt. This exact owned destination may safely be reclaimed.
            // A different DB exception may have an ambiguous commit outcome,
            // and must instead preserve bytes for the stopped-lane janitor.
            EnsureNoReparse(directory);
            EnsureNoReparse(destination);
            File.Delete(destination);
            throw;
        }
        catch
        {
            // Commit outcome might be unknown. Never delete the moved bytes:
            // no endpoint serves uncommitted files, and a later janitor must
            // check live DB references before reclaiming an orphan.
            throw;
        }

        if (!committed)
        {
            // This is a known lease/refusal, not an ambiguous commit. The
            // unreferenced file can be reclaimed, but never touch sources.
            File.Delete(destination);
        }
        return committed;
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsToolWorkspace(string name) =>
        name.StartsWith("video-tool-", StringComparison.Ordinal) &&
        name.Length == "video-tool-".Length + 32 &&
        name.Skip("video-tool-".Length).All(
            c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void EnsureNoReparse(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null;
             part = Path.GetDirectoryName(part))
        {
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked video output is not permitted.");
            if (Path.GetPathRoot(part) == part) break;
        }
    }
}
