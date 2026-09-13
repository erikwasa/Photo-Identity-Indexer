using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Api;

public sealed class IdentityMatchFollowUpConfiguration
{
    public const int DefaultCoalesceDelayMilliseconds = 30_000;
    public const int MaximumCoalesceDelayMilliseconds = 600_000;

    public IdentityMatchFollowUpConfiguration(
        bool? enabled = null,
        int? coalesceDelayMilliseconds = null)
    {
        int delay = coalesceDelayMilliseconds ?? DefaultCoalesceDelayMilliseconds;
        if (delay is < 0 or > MaximumCoalesceDelayMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coalesceDelayMilliseconds),
                $"Automatic identity follow-up delay must be between 0 and {MaximumCoalesceDelayMilliseconds} milliseconds.");
        }

        Enabled = enabled ?? true;
        CoalesceDelay = TimeSpan.FromMilliseconds(delay);
    }

    public bool Enabled { get; }
    public TimeSpan CoalesceDelay { get; }
}

public sealed record IdentityMatchFollowUpState(
    bool Enabled,
    string Status,
    DateTimeOffset? LatestQualifyingChangeAtUtc,
    DateTimeOffset? DueAtUtc,
    bool QueuedAfterActiveRun);

public interface IIdentityMatchFollowUpPlanner
{
    Task<bool> TryStartDueAsync(CancellationToken cancellationToken = default);

    Task<IdentityMatchFollowUpState> GetStateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledIdentityMatchFollowUpPlanner : IIdentityMatchFollowUpPlanner
{
    public static DisabledIdentityMatchFollowUpPlanner Instance { get; } = new();

    private DisabledIdentityMatchFollowUpPlanner()
    {
    }

    public Task<bool> TryStartDueAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<IdentityMatchFollowUpState> GetStateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new IdentityMatchFollowUpState(
            Enabled: false,
            Status: "disabled",
            LatestQualifyingChangeAtUtc: null,
            DueAtUtc: null,
            QueuedAfterActiveRun: false));
    }
}

public sealed class IdentityMatchFollowUpPlanner : IIdentityMatchFollowUpPlanner
{
    public const string RequestedBy = "identity-matcher:follow-up";
    private static readonly ReviewIdentityMatchEvidenceVersion ZeroEvidence = new(0, 0, 0, 0);

    private readonly IIdentityMatchModelRepository _models;
    private readonly IIdentityMatchRegenerationRepository _runs;
    private readonly IIdentityMatchFollowUpEvidenceRepository _followUpEvidence;
    private readonly IIdentitySuggestionPolicyRepository _policies;
    private readonly TimeProvider _timeProvider;
    private readonly IdentityMatchFollowUpConfiguration _configuration;

    public IdentityMatchFollowUpPlanner(
        IIdentityMatchModelRepository models,
        IIdentityMatchRegenerationRepository runs,
        IIdentityMatchFollowUpEvidenceRepository followUpEvidence,
        IIdentitySuggestionPolicyRepository policies,
        TimeProvider timeProvider,
        IdentityMatchFollowUpConfiguration configuration)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _followUpEvidence = followUpEvidence ?? throw new ArgumentNullException(nameof(followUpEvidence));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<bool> TryStartDueAsync(CancellationToken cancellationToken = default)
    {
        if (!_configuration.Enabled)
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<ReviewIdentityMatchModelRevision> models = await _models.ListAsync(cancellationToken);
        foreach (ReviewIdentityMatchModelRevision model in models)
        {
            ReviewIdentityMatchRegenerationRun? latest = await _runs.GetLatestAsync(
                model.ModelId,
                model.ModelHash,
                cancellationToken);
            if (latest?.IsActive == true)
            {
                continue;
            }

            ReviewIdentityMatchEvidenceVersion baseline = latest?.EvidenceVersion ?? ZeroEvidence;
            DateTimeOffset? latestChange = await _followUpEvidence.GetLatestQualifyingChangeAsync(
                model.ModelId,
                model.ModelHash,
                baseline,
                cancellationToken);
            if (latestChange is null || latestChange.Value + _configuration.CoalesceDelay > now)
            {
                continue;
            }

            ReviewIdentitySuggestionPolicy policy = await _policies.GetAsync(
                model.ModelId,
                model.ModelHash,
                cancellationToken);
            try
            {
                await _runs.StartAsync(
                    model.ModelId,
                    model.ModelHash,
                    policy.Version,
                    RequestedBy,
                    now,
                    cancellationToken);
                return true;
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
            {
                // An explicit request won the race. The durable active run is authoritative.
            }
        }

        return false;
    }

    public async Task<IdentityMatchFollowUpState> GetStateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        ReviewIdentityMatchRegenerationRun? latest = await _runs.GetLatestAsync(
            modelId,
            modelHash,
            cancellationToken);
        ReviewIdentityMatchEvidenceVersion baseline = latest?.EvidenceVersion ?? ZeroEvidence;
        DateTimeOffset? latestChange = await _followUpEvidence.GetLatestQualifyingChangeAsync(
            modelId,
            modelHash,
            baseline,
            cancellationToken);
        DateTimeOffset? dueAt = latestChange is DateTimeOffset changed
            ? changed + _configuration.CoalesceDelay
            : null;

        if (!_configuration.Enabled)
        {
            return new(false, "disabled", latestChange, dueAt, QueuedAfterActiveRun: false);
        }

        if (latest?.IsActive == true)
        {
            return new(
                true,
                "running",
                latestChange,
                dueAt,
                QueuedAfterActiveRun: latestChange is not null);
        }

        if (latestChange is not null)
        {
            return new(true, "queued", latestChange, dueAt, QueuedAfterActiveRun: false);
        }

        return new(true, "current", null, null, QueuedAfterActiveRun: false);
    }
}
