using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteIdentityAutoAssignmentAdapter :
    IIdentityAutoAssignmentService
{
    private readonly SqliteIdentityAutoAssignmentService _service;

    public SqliteIdentityAutoAssignmentAdapter(
        SqliteIdentityAutoAssignmentService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public async Task<ReviewIdentityAutoAssignmentSummary> ApplyAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        IdentitySuggestionPolicy sqlitePolicy = new(
            policy.Version,
            policy.AutoAssignEnabled,
            policy.HighScoreThreshold,
            policy.HighMarginThreshold,
            policy.MediumScoreThreshold,
            policy.UpdatedBy,
            policy.UpdatedAtUtc);
        IdentityAutoAssignmentSummary summary = await _service.ApplyAsync(
            modelId,
            modelHash,
            sqlitePolicy,
            cancellationToken);
        return new ReviewIdentityAutoAssignmentSummary(
            summary.CandidateCount,
            summary.AssignedCount,
            summary.SkippedCount);
    }
}
