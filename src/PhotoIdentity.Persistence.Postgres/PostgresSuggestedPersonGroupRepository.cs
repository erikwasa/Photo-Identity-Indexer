using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresSuggestedPersonGroupRepository : ISuggestedPersonGroupRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly IIdentitySuggestionPolicyRepository _policyRepository;

    public PostgresSuggestedPersonGroupRepository(
        PostgresCatalogueDatabase database,
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

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH pending AS (
                SELECT
                    suggestions.suggested_person_id,
                    COALESCE(people.display_name, 'Unnamed person') AS display_name,
                    rankings.face_occurrence_id,
                    suggestions.score,
                    rankings.score_margin,
                    rankings.generated_at_utc,
                    CASE
                        WHEN suggestions.score >= @high_score_threshold
                         AND rankings.score_margin IS NOT NULL
                         AND rankings.score_margin >= @high_margin_threshold THEN 0
                        WHEN suggestions.score >= @medium_score_threshold THEN 1
                        ELSE 2
                    END AS confidence_order,
                    ROW_NUMBER() OVER (
                        PARTITION BY suggestions.suggested_person_id
                        ORDER BY
                            CASE
                                WHEN suggestions.score >= @high_score_threshold
                                 AND rankings.score_margin IS NOT NULL
                                 AND rankings.score_margin >= @high_margin_threshold THEN 0
                                WHEN suggestions.score >= @medium_score_threshold THEN 1
                                ELSE 2
                            END,
                            suggestions.score DESC,
                            rankings.score_margin DESC NULLS LAST,
                            rankings.generated_at_utc DESC,
                            rankings.face_occurrence_id) AS representative_rank
                FROM identity_suggestion_rankings AS rankings
                INNER JOIN identity_suggestions AS suggestions
                    ON suggestions.id = rankings.suggestion_id
                INNER JOIN people
                    ON people.id = suggestions.suggested_person_id
                   AND people.merged_into_person_id IS NULL
                WHERE rankings.rank = 1
                  AND suggestions.status = 'pending'
                  AND rankings.model_id = @model_id
                  AND rankings.model_hash = @model_hash
                  AND NOT EXISTS (
                      SELECT 1
                      FROM review_actions
                      WHERE review_actions.face_occurrence_id = rankings.face_occurrence_id
                        AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
                        AND review_actions.reversed_at_utc IS NULL)
            ),
            grouped AS (
                SELECT
                    pending.suggested_person_id,
                    pending.display_name,
                    COUNT(*)::integer AS pending_count,
                    COUNT(*) FILTER (WHERE pending.confidence_order = 0)::integer AS high_count,
                    COUNT(*) FILTER (WHERE pending.confidence_order = 1)::integer AS medium_count,
                    COUNT(*) FILTER (WHERE pending.confidence_order = 2)::integer AS low_count,
                    MAX(pending.score) AS strongest_score,
                    MAX(pending.score_margin) AS strongest_margin,
                    EXISTS (
                        SELECT 1
                        FROM person_favorites
                        WHERE person_favorites.person_id = pending.suggested_person_id) AS is_favorite,
                    STRING_AGG(
                        pending.face_occurrence_id::text,
                        ','
                        ORDER BY pending.representative_rank)
                        FILTER (WHERE pending.representative_rank <= 4) AS representative_face_ids
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
                grouped.representative_face_ids
            FROM grouped
            ORDER BY
                grouped.is_favorite DESC,
                CASE
                    WHEN grouped.high_count > 0 THEN 0
                    WHEN grouped.medium_count > 0 THEN 1
                    ELSE 2
                END,
                grouped.pending_count DESC,
                grouped.strongest_score DESC,
                LOWER(grouped.display_name),
                grouped.suggested_person_id
            LIMIT @limit OFFSET @offset;
            """;
        AddParameters(command, modelId, modelHash, policy);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        List<ReviewSuggestedPersonGroup> items = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
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
                        PersonId.From(reader.GetGuid(0)),
                        reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.GetBoolean(8),
                    representativeFaceIds));
            }
        }

        await using NpgsqlCommand countCommand = connection.CreateCommand();
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
              AND rankings.model_id = @model_id
              AND rankings.model_hash = @model_hash
              AND NOT EXISTS (
                  SELECT 1
                  FROM review_actions
                  WHERE review_actions.face_occurrence_id = rankings.face_occurrence_id
                    AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
                    AND review_actions.reversed_at_utc IS NULL);
            """;
        countCommand.Parameters.AddWithValue("model_id", modelId.ToString());
        countCommand.Parameters.AddWithValue("model_hash", modelHash.ToString());
        object? totalValue = await countCommand.ExecuteScalarAsync(cancellationToken);
        int total = checked(Convert.ToInt32(totalValue));

        return new ReviewSuggestedPersonGroupPage(items, offset, limit, total);
    }

    private static void AddParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy policy)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("high_score_threshold", policy.HighScoreThreshold);
        command.Parameters.AddWithValue("high_margin_threshold", policy.HighMarginThreshold);
        command.Parameters.AddWithValue("medium_score_threshold", policy.MediumScoreThreshold);
    }
}
