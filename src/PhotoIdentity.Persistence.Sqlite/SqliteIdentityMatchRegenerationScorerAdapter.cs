using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteIdentityMatchRegenerationScorerAdapter :
    IIdentityMatchRegenerationScorer
{
    private readonly SqliteIdentityMatchRegenerationScorer _scorer;

    public SqliteIdentityMatchRegenerationScorerAdapter(
        SqliteIdentityMatchRegenerationScorer scorer)
    {
        ArgumentNullException.ThrowIfNull(scorer);
        _scorer = scorer;
    }

    public Task<int> ScoreTargetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default) =>
        _scorer.ScoreTargetAsync(
            modelId,
            modelHash,
            faceOccurrenceId,
            cancellationToken);

    public Task RemoveObsoleteRankingsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        Guid runId,
        CancellationToken cancellationToken = default) =>
        _scorer.RemoveObsoleteRankingsAsync(
            modelId,
            modelHash,
            runId,
            cancellationToken);
}
