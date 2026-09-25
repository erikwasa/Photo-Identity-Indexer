using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

/// <summary>
/// Advances durable identity regeneration work in bounded batches so browser requests only
/// enqueue or inspect work. Excluded faces are completed without scoring, preventing new identity
/// evidence from being generated for a source copy after its privacy tombstone is durable.
/// </summary>
public sealed class IdentityMatchRegenerationHostedService : BackgroundService
{
    private const int TargetBatchSize = 8;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ActiveDelay = TimeSpan.FromMilliseconds(25);

    private readonly IIdentityMatchRegenerationRepository _runs;
    private readonly IIdentityMatchRegenerationScorer _scorer;
    private readonly IIdentitySuggestionPolicyRepository _policies;
    private readonly IIdentityAutoAssignmentService _autoAssignment;
    private readonly IIdentityMatchEvidenceVersionReader _evidence;
    private readonly TimeProvider _timeProvider;
    private readonly ArchiveThroughputMetrics _metrics;
    private readonly ILogger<IdentityMatchRegenerationHostedService> _logger;
    private readonly IIdentityMatchFollowUpPlanner _followUpPlanner;
    private readonly ProvisionalFaceClusteringWorker? _provisionalClustering;
    private readonly ISourceCopyExclusionRepository? _exclusions;

