using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

/// <summary>
/// Advances durable identity regeneration work in small units so browser requests only enqueue
/// or inspect work. The run repository makes an interrupted running target reclaimable after an
/// application restart.
/// </summary>
public sealed class IdentityMatchRegenerationHostedService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ActiveDelay = TimeSpan.FromMilliseconds(25);

    private readonly SqliteIdentityMatchRegenerationRepository _runs;
    private readonly SqliteIdentityMatchRegenerationScorer _scorer;
    private readonly SqliteIdentitySuggestionPolicyRepository _policies;
    private readonly SqliteIdentityAutoAssignmentService _autoAssignment;
    private readonly SqliteIdentityMatchEvidenceVersionReader _evidence;
    private readonly TimeProvider _timeProvider;

    public IdentityMatchRegenerationHostedService(
        SqliteIdentityMatchRegenerationRepository runs,
        SqliteIdentityMatchRegenerationScorer scorer,
        SqliteIdentitySuggestionPolicyRepository policies,
        SqliteIdentityAutoAssignmentService autoAssignment,
        SqliteIdentityMatchEvidenceVersionReader evidence,
        TimeProvider timeProvider)
    {
        _runs = runs;
        _scorer = scorer;
        _policies = policies;
        _autoAssignment = autoAssignment;
        _evidence = evidence;
        _timeProvider = timeProvider;
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

            await Task.Delay(worked ? ActiveDelay : IdleDelay, stoppingToken);
        }
    }

    public async Task<bool> AdvanceOnceAsync(CancellationToken cancellationToken = default)
    {
        CatalogueIdentityMatchRegenerationRun? run = await _runs.GetNextActiveAsync(cancellationToken);
        if (run is null)
        {
            return false;
        }

        CatalogueIdentityMatchRegenerationTarget? target = await _runs.ClaimNextTargetAsync(
            run.Id,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        if (target is not null)
        {
            try
            {
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
            }

            return true;
        }

        CatalogueIdentityMatchRegenerationRun? latest = await _runs.GetLatestAsync(
            run.ModelId,
            run.ModelHash,
            cancellationToken);
        if (latest is null || !latest.IsActive || latest.Id != run.Id)
        {
            return true;
        }

        if (!await _runs.EvidenceStillMatchesAsync(latest, cancellationToken))
        {
            // ClaimNextTargetAsync normally detects this. This check closes the window after the
            // final target and before automatic assignment/finalization.
            await _runs.MarkFailedAsync(
                run.Id,
                "Identity evidence changed after the final target was scored. Start a new regeneration from the current catalogue state.",
                _timeProvider.GetUtcNow(),
                cancellationToken);
            return true;
        }

        IdentitySuggestionPolicy policy = await _policies.GetAsync(
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
            return true;
        }

        try
        {
            await _scorer.RemoveObsoleteRankingsAsync(
                run.ModelId,
                run.ModelHash,
                run.Id,
                cancellationToken);
            IdentityAutoAssignmentSummary auto = await _autoAssignment.ApplyAsync(
                run.ModelId,
                run.ModelHash,
                policy,
                cancellationToken);

            IdentityMatchEvidenceVersion currentEvidence = await _evidence.ReadAsync(
                run.ModelId,
                run.ModelHash,
                cancellationToken);
            IdentityMatchEvidenceVersion expectedEvidence =
                SqliteIdentityMatchEvidenceVersionReader.ExpectedAfterAutomaticAssignments(
                    run.EvidenceVersion,
                    auto.AssignedCount);
            if (currentEvidence != expectedEvidence)
            {
                await _runs.MarkFailedAsync(
                    run.Id,
                    "Identity evidence changed while automatic assignments were being finalized. The generated suggestions are stale; start a new regeneration.",
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                return true;
            }

            await _runs.CompleteRunAsync(
                run.Id,
                auto.AssignedCount,
                _timeProvider.GetUtcNow(),
                cancellationToken);
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
        }

        return true;
    }
}
