using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record ArchiveBoundedAnalysisAdvanceResult(bool StartedNewRun);

/// <summary>
/// Advances the permanent archive by at most one governed analysis attempt plus the durable
/// proxy/release work associated with that attempt. Online-only revisions are hydrated through the
/// same bounded storage policy used by explicit original viewing.
/// </summary>
public sealed class ArchiveBoundedAnalysisService
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteLocalBatchRepository _catalogue;
    private readonly SqliteArchiveAnalysisRepository _analysis;
    private readonly SqliteArchivePostAnalysisRepository _postAnalysis;
    private readonly SqliteArchiveReviewProxyRepository _proxies;
    private readonly CollectionOriginalAccessService _originals;
    private readonly ReviewProxyGenerationConfiguration _proxyConfiguration;
    private readonly TimeProvider _timeProvider;

    public ArchiveBoundedAnalysisService(
        SqliteCatalogueDatabase database,
        SqliteLocalBatchRepository catalogue,
        SqliteArchiveAnalysisRepository analysis,
        SqliteArchivePostAnalysisRepository postAnalysis,
        SqliteArchiveReviewProxyRepository proxies,
        CollectionOriginalAccessService originals,
        ReviewProxyGenerationConfiguration proxyConfiguration,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(postAnalysis);
        ArgumentNullException.ThrowIfNull(proxies);
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(proxyConfiguration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _catalogue = catalogue;
        _analysis = analysis;
        _postAnalysis = postAnalysis;
        _proxies = proxies;
        _originals = originals;
        _proxyConfiguration = proxyConfiguration;
        _timeProvider = timeProvider;
    }

    public async Task<ArchiveBoundedAnalysisAdvanceResult> AdvanceAsync(
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorConfiguration);
        ArchiveCoverageConfiguration coverage = await new SqliteArchiveCoverageRepository(_database)
            .GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");

        if (!operatorConfiguration.TryResolveAnalysisConfiguration(
                out ArchiveAnalysisConfiguration? analysisConfiguration,
                out string? analysisMessage) ||
            analysisConfiguration is null)
        {
            throw new InvalidOperationException(analysisMessage ?? "Archive analysis is not configured.");
        }

        if (!_proxyConfiguration.TryResolve(
                out string? derivativeRoot,
                out ReviewProxyProfile? proxyProfile,
                out string? proxyMessage) ||
            derivativeRoot is null ||
            proxyProfile is null)
        {
            throw new InvalidOperationException(proxyMessage ?? "Review proxy generation is not configured.");
        }

        LocalBatchConfiguration batchConfiguration = analysisConfiguration.ToBatchConfiguration(coverage.Source.RootLocator);
        AnalysisProfileDefinition analysisProfile = await ArchiveAnalysisProfileFactory.CreateAsync(
            batchConfiguration,
            cancellationToken);
        Sha256Digest analysisProfileHash = analysisProfile.ComputeHash();

        // Finish durable proxy/release work before starting more inference. If this fails, the
        // analysis completion remains durable and a later call retries only this post-analysis step.
        if (await TryAdvancePostAnalysisAsync(
                coverage,
                analysisProfileHash,
                derivativeRoot,
                proxyProfile,
                cancellationToken))
        {
            return new ArchiveBoundedAnalysisAdvanceResult(false);
        }

        SqliteArchiveStatusRepository statusRepository = new(_database);
        CatalogueArchiveRunStatus? latest = await statusRepository.GetLatestRunAsync(
            analysisProfileHash,
            cancellationToken);
        ArchiveAnalysisCoordinator coordinator = new(_database, _timeProvider);

        if (latest is not null)
        {
            ProcessingRunSummary durable = await new SqliteProcessingRepository(_database)
                .GetRunSummaryAsync(latest.RunId, cancellationToken);
            if (!durable.IsTerminal)
            {
                _ = await coordinator.ResumeAsync(
                    latest.RunId,
                    new ResumableBatchProcessorOptions(maxAttemptsPerInvocation: 1),
                    cancellationToken);
                _ = await TryAdvancePostAnalysisAsync(
                    coverage,
                    analysisProfileHash,
                    derivativeRoot,
                    proxyProfile,
                    cancellationToken);
                return new ArchiveBoundedAnalysisAdvanceResult(false);
            }
        }

        IReadOnlyList<AssetRevisionId> localPending = await _analysis.GetPendingCurrentRevisionIdsAsync(
            coverage.Source.Id,
            analysisProfileHash,
            cancellationToken);
        if (localPending.Count == 0)
        {
            IReadOnlyList<AssetRevisionId> hydratablePending = await _analysis.GetPendingCurrentRevisionIdsAsync(
                coverage.Source.Id,
                analysisProfileHash,
                includeHydratable: true,
                cancellationToken);
            if (hydratablePending.Count > 0)
            {
                await PreparePendingRevisionAsync(hydratablePending[0], cancellationToken);
            }

            return new ArchiveBoundedAnalysisAdvanceResult(false);
        }

        ArchiveAnalysisStartResult started = await coordinator.StartAsync(
            analysisConfiguration,
            new ResumableBatchProcessorOptions(maxAttemptsPerInvocation: 1),
            cancellationToken);
        _ = await TryAdvancePostAnalysisAsync(
            coverage,
            analysisProfileHash,
            derivativeRoot,
            proxyProfile,
            cancellationToken);
        return new ArchiveBoundedAnalysisAdvanceResult(started.ProcessingSummary is not null);
    }

    private async Task PreparePendingRevisionAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        CollectionOriginalAccessSnapshot? status = await _originals.GetStatusAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException("A pending archive revision could not be resolved for bounded hydration.");
        switch (status.State)
        {
            case CollectionOriginalAccessService.ReadyState:
                return;
            case CollectionOriginalAccessService.OnlineOnlyState:
                _ = await _originals.RequestHydrationAsync(revisionId, cancellationToken);
                return;
            case CollectionOriginalAccessService.DownloadingState:
            case CollectionOriginalAccessService.ReleasingState:
                return;
            case CollectionOriginalAccessService.HashMismatchState:
                throw new InvalidOperationException(
                    "A pending archive original no longer matches its immutable revision and requires source re-verification before analysis can continue.");
            default:
                throw new InvalidOperationException(
                    "A pending archive original is unavailable for bounded analysis. Check archive availability and retry.");
        }
    }

    private async Task<bool> TryAdvancePostAnalysisAsync(
        ArchiveCoverageConfiguration coverage,
        Sha256Digest analysisProfileHash,
        string derivativeRoot,
        ReviewProxyProfile proxyProfile,
        CancellationToken cancellationToken)
    {
        AssetRevisionId? pendingRevisionId = await _postAnalysis.GetNextMissingProxyRevisionAsync(
            coverage.Source.Id,
            analysisProfileHash,
            proxyProfile.Id,
            cancellationToken);
        if (pendingRevisionId is null)
        {
            return false;
        }

        AssetRevisionId revisionId = pendingRevisionId.Value;
        CollectionOriginalAccessSnapshot? status = await _originals.GetStatusAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException("An analyzed archive revision could not be resolved for review-proxy generation.");
        if (status.State == CollectionOriginalAccessService.OnlineOnlyState)
        {
            _ = await _originals.RequestHydrationAsync(revisionId, cancellationToken);
            return true;
        }

        if (status.State is CollectionOriginalAccessService.DownloadingState or CollectionOriginalAccessService.ReleasingState)
        {
            return true;
        }

        if (status.State == CollectionOriginalAccessService.HashMismatchState)
        {
            throw new InvalidOperationException(
                "An analyzed archive original no longer matches its immutable revision and requires source re-verification before its proxy can be generated.");
        }

        if (status.State != CollectionOriginalAccessService.ReadyState)
        {
            throw new InvalidOperationException(
                "An analyzed archive original is unavailable for review-proxy generation. Check archive availability and retry.");
        }

        CatalogueProcessingAssetRevision revision = await _catalogue.GetAssetRevisionAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException("The analyzed archive revision disappeared before proxy generation.");
        string sourcePath = ResolveSourcePath(revision.RootLocator, revision.SourceKey);
        ArchiveReviewProxyWriter writer = new(_database);
        _ = await writer.GenerateAsync(
            revisionId,
            sourcePath,
            revision.RootLocator,
            derivativeRoot,
            proxyProfile,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        // Only managed hydration is releasable. Pre-existing local/user-pinned originals remain
        // local because RequestReleaseAsync fails closed for them, so inspect ownership first.
        if (status.ManagedHydration)
        {
            _ = await _originals.RequestReleaseAsync(revisionId, cancellationToken);
        }

        return true;
    }

    private static string ResolveSourcePath(string rootLocator, string sourceKey)
    {
        string root = Path.GetFullPath(rootLocator);
        string relative = sourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("The archive source path escapes the configured source root.");
        }

        return path;
    }
}
