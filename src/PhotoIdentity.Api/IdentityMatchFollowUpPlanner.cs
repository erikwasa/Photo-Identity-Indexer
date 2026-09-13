using Microsoft.Extensions.Configuration;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Api;

public sealed class IdentityMatchFollowUpConfiguration
{
    public const int DefaultCoalesceDelayMilliseconds = 30_000;
    public const int MaximumCoalesceDelayMilliseconds = 600_000;
    public const string EnabledConfigurationKey =
        "PhotoIdentity:IdentityMatchRegeneration:AutomaticFollowUpEnabled";
    public const string DelayConfigurationKey =
        "PhotoIdentity:IdentityMatchRegeneration:AutomaticFollowUpDelayMilliseconds";

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

    public static IdentityMatchFollowUpConfiguration FromConfiguration(
        IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return new IdentityMatchFollowUpConfiguration();
        }

        bool? enabled = ParseOptionalBool(configuration[EnabledConfigurationKey], EnabledConfigurationKey);
        int? delay = ParseOptionalInt(configuration[DelayConfigurationKey], DelayConfigurationKey);
        return new IdentityMatchFollowUpConfiguration(enabled, delay);
    }

    private static bool? ParseOptionalBool(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException($"Configuration '{key}' must be true or false.");
    }

    private static int? ParseOptionalInt(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out int parsed)
            ? parsed
            : throw new InvalidOperationException($"Configuration '{key}' must be an integer number of milliseconds.");
    }
}

public sealed record IdentityMatchFollowUpState(
    bool Enabled,
    string Status,
    DateTimeOffset? ChangeDetectedAtUtc,
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
            ChangeDetectedAtUtc: null,
            DueAtUtc: null,
            QueuedAfterActiveRun: false));
    }
}

/// <summary>
/// Converts durable evidence-version drift into a debounced later regeneration. The durable
/// mismatch, rather than the in-memory debounce timestamp, is the queue authority: after restart
/// the mismatch is rediscovered and cannot be lost. Completed automatic assignments are included
/// in the expected post-run evidence version so they do not recursively trigger another run.
/// </summary>
public sealed class IdentityMatchFollowUpPlanner : IIdentityMatchFollowUpPlanner
{
    public const string RequestedBy = "identity-matcher:follow-up";
    private static readonly ReviewIdentityMatchEvidenceVersion ZeroEvidence = new(0, 0, 0, 0);

    private readonly IIdentityMatchModelRepository _models;
    private readonly IIdentityMatchRegenerationRepository _runs;
    private readonly IIdentityMatchEvidenceVersionReader _evidence;
    private readonly IIdentitySuggestionPolicyRepository _policies;
    private readonly TimeProvider _timeProvider;
    private readonly IdentityMatchFollowUpConfiguration _configuration;
    private readonly object _gate = new();
    private readonly Dictionary<ModelKey, PendingObservation> _pending = [];

    public IdentityMatchFollowUpPlanner(
        IIdentityMatchModelRepository models,
        IIdentityMatchRegenerationRepository runs,
        IIdentityMatchEvidenceVersionReader evidence,
        IIdentitySuggestionPolicyRepository policies,
        TimeProvider timeProvider,
        IdentityMatchFollowUpConfiguration configuration)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
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
            ReviewIdentityMatchEvidenceVersion current = await _evidence.ReadAsync(
                model.ModelId,
                model.ModelHash,
                cancellationToken);
            ReviewIdentityMatchEvidenceVersion expected = ExpectedEvidence(latest);
            ModelKey key = new(model.ModelId.ToString(), model.ModelHash.ToString());

            if (current == expected)
            {
                ClearObservation(key);
                continue;
            }

            PendingObservation observation = Observe(key, current, now);
            if (latest?.IsActive == true || observation.DueAtUtc > now)
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
                ClearObservation(key);
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
        if (!_configuration.Enabled)
        {
            return new(false, "disabled", null, null, QueuedAfterActiveRun: false);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        ReviewIdentityMatchRegenerationRun? latest = await _runs.GetLatestAsync(
            modelId,
            modelHash,
            cancellationToken);
        ReviewIdentityMatchEvidenceVersion current = await _evidence.ReadAsync(
            modelId,
            modelHash,
            cancellationToken);
        ReviewIdentityMatchEvidenceVersion expected = ExpectedEvidence(latest);
        ModelKey key = new(modelId.ToString(), modelHash.ToString());

        if (current == expected)
        {
            ClearObservation(key);
            return new(
                true,
                latest?.IsActive == true ? "running" : "current",
                null,
                null,
                QueuedAfterActiveRun: false);
        }

        PendingObservation observation = Observe(key, current, now);
        return new(
            true,
            latest?.IsActive == true ? "running" : "queued",
            observation.DetectedAtUtc,
            observation.DueAtUtc,
            QueuedAfterActiveRun: latest?.IsActive == true);
    }

    private ReviewIdentityMatchEvidenceVersion ExpectedEvidence(
        ReviewIdentityMatchRegenerationRun? latest)
    {
        if (latest is null)
        {
            return ZeroEvidence;
        }

        return string.Equals(
            latest.Status,
            ReviewIdentityMatchRegenerationStatuses.Completed,
            StringComparison.Ordinal)
            ? ReviewIdentityMatchEvidenceVersions.ExpectedAfterAutomaticAssignments(
                latest.EvidenceVersion,
                latest.AutomaticallyAssignedCount)
            : latest.EvidenceVersion;
    }

    private PendingObservation Observe(
        ModelKey key,
        ReviewIdentityMatchEvidenceVersion evidence,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out PendingObservation? existing) &&
                existing.Evidence == evidence)
            {
                return existing;
            }

            PendingObservation observed = new(
                evidence,
                now,
                now + _configuration.CoalesceDelay);
            _pending[key] = observed;
            return observed;
        }
    }

    private void ClearObservation(ModelKey key)
    {
        lock (_gate)
        {
            _pending.Remove(key);
        }
    }

    private readonly record struct ModelKey(string ModelId, string ModelHash);

    private sealed record PendingObservation(
        ReviewIdentityMatchEvidenceVersion Evidence,
        DateTimeOffset DetectedAtUtc,
        DateTimeOffset DueAtUtc);
}
