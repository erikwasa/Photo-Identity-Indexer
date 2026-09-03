using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Reads the same monotonic identity-affecting evidence counters as the accepted SQLite
/// implementation. Embedding evidence is scoped to one exact model revision; review,
/// suggestion-decision and person-merge counters are catalogue-wide by design.
/// </summary>
public sealed class PostgresIdentityMatchEvidenceVersionReader :
    IIdentityMatchEvidenceVersionReader
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresIdentityMatchEvidenceVersionReader(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ReviewIdentityMatchEvidenceVersion> ReadAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COALESCE((SELECT MAX(id) FROM review_actions), 0),
                COALESCE((SELECT MAX(id) FROM identity_suggestion_review_actions), 0),
                COALESCE((
                    SELECT MAX(id)
                    FROM person_maintenance_actions
                    WHERE action_kind = 'merge'), 0),
                COALESCE((
                    SELECT MAX(embedding.id)
                    FROM embeddings AS embedding
                    WHERE embedding.model_id = @model_id
                      AND embedding.model_hash = @model_hash), 0);
            """;
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Could not read PostgreSQL identity evidence version.");
        }

        return new ReviewIdentityMatchEvidenceVersion(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }
}
