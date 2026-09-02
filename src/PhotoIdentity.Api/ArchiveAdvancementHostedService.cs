using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed class ArchiveAdvancementHostedService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ActiveDelay = TimeSpan.FromMilliseconds(500);

    private readonly SqliteCatalogueDatabase _database;
    private readonly IArchiveAdvancementControlRepository _control;
    private readonly SqliteArchiveSourceObservationRepository _observations;
    private readonly SqliteArchiveAnalysisRepository _analysis;
    private readonly SqliteArchivePostAnalysisRepository _postAnalysis;
    private readonly SqliteArchiveHydrationRepository _hydrations;
    private readonly SqliteArchiveSourceHydrationRepository _sourceHydrations;
    private readonly ArchiveHydrationCapacityService _capacity;
    private readonly ArchiveBoundedAnalysisService _boundedAnalysis;
    private readonly CollectionOriginalAccessService _originals;
    private readonly FaceReviewDerivativeBackfillService _faceReviewBackfill;
    private readonly ArchiveOperatorConfiguration _operatorConfiguration;
    private readonly ReviewProxyGenerationConfiguration _proxyConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly ArchiveThroughputMetrics _metrics;
    private readonly ILogger<ArchiveAdvancementHostedService> _logger;

    public ArchiveAdvancementHostedService(
        SqliteCatalogueDatabase database,
        IArchiveAdvancementControlRepository control,
        SqliteArchiveSourceObservationRepository observations,
        SqliteArchiveAnalysisRepository analysis,
        SqliteArchivePostAnalysisRepository postAnalysis,
        SqliteArchiveHydrationRepository hydrations,
        SqliteArchiveSourceHydrationRepository sourceHydrations,
        ArchiveHydrationCapacityService capacity,
        ArchiveBoundedAnalysisService boundedAnalysis,
        CollectionOriginalAccessService originals,
        ArchiveOperatorConfiguration operatorConfiguration,
        ReviewProxyGenerationConfiguration proxyConfiguration,
        TimeProvider timeProvider,
        ArchiveThroughputMetrics metrics,
        ILogger<ArchiveAdvancementHostedService> logger)
    {
        _database = database;
        _control = control;
        _observations = observations;
        _analysis = analysis;
        _postAnalysis = postAnalysis;
        _hydrations = hydrations;
        _sourceHydrations = sourceHydrations;
        _capacity = capacity;
        _boundedAnalysis = boundedAnalysis;
        _originals = originals;
        ArgumentNullException.ThrowIfNull(metrics);
        _faceReviewBackfill = new FaceReviewDerivativeBackfillService(
            database,
            new SqliteLocalBatchRepository(database),
            originals,
            proxyConfiguration,
            timeProvider,
            metrics);
        _operatorConfiguration = operatorConfiguration;
        _proxyConfiguration = proxyConfiguration;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ArchiveCoverageConfiguration? coverage = null;
            bool advancementRequested = false;

            try
            {
                coverage = await new SqliteArchiveCoverageRepository(_database)
                    .GetAsync(stoppingToken);
                if (coverage is null)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                ArchiveAdvancementControlState? control = await _control.GetAsync(coverage.Source.Id, stoppingToken);
                if (control?.IsRequested != true)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                advancementRequested = true;
                if (control.SyncRequired)
                {
                    await _control.UpdateRuntimeAsync(
                        coverage.Source.Id,
                        "syncing",
                        syncRequired: null,
                        "Synchronizing included folders before archive processing.",
                        _timeProvider.GetUtcNow(),
                        stoppingToken);
                    await SynchronizeAsync(coverage, stoppingToken);
                    await _control.UpdateRuntimeAsync(
                        coverage.Source.Id,
                        "running",
                        syncRequired: false,
                        "Archive synchronization completed; processing is continuing.",
                        _timeProvider.GetUtcNow(),
                        stoppingToken);
                }

                _ = await _faceReviewBackfill.AdvanceAsync(coverage, stoppingToken);
                _ = await _boundedAnalysis.AdvanceAsync(_operatorConfiguration, stoppingToken);
                ArchiveAdvancementWorkClassification work = await GetWorkStateAsync(coverage, stoppingToken);
                if (!work.HasWork)
                {
                    await _control.CompleteAsync(coverage.Source.Id, _timeProvider.GetUtcNow(), stoppingToken);
                    continue;
                }

                await _control.UpdateRuntimeAsync(
                    coverage.Source.Id,
                    work.WaitingForOneDrive ? "waiting" : "running",
                    syncRequired: null,
                    work.WaitingForOneDrive
                        ? "Waiting for OneDrive to finish a managed download or release."
                        : "Archive processing is continuing.",
                    _timeProvider.GetUtcNow(),
                    stoppingToken);
                if (work.WaitingForOneDrive)
                {
                    using IDisposable waitTiming = _metrics.Measure(ArchiveThroughputMetricNames.OneDriveWait);
                    await Task.Delay(IdleDelay, stoppingToken);
                }
                else
                {
                    using IDisposable activeDelayTiming = _metrics.Measure(
                        ArchiveThroughputMetricNames.ActiveLoopDelay);
                    await Task.Delay(ActiveDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _metrics.RecordCounter(ArchiveThroughputMetricNames.ArchiveErrors);

                if (coverage is null || !advancementRequested)
                {
                    _logger.LogError(
                        exception,
                        "Archive advancement could not read its startup/control state; retrying without stopping Photo Identity.");
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                if (IsRetryableTransition(exception))
                {
                    await TryPersistRecoveryStateAsync(
                        "waiting",
                        cancellationToken => _control.UpdateRuntimeAsync(
                            coverage.Source.Id,
                            "waiting",
                            syncRequired: null,
                            exception.Message,
                            _timeProvider.GetUtcNow(),
                            cancellationToken),
                        stoppingToken);
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                await TryPersistRecoveryStateAsync(
                    "blocked",
                    cancellationToken => _control.BlockAsync(
                        coverage.Source.Id,
                        exception.Message,
                        _timeProvider.GetUtcNow(),
                        cancellationToken),
                    stoppingToken);
            }
        }
    }

    private async Task SynchronizeAsync(
        ArchiveCoverageConfiguration coverage,
        CancellationToken cancellationToken)
    {
        LocalFolderAssetSource source = new(coverage.Source.Id, coverage.Source.RootLocator);
        using IDisposable syncTiming = _metrics.Measure(ArchiveThroughputMetricNames.Synchronization);
        _ = await new LocalArchiveSyncCoordinator(_database, _metrics).SyncAsync(
            source,
            coverage.Source,
            coverage.IncludedFolders,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task<ArchiveAdvancementWorkClassification> GetWorkStateAsync(
        ArchiveCoverageConfiguration coverage,
        CancellationToken cancellationToken)
    {
        if (!_operatorConfiguration.TryResolveAnalysisConfiguration(
                out ArchiveAnalysisConfiguration? analysisConfiguration,
                out string? analysisMessage) ||
            analysisConfiguration is null)
        {
            throw new InvalidOperationException(analysisMessage ?? "Archive analysis is not configured.");
        }

        if (!_proxyConfiguration.TryResolve(
                out _,
                out ReviewProxyProfile? proxyProfile,
                out string? proxyMessage) ||
            proxyProfile is null)
        {
            throw new InvalidOperationException(proxyMessage ?? "Review proxy generation is not configured.");
        }

        LocalBatchConfiguration batchConfiguration = analysisConfiguration.ToBatchConfiguration(coverage.Source.RootLocator);
        AnalysisProfileDefinition analysisProfile = await ArchiveAnalysisProfileFactory.CreateAsync(
            batchConfiguration,
            cancellationToken);
        var profileHash = analysisProfile.ComputeHash();

        bool sourcePending = await _observations.GetNextPendingAsync(coverage.Source.Id, cancellationToken) is not null;
        bool proxyPending = await _postAnalysis.GetNextMissingProxyRevisionAsync(
            coverage.Source.Id,
            profileHash,
            proxyProfile.Id,
            cancellationToken) is not null;
        var faceReviewPendingRevision = await _faceReviewBackfill.GetNextPendingAsync(
            coverage,
            cancellationToken);
        bool faceReviewPending = faceReviewPendingRevision is not null;
        bool faceReviewBlockedOnOneDrive = false;
        if (faceReviewPendingRevision is not null)
        {
            CollectionOriginalAccessSnapshot? faceReviewStatus = await _originals.GetStatusAsync(
                faceReviewPendingRevision.Value,
                cancellationToken);
            faceReviewBlockedOnOneDrive = faceReviewStatus?.State is
                CollectionOriginalAccessService.DownloadingState or
                CollectionOriginalAccessService.ReleasingState;
        }

        bool analysisPending = (await _analysis.GetPendingCurrentRevisionIdsAsync(
            coverage.Source.Id,
            profileHash,
            includeHydratable: true,
            cancellationToken)).Count > 0;

        CatalogueArchiveRunStatus? latest = await new SqliteArchiveStatusRepository(_database)
            .GetLatestRunAsync(profileHash, cancellationToken);
        bool activeRun = latest is not null && (latest.QueuedJobs > 0 || latest.RunningJobs > 0);
        bool hasRunnableWork = sourcePending ||
            proxyPending ||
            (faceReviewPending && !faceReviewBlockedOnOneDrive) ||
            analysisPending ||
            activeRun;

        // Observing the storage snapshot reconciles durable release ownership once OneDrive has
        // actually made a managed file online-only.
        ArchiveStorageSnapshot storage = await _capacity.GetStorageSnapshotAsync(cancellationToken);
        IReadOnlyList<ArchiveManagedHydrationLease> revisionLeases = await _hydrations.GetActiveLeasesAsync(cancellationToken);
        IReadOnlyList<ArchiveManagedSourceHydrationLease> sourceLeases = await _sourceHydrations.GetActiveLeasesAsync(cancellationToken);
        bool releasePending = revisionLeases.Any(value => value.IsReleaseRequested) ||
            sourceLeases.Any(value => value.IsReleaseRequested);
        bool hasOneDriveTransition = storage.HydrationsInProgress > 0 ||
            storage.ManagedReleasingBytes > 0 ||
            releasePending;

        return ArchiveAdvancementWorkClassifier.Classify(
            hasRunnableWork,
            hasOneDriveTransition,
            faceReviewBlockedOnOneDrive);
    }

    private async Task TryPersistRecoveryStateAsync(
        string recoveryState,
        Func<CancellationToken, Task> persistAsync,
        CancellationToken stoppingToken)
    {
        try
        {
            await persistAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown owns cancellation; recovery persistence must never turn it into a fault.
        }
        catch (Exception recoveryException)
        {
            _logger.LogError(
                recoveryException,
                "Archive advancement failed to persist recovery state {RecoveryState}; the worker will continue.",
                recoveryState);
        }
    }

    private static bool IsRetryableTransition(Exception exception)
    {
        if (exception is not InvalidOperationException)
        {
            return false;
        }

        string message = exception.Message;
        return message.Contains("Retry after", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("currently being released", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("concurrency limit", StringComparison.OrdinalIgnoreCase);
    }
}
