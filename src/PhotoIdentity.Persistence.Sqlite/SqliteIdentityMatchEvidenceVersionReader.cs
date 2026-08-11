using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Reads the identity-affecting counters used to prove that a regeneration still represents
/// the same canonical evidence. Kept separate from the run repository so finalization can
/// account for the audit rows created by its own automatic assignments.
/// </summary>
public sealed class SqliteIdentityMatchEvidenceVersionReader
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteIdentityMatchEvidenceVersionReader(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IdentityMatchEvidenceVersion> ReadAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE((SELECT MAX(id) FROM review_actions), 0),
                COALESCE((SELECT MAX(id) FROM identity_suggestion_review_actions), 0),
                COALESCE((SELECT MAX(id) FROM person_maintenance_actions WHERE action_kind = 'merge'), 0),
                COALESCE((
                    SELECT MAX(embedding.id)
                    FROM embeddings AS embedding
                    WHERE embedding.model_id = $model_id
                      AND embedding.model_hash = $model_hash), 0);
            """;
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not read identity evidence version.");
        }

        return new IdentityMatchEvidenceVersion(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    public static IdentityMatchEvidenceVersion ExpectedAfterAutomaticAssignments(
        IdentityMatchEvidenceVersion before,
        int automaticallyAssignedCount)
    {
        if (automaticallyAssignedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(automaticallyAssignedCount));
        }

        return before with
        {
            ReviewActionId = checked(before.ReviewActionId + automaticallyAssignedCount),
            SuggestionReviewActionId = checked(before.SuggestionReviewActionId + automaticallyAssignedCount),
        };
    }
}