    public IdentityMatchRegenerationHostedService(
        IIdentityMatchRegenerationRepository runs,
        IIdentityMatchRegenerationScorer scorer,
        IIdentitySuggestionPolicyRepository policies,
        IIdentityAutoAssignmentService autoAssignment,
        IIdentityMatchEvidenceVersionReader evidence,
        TimeProvider timeProvider,
        ArchiveThroughputMetrics metrics,
        ILogger<IdentityMatchRegenerationHostedService>? logger = null,
        IIdentityMatchModelRepository? models = null,
        IConfiguration? configuration = null,
        PostgresCatalogueDatabase? postgresCatalogueDatabase = null,
        ISourceCopyExclusionRepository? exclusions = null)
    {
        _runs = runs;
        _scorer = scorer;
        _policies = policies;
        _autoAssignment = autoAssignment;
        _evidence = evidence;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger ?? NullLogger<IdentityMatchRegenerationHostedService>.Instance;
        _exclusions = exclusions;
        _followUpPlanner = models is null
            ? DisabledIdentityMatchFollowUpPlanner.Instance
            : new IdentityMatchFollowUpPlanner(
                models,
                runs,
                evidence,
                policies,
                timeProvider,
                IdentityMatchFollowUpConfiguration.FromConfiguration(configuration));

        bool postgresSelected = configuration is not null &&
            CataloguePersistenceComposition.ResolveProvider(configuration) == CatalogueProviderKind.Postgres;
        _provisionalClustering = postgresSelected && postgresCatalogueDatabase is not null
            ? new ProvisionalFaceClusteringWorker(
                new PostgresProvisionalFaceClusterRepository(postgresCatalogueDatabase),
                new ProvisionalFaceDbscanClusterer(),
                timeProvider,
                _logger,
                new PostgresProvisionalFaceClusterReviewRepository(postgresCatalogueDatabase))
            : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked;
            try
            {
                worked = await AdvanceOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Identity match regeneration failed unexpectedly; retrying without stopping Photo Identity.");
                worked = false;
            }

            await Task.Delay(worked ? ActiveDelay : IdleDelay, stoppingToken);
        }
    }

    public async Task<bool> AdvanceOnceAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable timing = _metrics.Measure(
            ArchiveThroughputMetricNames.IdentityRegenerationCycle);

        bool followUpStarted = await _followUpPlanner.TryStartDueAsync(cancellationToken);
        ReviewIdentityMatchRegenerationRun? run = await _runs.GetNextActiveAsync(cancellationToken);
        if (run is null)
        {
            bool clusteringWorked = _provisionalClustering is not null &&
                await _provisionalClustering.AdvanceOnceAsync(cancellationToken);
            return followUpStarted || clusteringWorked;
        }

        int processedInBatch = 0;
        bool stoppedBecauseNoTarget = false;
        bool prepared = false;
        while (processedInBatch < TargetBatchSize)
        {
            ReviewIdentityMatchRegenerationTarget? target = await _runs.ClaimNextTargetAsync(
                run.Id,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            if (target is null)
            {
                stoppedBecauseNoTarget = true;
                break;
            }

            _metrics.RecordCounter(
                ArchiveThroughputMetricNames.IdentityRegenerationTargetsClaimed);
            try
            {
                if (_exclusions is not null &&
                    await _exclusions.IsFaceOccurrenceExcludedAsync(target.FaceOccurrenceId, cancellationToken))
                {
                    await _runs.CompleteTargetAsync(
                        run.Id,
                        target.FaceOccurrenceId,
                        suggestionCount: 0,
                        _timeProvider.GetUtcNow(),
                        cancellationToken);
                    _metrics.RecordCounter(
                        ArchiveThroughputMetricNames.IdentityRegenerationTargetsCompleted);
                    processedInBatch++;
                    continue;
                }

                if (!prepared)
                {
                    await _scorer.PrepareRunAsync(run, cancellationToken);
                    prepared = true;
                }

                int suggestionCount = await _scorer.ScoreTargetAsync(
                    run.ModelId,
                    run.ModelHash,
                    target.FaceOccurrenceId,
                    cancellationToken);
                await _runs.CompleteTargetAsync(
                    run.Id,
                    target.FaceOccurrenceId,
                    suggestionCount,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                _metrics.RecordCounter(
                    ArchiveThroughputMetricNames.IdentityRegenerationTargetsCompleted);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                await _runs.FailTargetAsync(
                    run.Id,
                    target.FaceOccurrenceId,
                    exception.Message,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                _metrics.RecordCounter(
                    ArchiveThroughputMetricNames.IdentityRegenerationTargetsFailed);
            }

            processedInBatch++;
        }

        if (processedInBatch > 0)
        {
            if (stoppedBecauseNoTarget)
            {
                ReviewIdentityMatchRegenerationRun? latestAfterBatch = await _runs.GetLatestAsync(
                    run.ModelId,
                    run.ModelHash,
                    cancellationToken);
                if (latestAfterBatch is null || !latestAfterBatch.IsActive || latestAfterBatch.Id != run.Id)
                {
                    await _scorer.ReleaseRunAsync(run.Id, cancellationToken);
                }
            }

            return true;
        }

        ReviewIdentityMatchRegenerationRun? latest = await _runs.GetLatestAsync(
            run.ModelId,
            run.ModelHash,
            cancellationToken);
        if (latest is null || !latest.IsActive || latest.Id != run.Id)
        {
            await _scorer.ReleaseRunAsync(run.Id, cancellationToken);
            return true;
        }

        if (!await _runs.EvidenceStillMatchesAsync(latest, cancellationToken))
        {
            await _runs.MarkFailedAsync(
                run.Id,
                "Identity evidence changed after the final target was scored. Start a new regeneration from the current catalogue state.",
                _timeProvider.GetUtcNow(),
                cancellationToken);
            await _scorer.ReleaseRunAsync(run.Id, cancellationToken);
            return true;
        }

        ReviewIdentitySuggestionPolicy policy = await _policies.GetAsync(
            run.ModelId,
            run.ModelHash,
            cancellationToken);
        if (policy.Version != run.PolicyVersion)
        {
            await _runs.MarkFailedAsync(
                run.Id,
                $"Suggestion policy changed from version {run.PolicyVersion} to {policy.Version} while regeneration was running. Start a new regeneration.",
                _timeProvider.GetUtcNow(),
                cancellationToken);
            await _scorer.ReleaseRunAsync(run.Id, cancellationToken);
            return true;
        }

        try
        {
            await _scorer.RemoveObsoleteRankingsAsync(
                run.ModelId,
                run.ModelHash,
                run.Id,
                cancellationToken);
            ReviewIdentityAutoAssignmentSummary auto = await _autoAssignment.ApplyAsync(
                run.ModelId,
                run.ModelHash,
                policy,
                cancellationToken);

            ReviewIdentityMatchEvidenceVersion currentEvidence = await _evidence.ReadAsync(
                run.ModelId,
                run.ModelHash,
                cancellationToken);
            ReviewIdentityMatchEvidenceVersion expectedEvidence =
                ReviewIdentityMatchEvidenceVersions.ExpectedAfterAutomaticAssignments(
                    run.EvidenceVersion,
                    auto.AssignedCount);
            if (currentEvidence != expectedEvidence)
            {
                await _runs.MarkFailedAsync(
                    run.Id,
                    "Identity evidence changed while automatic assignments were being finalized. The generated suggestions are stale; start a new regeneration from the current catalogue state.",
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                await _scorer.ReleaseRunAsync(run.Id, cancellationToken);
                return true;
            }

            await _runs.CompleteRunAsync(
                run.Id,
                auto.AssignedCount,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            await _scorer.ReleaseRunAsync(run.Id, cancellationToken);
            _metrics.RecordCounter(
                ArchiveThroughputMetricNames.IdentityRegenerationRunsCompleted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await _runs.MarkFailedAsync(
                run.Id,
                exception.Message,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            await _scorer.ReleaseRunAsync(run.Id, cancellationToken);
            _metrics.RecordCounter(
                ArchiveThroughputMetricNames.IdentityRegenerationRunsFailed);
        }

        return true;
    }
}
