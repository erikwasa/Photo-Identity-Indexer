using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Lists exact embedding revisions that can be regenerated even when no suggestion ranking exists yet.
/// </summary>
public sealed class PostgresIdentityMatchModelRepository : IIdentityMatchModelRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresIdentityMatchModelRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<ReviewIdentityMatchModelRevision>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                embedding.model_id,
                embedding.model_hash,
                COUNT(DISTINCT crop.face_occurrence_id)::integer AS face_count
            FROM embeddings AS embedding
            INNER JOIN face_crops AS crop
                ON crop.id = embedding.face_crop_id
            GROUP BY embedding.model_id, embedding.model_hash
            ORDER BY embedding.model_id, embedding.model_hash;
            """;

        List<ReviewIdentityMatchModelRevision> results = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ReviewIdentityMatchModelRevision(
                new ModelId(reader.GetString(0)),
                new Sha256Digest(reader.GetString(1)),
                reader.GetInt32(2)));
        }

        return results;
    }
}
