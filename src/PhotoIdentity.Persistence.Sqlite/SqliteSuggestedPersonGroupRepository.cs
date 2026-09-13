using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Compatibility implementation of the suggested-person group query. PostgreSQL remains the
/// production catalogue, but keeping the provider-neutral review contract available preserves
/// local migration/test coverage.
/// </summary>
public sealed class SqliteSuggestedPersonGroupRepository : ISuggestedPersonGroupRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly IIdentitySuggestionPolicyRepository _policyRepository;

    public SqliteSuggestedPersonGroupRepository(
        SqliteCatalogueDatabase database,
        IIdentitySuggestionPolicyRepository policyRepository)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(policyRepository);
        _database = database;
        _policyRepository = policyRepository;
    }

    public async Task<ReviewSuggestedPersonGroupPage> GetGroupsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Suggested-person group page size must be between 1 and 100.");
        }

        ReviewIdentitySuggestionPolicy policy = await _policyRepository.GetAsync(
            modelId,
            modelHash,
            cancellationToken);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH pending_base AS (
                SELECT
                    suggestions.suggested_person_id,
                    COALESCE(people.display_name, 'Unnamed person') AS display_name,
                    rankings.face_occurrence_id,
                    suggestions.score,
                    rankings.score_margin,
                    rankings.generated_at_utc,
                    CASE
                        WHEN suggestions.score >= $high_score_threshold
                         AND rankings.score_margin IS NOT NULL
                         AND rankings.score_margin >= $high_margin_threshold THEN 0
                        WHEN suggestions.score >= $medium_score_threshold THEN 1
                        ELSE 2
                    END AS confidence_order
                FROM identity_suggestion_rankings AS rankings
                INNER JOIN identity_suggestions AS suggestions
                    ON suggestions.id = rankings.suggestion_id
                INNER JOIN people
                    ON people.id = suggestions.suggested_person_id
                   AND people.merged_into_person_id IS NULL
                WHERE rankings.rank = 1
                  AND suggestions.status = 'pending'
                  AND rankings.model_id = $model_id
                  AND rankings.model_hash = $model_hash
                  AND NOT EXISTS (
                      SELECT 1
                      FROM review_actions
                      WHERE review_actions.face_occurrence_id = rankings.face_occurrence_id
                        AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
                        AND review_actions.reversed_at_utc IS NULL)
            ),
            pending AS (
                SELECT
                    pending_base.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY pending_base.suggested_person_id
                        ORDER BY
                            pending_base.confidence_order,
                            pending_base.score DESC,
                            COALESCE(pending_base.score_margin, -1.0) DESC,
                            pending_base.generated_at_utc DESC,
                            pending_base.face_occurrence_id) AS representative_rank
                FROM pending_base
            ),
            representatives AS (
                SELECT
                    suggested_person_id,
                    GROUP_CONCAT(face_occurrence_id, ',') AS representative_face_ids
                FROM (
                    SELECT suggested_person_id, face_occurrence_id
                    FROM pending
                    WHERE representative_rank <= 4
                    ORDER BY suggested_person_id, representative_rank)
                GROUP BY suggested_person_id
            ),
            grouped AS (
                SELECT
                    pending.suggested_person_id,
                    pending.display_name,
                    COUNT(*) AS pending_count,
                    SUM(CASE WHEN pending.confidence_order = 0 THEN 1 ELSE 0 END) AS high_count,
                    SUM(CASE WHEN pending.confidence_order = 1 THEN 1 ELSE 0 END) AS medium_count,
                    SUM(CASE WHEN pending.confidence_order = 2 THEN 1 ELSE 0 END) AS low_count,
                    MAX(pending.score) AS strongest_score,
                    MAX(pending.score_margin) AS strongest_margin,
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM person_favorites
                        WHERE person_favorites.person_id = pending.suggested_person_id)
                        THEN 1 ELSE 0 END AS is_favorite
                FROM pending
                GROUP BY pending.suggested_person_id, pending.display_name
            )
            SELECT
                grouped.suggested_person_id,
                grouped.display_name,
                grouped.pending_count,
                grouped.high_count,
                grouped.medium_count,
                grouped.low_count,
                grouped.strongest_score,
                grouped.strongest_margin,
                grouped.is_favorite,
                representatives.representative_face_ids
            FROM grouped
            INNER JOIN representatives
                ON representatives.suggested_person_id = grouped.suggested_person_id
            ORDER BY
                grouped.is_favorite DESC,
                CASE
                    WHEN grouped.high_count > 0 THEN 0
                    WHEN grouped.medium_count > 0 THEN 1
                    ELSE 2
                END,
                grouped.pending_count DESC,
                grouped.strongest_score DESC,
                grouped.display_name COLLATE NOCASE,
                grouped.suggested_person_id
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, modelId, modelHash, policy);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        List<ReviewSuggestedPersonGroup> items = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                string representatives = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);
                FaceOccurrenceId[] representativeFaceIds = representatives
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => FaceOccurrenceId.From(Guid.Parse(value)))
                    .ToArray();

                items.Add(new ReviewSuggestedPersonGroup(
                    new ReviewSuggestionGalleryPerson(
                        PersonId.From(Guid.Parse(reader.GetString(0))),
                        reader.GetString(1)),
                    checked((int)reader.GetInt64(2)),
                    checked((int)reader.GetInt64(3)),
                    checked((int)reader.GetInt64(4)),
                    checked((int)reader.GetInt64(5)),
                    reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.GetInt64(8) != 0,
                    representativeFaceIds));
            }
        }

        using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.CommandText =
            """
            SELECT COUNT(DISTINCT suggestions.suggested_person_id)
            FROM identity_suggestion_rankings AS rankings
            INNER JOIN identity_suggestions AS suggestions
                ON suggestions.id = rankings.suggestion_id
            INNER JOIN people
                ON people.id = suggestions.suggested_person_id
               AND people.merged_into_person_id IS NULL
            WHERE rankings.rank = 1
              AND suggestions.status = 'pending'
              AND rankings.model_id = $model_id
              AND rankings.model_hash = $model_hash
              AND NOT EXISTS (
                  SELECT 1
                  FROM review_actions
                  WHERE review_actions.face_occurrence_id = rankings.face_occurrence_id
                    AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
                    AND review_actions.reversed_at_utc IS NULL);
            """;
        countCommand.Parameters.AddWithValue("$model_id", modelId.ToString());
        countCommand.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        object? totalValue = await countCommand.ExecuteScalarAsync(cancellationToken);
        int total = checked(Convert.ToInt32(totalValue));

        return new ReviewSuggestedPersonGroupPage(items, offset, limit, total);
    }

    private static void AddParameters(
        SqliteCommand command,
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy policy)
    {
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("$high_score_threshold", policy.HighScoreThreshold);
        command.Parameters.AddWithValue("$high_margin_threshold", policy.HighMarginThreshold);
        command.Parameters.AddWithValue("$medium_score_threshold", policy.MediumScoreThreshold);
    }
}
