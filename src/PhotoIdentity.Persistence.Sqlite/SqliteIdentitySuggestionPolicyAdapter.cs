using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteIdentitySuggestionPolicyAdapter :
    IIdentitySuggestionPolicyRepository
{
    private readonly SqliteIdentitySuggestionPolicyRepository _repository;

    public SqliteIdentitySuggestionPolicyAdapter(
        SqliteIdentitySuggestionPolicyRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<ReviewIdentitySuggestionPolicy> GetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default) =>
        Map(await _repository.GetAsync(modelId, modelHash, cancellationToken));

    public async Task<ReviewIdentitySuggestionPolicy> UpdateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        bool autoAssignEnabled,
        double highScoreThreshold,
        double highMarginThreshold,
        double mediumScoreThreshold,
        string actor,
        CancellationToken cancellationToken = default) =>
        Map(await _repository.UpdateAsync(
            modelId,
            modelHash,
            autoAssignEnabled,
            highScoreThreshold,
            highMarginThreshold,
            mediumScoreThreshold,
            actor,
            cancellationToken));

    private static ReviewIdentitySuggestionPolicy Map(IdentitySuggestionPolicy policy) =>
        new(
            policy.Version,
            policy.AutoAssignEnabled,
            policy.HighScoreThreshold,
            policy.HighMarginThreshold,
            policy.MediumScoreThreshold,
            policy.UpdatedBy,
            policy.UpdatedAtUtc);
}
