using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresIdentityMatchFollowUpEvidenceRepository :
    IIdentityMatchFollowUpEvidenceRepository
{
    private const string AutomaticActor = "identity-matcher:auto";
    private readonly PostgresCatalogueDatabase _database;

    public PostgresIdentityMatchFollowUpEvidenceRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<DateTimeOffset?> GetLatestQualifyingChangeAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentityMatchEvidenceVersion afterVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(afterVersion);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MAX(changed_at_utc)
            FROM (
                SELECT created_at_utc AS changed_at_utc
                FROM review_actions
                WHERE id > @review_action_id
                  AND actor <> @automatic_actor

                UNION ALL

                SELECT created_at_utc AS changed_at_utc
                FROM identity_suggestion_review_actions
                WHERE id > @suggestion_review_action_id
                  AND actor <> @automatic_actor

                UNION ALL

                SELECT created_at_utc AS changed_at_utc
                FROM person_maintenance_actions
                WHERE id > @person_merge_action_id
                  AND action_kind = 'merge'

                UNION ALL

                SELECT created_at_utc AS changed_at_utc
                FROM embeddings
                WHERE id > @embedding_id
                  AND model_id = @model_id
                  AND model_hash = @model_hash
            ) AS qualifying_changes;
            """;
        command.Parameters.AddWithValue("review_action_id", afterVersion.ReviewActionId);
        command.Parameters.AddWithValue("suggestion_review_action_id", afterVersion.SuggestionReviewActionId);
        command.Parameters.AddWithValue("person_merge_action_id", afterVersion.PersonMergeActionId);
        command.Parameters.AddWithValue("embedding_id", afterVersion.EmbeddingId);
        command.Parameters.AddWithValue("automatic_actor", AutomaticActor);
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : (DateTimeOffset)value;
    }
}
