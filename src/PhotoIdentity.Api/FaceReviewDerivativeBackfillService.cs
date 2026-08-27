using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

/// <summary>
/// Advances at most one already-analyzed revision toward durable face-review derivative completion.
/// Existing face observations are reused; detector and embedder inference are never rerun.
/// </summary>
public sealed class FaceReviewDerivativeBackfillService
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteLocalBatchRepository _catalogue;
    private readonly CollectionOriginalAccessService _originals;
    private readonly ReviewProxyGenerationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ArchiveThroughputMetrics? _metrics;
    private readonly SqliteFaceReviewDerivativeBackfillRepository _pending;

    public FaceReviewDerivativeBackfillService(
        SqliteCatalogueDatabase database,
        SqliteLocalBatchRepository catalogue,
        CollectionOriginalAccessService originals,
        ReviewProxyGenerationConfiguration configuration,
        TimeProvider timeProvider,
        ArchiveThroughputMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _catalogue = catalogue;
        _originals = originals;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _pending = new SqliteFaceReviewDerivativeBackfillRepository(database);
    }

    public async Task<bool> AdvanceAsync(
        ArchiveCoverageConfiguration coverage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        if (!_configuration.TryResolve(
                out string? derivativeRoot,
                out _,
                out string? message) ||
            derivativeRoot is null)
        {
            throw new InvalidOperationException(
                message ?? "Face review derivative generation is not configured.");
        }

        AssetRevisionId? pendingRevisionId = await _pending.GetNextPendingCurrentRevisionAsync(
            coverage.Source.Id,
            ArchiveFaceReviewDerivativeWriter.ProfileId,
            cancellationToken);
        if (pendingRevisionId is null)
        {
            return false;
        }

        AssetRevisionId revisionId = pendingRevisionId.Value;
        CollectionOriginalAccessSnapshot? status = await _originals.GetStatusAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException(
                "An analyzed archive revision could not be resolved for face review derivative backfill.");

        switch (status.State)
        {
            case CollectionOriginalAccessService.OnlineOnlyState:
                _ = await _originals.RequestHydrationAsync(revisionId, cancellationToken);
                return true;
            case CollectionOriginalAccessService.DownloadingState:
            case CollectionOriginalAccessService.ReleasingState:
                return true;
            case CollectionOriginalAccessService.HashMismatchState:
                // Source verification owns immutable-revision reconciliation. Leave this revision
                // pending so bounded archive advancement can verify/reconcile it before backfill
                // attempts to read source bytes.
                return true;
            case CollectionOriginalAccessService.ReadyState:
                break;
            default:
                throw new InvalidOperationException(
                    "An analyzed archive original is unavailable for face review derivative backfill.");
        }

        CatalogueProcessingAssetRevision revision = await _catalogue.GetAssetRevisionAsync(
            revisionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The analyzed archive revision disappeared before face review derivative generation.");
        string sourcePath = ResolveSourcePath(revision.RootLocator, revision.SourceKey);
        ArchiveFaceReviewDerivativeWriter writer = new(_database);
        using (IDisposable? derivativeTiming = _metrics?.Measure(
                   ArchiveThroughputMetricNames.FaceReviewDerivativeGeneration))
        {
            _ = await writer.GenerateAsync(
                revisionId,
                sourcePath,
                revision.RootLocator,
                derivativeRoot,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }
        _metrics?.RecordCounter(ArchiveThroughputMetricNames.FaceReviewDerivativeRevisions);

        if (status.ManagedHydration)
        {
            _ = await _originals.RequestReleaseAsync(revisionId, cancellationToken);
        }

        return true;
    }

    public Task<AssetRevisionId?> GetNextPendingAsync(
        ArchiveCoverageConfiguration coverage,
        CancellationToken cancellationToken = default) =>
        _pending.GetNextPendingCurrentRevisionAsync(
            coverage.Source.Id,
            ArchiveFaceReviewDerivativeWriter.ProfileId,
            cancellationToken);

    private static string ResolveSourcePath(string rootLocator, string sourceKey)
    {
        string root = Path.GetFullPath(rootLocator);
        string relativePath = sourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidOperationException("The archive source path escaped its configured root.");
        }

        return path;
    }
}
