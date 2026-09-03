using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Compatibility adapter that preserves the accepted SQLite regeneration implementation while
/// exposing provider-neutral run/target state to API and hosted-service composition.
/// </summary>
public sealed class SqliteIdentityMatchRegenerationAdapter :
    IIdentityMatchRegenerationRepository
{
    private readonly SqliteIdentityMatchRegenerationRepository _repository;

    public SqliteIdentityMatchRegenerationAdapter(
        SqliteIdentityMatchRegenerationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<ReviewIdentityMatchRegenerationRun> StartAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int policyVersion,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default) =>
        Map(await _repository.StartAsync(
            modelId,
            modelHash,
            policyVersion,
            requestedBy,
            requestedAtUtc,
            cancellationToken));

    public async Task<ReviewIdentityMatchRegenerationRun?> GetLatestAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        CatalogueIdentityMatchRegenerationRun? run =
            await _repository.GetLatestAsync(modelId, modelHash, cancellationToken);
        return run is null ? null : Map(run);
    }

    public async Task<ReviewIdentityMatchRegenerationRun?> GetNextActiveAsync(
        CancellationToken cancellationToken = default)
    {
        CatalogueIdentityMatchRegenerationRun? run =
            await _repository.GetNextActiveAsync(cancellationToken);
        return run is null ? null : Map(run);
    }

    public async Task<ReviewIdentityMatchRegenerationTarget?> ClaimNextTargetAsync(
        Guid runId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        CatalogueIdentityMatchRegenerationTarget? target =
            await _repository.ClaimNextTargetAsync(runId, nowUtc, cancellationToken);
        return target is null ? null : Map(target);
    }

    public Task CompleteTargetAsync(
        Guid runId,
        FaceOccurrenceId faceOccurrenceId,
        int suggestionCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        _repository.CompleteTargetAsync(
            runId,
            faceOccurrenceId,
            suggestionCount,
            nowUtc,
            cancellationToken);

    public Task FailTargetAsync(
        Guid runId,
        FaceOccurrenceId faceOccurrenceId,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        _repository.FailTargetAsync(
            runId,
            faceOccurrenceId,
            error,
            nowUtc,
            cancellationToken);

    public Task CompleteRunAsync(
        Guid runId,
        int automaticallyAssignedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        _repository.CompleteRunAsync(
            runId,
            automaticallyAssignedCount,
            nowUtc,
            cancellationToken);

    public Task MarkFailedAsync(
        Guid runId,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        _repository.MarkFailedAsync(runId, error, nowUtc, cancellationToken);

    public Task<bool> EvidenceStillMatchesAsync(
        ReviewIdentityMatchRegenerationRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        return _repository.EvidenceStillMatchesAsync(Map(run), cancellationToken);
    }

    private static ReviewIdentityMatchRegenerationRun Map(
        CatalogueIdentityMatchRegenerationRun run) =>
        new(
            run.Id,
            run.ModelId,
            run.ModelHash,
            run.PolicyVersion,
            run.Status,
            new ReviewIdentityMatchEvidenceVersion(
                run.EvidenceVersion.ReviewActionId,
                run.EvidenceVersion.SuggestionReviewActionId,
                run.EvidenceVersion.PersonMergeActionId,
                run.EvidenceVersion.EmbeddingId),
            run.TargetCount,
            run.ProcessedTargetCount,
            run.SuggestedTargetCount,
            run.SuggestionCount,
            run.AutomaticallyAssignedCount,
            run.ErrorCount,
            run.RequestedBy,
            run.RequestedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.UpdatedAtUtc,
            run.Error);

    private static CatalogueIdentityMatchRegenerationRun Map(
        ReviewIdentityMatchRegenerationRun run) =>
        new(
            run.Id,
            run.ModelId,
            run.ModelHash,
            run.PolicyVersion,
            run.Status,
            new IdentityMatchEvidenceVersion(
                run.EvidenceVersion.ReviewActionId,
                run.EvidenceVersion.SuggestionReviewActionId,
                run.EvidenceVersion.PersonMergeActionId,
                run.EvidenceVersion.EmbeddingId),
            run.TargetCount,
            run.ProcessedTargetCount,
            run.SuggestedTargetCount,
            run.SuggestionCount,
            run.AutomaticallyAssignedCount,
            run.ErrorCount,
            run.RequestedBy,
            run.RequestedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.UpdatedAtUtc,
            run.Error);

    private static ReviewIdentityMatchRegenerationTarget Map(
        CatalogueIdentityMatchRegenerationTarget target) =>
        new(
            target.RunId,
            target.FaceOccurrenceId,
            target.Ordinal,
            target.Status,
            target.SuggestionCount,
            target.Error);
}
