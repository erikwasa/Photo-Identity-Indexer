using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Promotes persisted High rank-one suggestions through the canonical PostgreSQL suggestion
/// acceptance path. Human review that wins the race remains authoritative and is skipped.
/// </summary>
public sealed class PostgresIdentityAutoAssignmentService :
    IIdentityAutoAssignmentService
{
    public const string AutomaticActor = "identity-matcher:auto";

    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresIdentityAutoAssignmentService(
        PostgresCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReviewIdentityAutoAssignmentSummary> ApplyAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        if (!policy.AutoAssignEnabled)
        {
            return new ReviewIdentityAutoAssignmentSummary(0, 0, 0);
        }

        IReadOnlyList<AutoAssignmentCandidate> candidates = await ReadCandidatesAsync(
            modelId,
            modelHash,
            policy.HighScoreThreshold,
            policy.HighMarginThreshold,
            cancellationToken);

        IReviewSuggestionRepository reviewSuggestions =
            new PostgresReviewSuggestionRepository(_database);
        int assignedCount = 0;
        int skippedCount = 0;
        foreach (AutoAssignmentCandidate candidate in candidates)
        {
            DateTimeOffset decidedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            string note = FormattableString.Invariant(
                $"Automatic assignment from persisted High rank-1 identity suggestion; model-id={modelId}; model-hash={modelHash}; score={candidate.Score:R}; rank1-rank2-margin={candidate.ScoreMargin:R}; policy-version={policy.Version}; high-score-threshold={policy.HighScoreThreshold:R}; high-margin-threshold={policy.HighMarginThreshold:R}; medium-score-threshold={policy.MediumScoreThreshold:R}.");

            try
            {
                _ = await reviewSuggestions.AcceptAsync(
                    candidate.FaceOccurrenceId,
                    candidate.SuggestionId,
                    AutomaticActor,
                    decidedAtUtc,
                    note,
                    cancellationToken);
                assignedCount++;
            }
            catch (InvalidOperationException exception)
                when (IsConcurrentOrSupersedingDecision(exception))
            {
                skippedCount++;
            }
            catch (KeyNotFoundException)
            {
                skippedCount++;
            }
        }

        return new ReviewIdentityAutoAssignmentSummary(
            candidates.Count,
            assignedCount,
            skippedCount);
    }

    private async Task<IReadOnlyList<AutoAssignmentCandidate>> ReadCandidatesAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        double scoreThreshold,
        double marginThreshold,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                suggestion.id,
                ranking.face_occurrence_id,
                suggestion.score,
                ranking.score_margin
            FROM identity_suggestion_rankings AS ranking
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
            WHERE ranking.model_id = @model_id
              AND ranking.model_hash = @model_hash
              AND ranking.rank = 1
              AND suggestion.status = 'pending'
              AND suggestion.score >= @score_threshold
              AND ranking.score_margin IS NOT NULL
              AND ranking.score_margin >= @margin_threshold
              AND NOT EXISTS (
                  SELECT 1
                  FROM review_actions AS action
                  WHERE action.face_occurrence_id = ranking.face_occurrence_id
                    AND action.action_kind IN ('assign', 'unknown', 'reject')
                    AND action.reversed_at_utc IS NULL)
            ORDER BY ranking.face_occurrence_id;
            """;
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("score_threshold", scoreThreshold);
        command.Parameters.AddWithValue("margin_threshold", marginThreshold);

        List<AutoAssignmentCandidate> candidates = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new AutoAssignmentCandidate(
                reader.GetInt64(0),
                FaceOccurrenceId.From(reader.GetGuid(1)),
                reader.GetDouble(2),
                reader.GetDouble(3)));
        }

        return candidates;
    }

    private static bool IsConcurrentOrSupersedingDecision(
        InvalidOperationException exception) =>
        exception.Message.Contains("already been reviewed", StringComparison.Ordinal)
        || exception.Message.Contains("already been accepted", StringComparison.Ordinal)
        || exception.Message.Contains("already been rejected", StringComparison.Ordinal)
        || exception.Message.Contains(
            "changed before the review decision",
            StringComparison.Ordinal);

    private sealed record AutoAssignmentCandidate(
        long SuggestionId,
        FaceOccurrenceId FaceOccurrenceId,
        double Score,
        double ScoreMargin);
}
