using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

public sealed record VideoManagedHydrationRecoverySummary(
    int Inspected, int Reconciled, int Retained, int Failed);

/// <summary>
/// WI-0207 operator-started recovery of durable video release receipts.
/// The only permitted mutation is marking an already-requested release
/// complete AFTER Files On-Demand reports the original is online-only.
/// Never issues a new hydration, pin, unpin, eviction or original-byte read.
/// Unknown, pinned, corrupt and unreachable states retain ownership.
/// </summary>
public sealed class VideoManagedHydrationRecovery(
    IArchiveHydrationRepository ownership,
    IOneDriveFilesOnDemandPlatform platform,
    TimeProvider timeProvider)
{
    public const int MaximumReceiptsPerAdvance = 16;

    public async Task<VideoManagedHydrationRecoverySummary> ReconcileAsync(
        CancellationToken token = default)
    {
        IReadOnlyList<ArchiveManagedHydrationLeaseState> pending =
            await ownership.GetPendingVideoReleasesAsync(
                MaximumReceiptsPerAdvance, token);

        int reconciled = 0, retained = 0, failed = 0;
        foreach (ArchiveManagedHydrationLeaseState lease in pending)
        {
            token.ThrowIfCancellationRequested();
            string? sourcePath = TryResolveMetadataPath(
                lease.RootLocator, lease.SourceKey);
            if (sourcePath is null)
            {
                retained++;
                continue;
            }

            try
            {
                // GetState reads Windows metadata, not the original bytes.
                OneDriveFilesOnDemandState state = platform.GetState(sourcePath);
                if (CanCloseReleaseReceipt(lease, state))
                {
                    await ownership.MarkReleasedAsync(
                        lease.AssetRevisionId, timeProvider.GetUtcNow(), token);
                    reconciled++;
                }
                else
                {
                    retained++;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // No private paths, identifiers, platform error strings or
                // original-content information escape this aggregate result.
                failed++;
            }
        }

        return new VideoManagedHydrationRecoverySummary(
            pending.Length, reconciled, retained, failed);
    }

    public static bool CanCloseReleaseReceipt(
        ArchiveManagedHydrationLeaseState lease,
        OneDriveFilesOnDemandState state) =>
        lease.IsVideo && lease.IsReleaseRequested &&
        state.Availability == AssetAvailability.OnlineOnly &&
        !state.IsPinned;

    /// <summary>
    /// Reject UNC paths, source-root escapes and directory reparse points
    /// before even inspecting the Files On-Demand attributes.
    /// No original is opened or traversed.
    /// </summary>
    public static string? TryResolveMetadataPath(
        string rootLocator, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(rootLocator) ||
            string.IsNullOrWhiteSpace(sourceKey))
            return null;

        try
        {
            if (!Path.IsPathFullyQualified(rootLocator) ||
                rootLocator.StartsWith(@"\\", StringComparison.Ordinal) ||
                Path.IsPathRooted(sourceKey) ||
                sourceKey.Contains(':'))
                return null;

            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(rootLocator));
            string relative = sourceKey.Replace(
                '/', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
                return null;

            VideoDerivativeCacheGuard.AssertNoLinks(
                Path.GetDirectoryName(path)!);
            return path;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
