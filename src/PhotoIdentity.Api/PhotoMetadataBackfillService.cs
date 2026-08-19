using System.Security.Cryptography;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

public sealed record PhotoMetadataBackfillReport(
    int Candidates,
    int Persisted,
    int NewlyInspected,
    int RefreshedStale,
    int ForcedCurrentRefresh,
    int DeferredNonLocal,
    int DeferredChanged,
    int DeferredUnavailable,
    int CurrentContractVersion,
    bool Force);

/// <summary>
/// Reads photo metadata only from source files that are already local and still match the immutable
/// revision fingerprint. Normal execution processes missing and stale extraction-contract rows;
/// force mode can intentionally re-read current rows for repair. The service never requests Files
/// On-Demand hydration.
/// </summary>
public sealed class PhotoMetadataBackfillService
{
    private readonly SqlitePhotoMetadataBackfillRepository _backfill;
    private readonly IOneDriveFilesOnDemandPlatform _filesOnDemand;
    private readonly PhotoMetadataInspectionService _inspection;

    public PhotoMetadataBackfillService(
        SqlitePhotoMetadataBackfillRepository backfill,
        IOneDriveFilesOnDemandPlatform filesOnDemand,
        PhotoMetadataInspectionService inspection)
    {
        ArgumentNullException.ThrowIfNull(backfill);
        ArgumentNullException.ThrowIfNull(filesOnDemand);
        ArgumentNullException.ThrowIfNull(inspection);
        _backfill = backfill;
        _filesOnDemand = filesOnDemand;
        _inspection = inspection;
    }

    public async Task<PhotoMetadataBackfillReport> ExecuteBatchAsync(
        int limit = 250,
        int offset = 0,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        int currentVersion = PhotoMetadataExtractionContract.CurrentVersion;
        IReadOnlyList<PhotoMetadataRefreshCandidate> candidates =
            await _backfill.GetRefreshCandidatesAsync(
                limit,
                offset,
                currentVersion,
                force,
                cancellationToken);

        int persisted = 0;
        int newlyInspected = 0;
        int refreshedStale = 0;
        int forcedCurrentRefresh = 0;
        int deferredNonLocal = 0;
        int deferredChanged = 0;
        int deferredUnavailable = 0;

        foreach (PhotoMetadataRefreshCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path;
            try
            {
                path = ResolveSourcePath(candidate.RootLocator, candidate.SourceKey);
            }
            catch (ArgumentException)
            {
                deferredUnavailable++;
                continue;
            }

            OneDriveFilesOnDemandState state = _filesOnDemand.GetState(path);
            if (state.Availability != AssetAvailability.Local)
            {
                if (state.Availability is AssetAvailability.OnlineOnly or AssetAvailability.Downloading)
                {
                    deferredNonLocal++;
                }
                else
                {
                    deferredUnavailable++;
                }

                continue;
            }

            try
            {
                FileInfo file = new(path);
                if (!file.Exists || file.Length != candidate.SizeBytes)
                {
                    deferredChanged++;
                    continue;
                }

                await using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                using SHA256 sha256 = SHA256.Create();
                byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
                string actualHash = Convert.ToHexString(hash).ToLowerInvariant();
                if (!string.Equals(actualHash, candidate.ContentHash.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    deferredChanged++;
                    continue;
                }

                stream.Position = 0;
                _ = await _inspection.InspectVerifiedAsync(
                    candidate.RevisionId,
                    stream,
                    candidate.MediaType,
                    cancellationToken);
                persisted++;

                if (candidate.IsNew)
                {
                    newlyInspected++;
                }
                else if (candidate.IsStale(currentVersion))
                {
                    refreshedStale++;
                }
                else
                {
                    forcedCurrentRefresh++;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
            {
                deferredUnavailable++;
            }
        }

        return new PhotoMetadataBackfillReport(
            candidates.Count,
            persisted,
            newlyInspected,
            refreshedStale,
            forcedCurrentRefresh,
            deferredNonLocal,
            deferredChanged,
            deferredUnavailable,
            currentVersion,
            force);
    }

    private static string ResolveSourcePath(string rootLocator, string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);

        string root = Path.GetFullPath(rootLocator);
        string platformKey = sourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(root, platformKey));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.Equals(root, comparison) && !resolved.StartsWith(rootPrefix, comparison))
        {
            throw new ArgumentException("The source item must remain inside the configured root.", nameof(sourceKey));
        }

        return resolved;
    }
}
