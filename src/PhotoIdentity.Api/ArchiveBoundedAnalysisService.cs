using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record ArchiveBoundedAnalysisAdvanceResult(bool StartedNewRun);

/// <summary>
/// Advances the permanent archive by at most one governed source-verification, metadata,
/// analysis or post-analysis step. Lightweight source divergence is reconciled before inference,
/// online-only content uses the same bounded hydration policy, and successful analysis remains
/// independent from durable review-proxy completion.
/// </summary>
public sealed class ArchiveBoundedAnalysisService : IDisposable
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteLocalBatchRepository _catalogue;
    private readonly SqliteArchiveAnalysisRepository _analysis;
    private readonly IArchivePostAnalysisRepository _postAnalysis;
    private readonly IArchiveReviewProxyRepository _proxies;
    private readonly SqliteArchiveSourceVerificationStateRepository _sourceVerificationState;
    private readonly CollectionOriginalAccessService _originals;
    private readonly ArchiveSourceVerificationService _sourceVerification;
    private readonly PhotoMetadataInspectionService _metadataInspection;
    private readonly FaceReviewDerivativeBackfillService _faceReviewBackfill;
    private readonly ReviewProxyGenerationConfiguration _proxyConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly ArchiveThroughputMetrics? _metrics;
    private readonly ArchiveAnalysisInspectionSession _inspectionSession;
    private readonly ArchiveAnalysisCoordinator _analysisCoordinator;
    private readonly SemaphoreSlim _advanceGate = new(1, 1);
    private bool _disposed;

    public ArchiveBoundedAnalysisService(
        SqliteCatalogueDatabase database,
        SqliteLocalBatchRepository catalogue,
        SqliteArchiveAnalysisRepository analysis,
        IArchivePostAnalysisRepository postAnalysis,
        IArchiveReviewProxyRepository proxies,
        SqliteArchiveSourceVerificationStateRepository sourceVerificationState,
        CollectionOriginalAccessService originals,
        ArchiveSourceVerificationService sourceVerification,
        PhotoMetadataInspectionService metadataInspection,
        ReviewProxyGenerationConfiguration proxyConfiguration,
        TimeProvider timeProvider,
        ArchiveThroughputMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(postAnalysis);
        ArgumentNullException.ThrowIfNull(proxies);
        ArgumentNullException.ThrowIfNull(sourceVerificationState);
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(sourceVerification);
        ArgumentNullException.ThrowIfNull(metadataInspection);
        ArgumentNullException.ThrowIfNull(proxyConfiguration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _catalogue = catalogue;
        _analysis = analysis;
        _postAnalysis = postAnalysis;
        _proxies = proxies;
        _sourceVerificationState = sourceVerificationState;
        _originals = originals;
        _sourceVerification = sourceVerification;
        _metadataInspection = metadataInspection;
        _faceReviewBackfill = new FaceReviewDerivativeBackfillService(
            database,
            catalogue,
            originals,
            proxyConfiguration,
            timeProvider,
            metrics);
        _proxyConfiguration = proxyConfiguration;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _inspectionSession = new ArchiveAnalysisInspectionSession(database, metrics);
        _analysisCoordinator = new ArchiveAnalysisCoordinator(
            database,
            timeProvider,
            metrics,
            _inspectionSession);
    }

    public async Task<ArchiveBoundedAnalysisAdvanceResult> AdvanceAsync(
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorConfiguration);
        _metrics?.RecordCounter(ArchiveThroughputMetricNames.AdvanceInvocations);
        await _advanceGate.WaitAsync(cancellationToken);
        try
        {
            return await AdvanceCoreAsync(operatorConfiguration, cancellationToken);
        }
        finally
        {
            _advanceGate.Release();
        }
    }

    private async Task<ArchiveBoundedAnalysisAdvanceResult> AdvanceCoreAsync(
        ArchiveOperatorConfiguration operatorConfiguration,
        CancellationToken cancellationToken)
    {
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

        ReviewProxyProfile? registeredProfile = await _proxies.GetProfileAsync(proxyProfile.Id, cancellationToken);
        if (registeredProfile is not null &&
            !string.Equals(
                registeredProfile.ToCanonicalText(),
                proxyProfile.ToCanonicalText(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configured review proxy profile '{proxyProfile.Id}' does not match its durable registered settings.");
        }

        LocalBatchConfiguration batchConfiguration = analysisConfiguration.ToBatchConfiguration(coverage.Source.RootLocator);
        AnalysisProfileDefinition analysisProfile = await ArchiveAnalysisProfileFactory.CreateAsync(
            batchConfiguration,
            cancellationToken);
        Sha256Digest analysisProfileHash = analysisProfile.ComputeHash();

        SqliteArchiveStatusRepository statusRepository = new(_database);
        SqliteProcessingRepository processingRepository = new(_database);
        CatalogueArchiveRunStatus? latest = await statusRepository.GetLatestRunAsync(
            analysisProfileHash,
            cancellationToken);

        if (latest is not null)
        {
            ProcessingRunSummary durable = await processingRepository.GetRunSummaryAsync(
                latest.RunId,
                cancellationToken);
            if (!durable.IsTerminal)
            {
                CatalogueProcessingRun savedRun = await processingRepository.GetRunAsync(
                    latest.RunId,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Archive analysis run {latest.RunId} disappeared while checking its runtime configuration.");
                LocalBatchConfiguration savedConfiguration = LocalBatchConfiguration.FromJson(savedRun.ConfigurationJson);
                if (!AnalysisRuntimePathsEqual(savedConfiguration, batchConfiguration))
                {
                    _ = await processingRepository.RequestCancellationAsync(
                        latest.RunId,
                        _timeProvider.GetUtcNow(),
                        cancellationToken);
                    latest = null;
                }
            }
        }

        ArchiveSourceVerificationAdvanceResult verification = await _sourceVerification.AdvanceAsync(
            coverage.Source.Id,
            cancellationToken);
        if (verification.WaitingForLocalContent)
        {
            return new ArchiveBoundedAnalysisAdvanceResult(false);
        }

        if (verification is
            {
                VerificationCompleted: true,
                RevisionChanged: true,
                PreviousRevisionId: AssetRevisionId previousRevisionId,
            } &&
            latest is not null)
        {
            ProcessingRunSummary durable = await processingRepository.GetRunSummaryAsync(
                latest.RunId,
                cancellationToken);
            if (!durable.IsTerminal)
            {
                IReadOnlyList<CatalogueProcessingJob> jobs = await processingRepository.GetJobsAsync(
                    latest.RunId,
                    cancellationToken);
                bool staleRevisionIsStillActive = jobs.Any(job =>
                    job.AssetRevisionId == previousRevisionId &&
                    job.Status is ProcessingJobStatus.Queued or ProcessingJobStatus.Running);
                if (staleRevisionIsStillActive)
                {
                    _ = await processingRepository.RequestCancellationAsync(
                        latest.RunId,
                        _timeProvider.GetUtcNow(),
                        cancellationToken);
                    latest = null;
                }
            }
        }

        if (await TryAdvancePostAnalysisAsync(
                coverage,
                analysisProfileHash,
                derivativeRoot,
                proxyProfile,
                cancellationToken))
        {
            return new ArchiveBoundedAnalysisAdvanceResult(false);
        }

        if (verification is
            {
                VerificationCompleted: true,
                ManagedHydrationTransferred: true,
                RevisionId: AssetRevisionId verifiedRevisionId,
            } &&
            await _analysis.IsCompletedAsync(verifiedRevisionId, analysisProfileHash, cancellationToken) &&
            await _proxies.GetAsync(verifiedRevisionId, proxyProfile.Id, cancellationToken) is not null)
        {
            CollectionOriginalAccessSnapshot? status = await _originals.GetStatusAsync(
                verifiedRevisionId,
                cancellationToken);
            if (status is
                {
                    State: CollectionOriginalAccessService.ReadyState,
                    ManagedHydration: true,
                    CanRelease: true,
                })
            {
                if (!await EnsureMetadataInspectedAsync(verifiedRevisionId, cancellationToken))
                {
                    return new ArchiveBoundedAnalysisAdvanceResult(false);
                }
                await _faceReviewBackfill.GenerateReadyRevisionAsync(
                    verifiedRevisionId,
                    cancellationToken);
                _ = await _originals.RequestReleaseAsync(verifiedRevisionId, cancellationToken);
            }

            return new ArchiveBoundedAnalysisAdvanceResult(false);
        }

        ArchiveAnalysisCoordinator coordinator = _analysisCoordinator;
        if (latest is not null)
        {
            ProcessingRunSummary durable = await processingRepository.GetRunSummaryAsync(
                latest.RunId,
                cancellationToken);
            if (!durable.IsTerminal)
            {
                if (!await EnsureNextDueJobReadyAsync(processingRepository, latest.RunId, cancellationToken))
                {
                    return new ArchiveBoundedAnalysisAdvanceResult(false);
                }

                ArchiveAnalysisResumeResult resumed = await coordinator.ResumeAsync(
                    latest.RunId,
                    new ResumableBatchProcessorOptions(maxAttemptsPerInvocation: 1),
                    cancellationToken);
                if (resumed.ProcessingSummary.FailedJobs > 0)
                {
                    throw new InvalidOperationException(
                        "Archive analysis failed for one or more images. Review the Archive failed filter before retrying.");
                }

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

        CollectionOriginalAccessSnapshot? firstPendingStatus = await _originals.GetStatusAsync(
            localPending[0],
            cancellationToken);
        if (firstPendingStatus?.State != CollectionOriginalAccessService.ReadyState)
        {
            await PreparePendingRevisionAsync(localPending[0], cancellationToken);
            return new ArchiveBoundedAnalysisAdvanceResult(false);
        }

        if (!await EnsureMetadataInspectedAsync(localPending[0], cancellationToken))
        {
            return new ArchiveBoundedAnalysisAdvanceResult(false);
        }
        await RecordAvailabilityAsync(localPending[0], AssetAvailability.Local, cancellationToken);
        ArchiveAnalysisStartResult started = await coordinator.StartAsync(
            analysisConfiguration,
            new ResumableBatchProcessorOptions(maxAttemptsPerInvocation: 1),
            cancellationToken);
        if (started.ProcessingSummary?.FailedJobs > 0)
        {
            throw new InvalidOperationException(
                "Archive analysis failed for one or more images. Review the Archive failed filter before retrying.");
        }

        _ = await TryAdvancePostAnalysisAsync(
            coverage,
            analysisProfileHash,
            derivativeRoot,
            proxyProfile,
            cancellationToken);
        return new ArchiveBoundedAnalysisAdvanceResult(started.ProcessingSummary is not null);
    }

    private async Task<bool> EnsureNextDueJobReadyAsync(
        SqliteProcessingRepository processing,
        ProcessingRunId runId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogueProcessingJob> jobs = await processing.GetJobsAsync(runId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        CatalogueProcessingJob? next = jobs
            .Where(job => job.Status == ProcessingJobStatus.Queued && job.AvailableAtUtc <= now)
            .OrderBy(job => job.AvailableAtUtc)
            .ThenBy(job => job.Id.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();
        if (next is null)
        {
            return true;
        }

        CollectionOriginalAccessSnapshot? status = await _originals.GetStatusAsync(
            next.AssetRevisionId,
            cancellationToken);
        if (status?.State == CollectionOriginalAccessService.ReadyState)
        {
            if (!await EnsureMetadataInspectedAsync(next.AssetRevisionId, cancellationToken))
            {
                return false;
            }
            await RecordAvailabilityAsync(next.AssetRevisionId, AssetAvailability.Local, cancellationToken);
            return true;
        }

        if (status?.State == CollectionOriginalAccessService.HashMismatchState)
        {
            await MarkRevisionNeedsVerificationAsync(next.AssetRevisionId, cancellationToken);
            _ = await processing.RequestCancellationAsync(runId, _timeProvider.GetUtcNow(), cancellationToken);
            return false;
        }

        await PreparePendingRevisionAsync(next.AssetRevisionId, cancellationToken);
        return false;
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
                if (await EnsureMetadataInspectedAsync(revisionId, cancellationToken))
                {
                    await RecordAvailabilityAsync(revisionId, AssetAvailability.Local, cancellationToken);
                }
                return;
            case CollectionOriginalAccessService.OnlineOnlyState:
                await RecordAvailabilityAsync(revisionId, AssetAvailability.OnlineOnly, cancellationToken);
                CollectionOriginalAccessSnapshot? requested = await _originals.RequestHydrationAsync(
                    revisionId,
                    cancellationToken);
                if (requested?.State == CollectionOriginalAccessService.DownloadingState)
                {
                    await RecordAvailabilityAsync(revisionId, AssetAvailability.Downloading, cancellationToken);
                }
                return;
            case CollectionOriginalAccessService.DownloadingState:
                await RecordAvailabilityAsync(revisionId, AssetAvailability.Downloading, cancellationToken);
                return;
            case CollectionOriginalAccessService.ReleasingState:
                return;
            case CollectionOriginalAccessService.HashMismatchState:
                await RecordAvailabilityAsync(revisionId, AssetAvailability.Local, cancellationToken);
                await MarkRevisionNeedsVerificationAsync(revisionId, cancellationToken);
                return;
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
            await RecordAvailabilityAsync(revisionId, AssetAvailability.OnlineOnly, cancellationToken);
            CollectionOriginalAccessSnapshot? requested = await _originals.RequestHydrationAsync(
                revisionId,
                cancellationToken);
            if (requested?.State == CollectionOriginalAccessService.DownloadingState)
            {
                await RecordAvailabilityAsync(revisionId, AssetAvailability.Downloading, cancellationToken);
            }
            return true;
        }

        if (status.State == CollectionOriginalAccessService.DownloadingState)
        {
            await RecordAvailabilityAsync(revisionId, AssetAvailability.Downloading, cancellationToken);
            return true;
        }

        if (status.State == CollectionOriginalAccessService.ReleasingState)
        {
            return true;
        }

        if (status.State == CollectionOriginalAccessService.HashMismatchState)
        {
            await RecordAvailabilityAsync(revisionId, AssetAvailability.Local, cancellationToken);
            await MarkRevisionNeedsVerificationAsync(revisionId, cancellationToken);
            return true;
        }

        if (status.State != CollectionOriginalAccessService.ReadyState)
        {
            throw new InvalidOperationException(
                "An analyzed archive original is unavailable for review-proxy generation. Check archive availability and retry.");
        }

        if (!await EnsureMetadataInspectedAsync(revisionId, cancellationToken))
        {
            return true;
        }
        await RecordAvailabilityAsync(revisionId, AssetAvailability.Local, cancellationToken);
        CatalogueProcessingAssetRevision revision = await _catalogue.GetAssetRevisionAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException("The analyzed archive revision disappeared before proxy generation.");
        string sourcePath = ResolveSourcePath(revision.RootLocator, revision.SourceKey);
        ArchiveReviewProxyWriter writer = new(_database);
        using (IDisposable? proxyTiming = _metrics?.Measure(ArchiveThroughputMetricNames.ReviewProxyGeneration))
        {
            _ = await writer.GenerateAsync(
                revisionId,
                sourcePath,
                revision.RootLocator,
                derivativeRoot,
                proxyProfile,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }
        _metrics?.RecordCounter(ArchiveThroughputMetricNames.ReviewProxiesGenerated);

        await _faceReviewBackfill.GenerateReadyRevisionAsync(revisionId, cancellationToken);

        if (status.ManagedHydration)
        {
            CollectionOriginalAccessSnapshot? released = await _originals.RequestReleaseAsync(
                revisionId,
                cancellationToken);
            if (released?.State == CollectionOriginalAccessService.OnlineOnlyState)
            {
                await RecordAvailabilityAsync(revisionId, AssetAvailability.OnlineOnly, cancellationToken);
            }
        }

        return true;
    }

    private async Task<bool> EnsureMetadataInspectedAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        if (await _metadataInspection.IsInspectedAsync(revisionId, cancellationToken))
        {
            return true;
        }

        VerifiedCollectionOriginal? original = await _originals.OpenVerifiedAsync(revisionId, cancellationToken);
        if (original is null)
        {
            await MarkRevisionNeedsVerificationAsync(revisionId, cancellationToken);
            return false;
        }

        await using FileStream stream = original.Stream;
        using (IDisposable? metadataTiming = _metrics?.Measure(ArchiveThroughputMetricNames.MetadataInspection))
        {
            _ = await _metadataInspection.InspectVerifiedAsync(
                revisionId,
                stream,
                original.ContentType,
                cancellationToken);
        }
        _metrics?.RecordCounter(ArchiveThroughputMetricNames.MetadataInspections);
        return true;
    }

    private async Task MarkRevisionNeedsVerificationAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        CatalogueProcessingAssetRevision? revision = await _catalogue.GetAssetRevisionAsync(
            revisionId,
            cancellationToken);
        if (revision is null)
        {
            return;
        }

        await _sourceVerificationState.MarkNeedsVerificationAsync(
            revision.AssetId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task RecordAvailabilityAsync(
        AssetRevisionId revisionId,
        AssetAvailability availability,
        CancellationToken cancellationToken)
    {
        CatalogueProcessingAssetRevision? revision = await _catalogue.GetAssetRevisionAsync(revisionId, cancellationToken);
        if (revision is null)
        {
            return;
        }

        await new SqliteArchiveAvailabilityRepository(_database).RecordAsync(
            revision.AssetId,
            availability,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static bool AnalysisRuntimePathsEqual(
        LocalBatchConfiguration savedConfiguration,
        LocalBatchConfiguration currentConfiguration) =>
        PathsEqual(savedConfiguration.RepositoryRoot, currentConfiguration.RepositoryRoot) &&
        PathsEqual(savedConfiguration.ModelDirectory, currentConfiguration.ModelDirectory);

    private static bool PathsEqual(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inspectionSession.Dispose();
        _advanceGate.Dispose();
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
