using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Lists exact embedding-model revisions directly from PostgreSQL face-crop embeddings.
/// </summary>
public sealed class PostgresIdentityMatchRegenerationModelRepository :
    IIdentityMatchRegenerationModelRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresIdentityMatchRegenerationModelRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<ReviewIdentityMatchModelRevision>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                embedding.model_id,
                embedding.model_hash,
                COUNT(DISTINCT crop.face_occurrence_id) AS face_count
            FROM embeddings AS embedding
            INNER JOIN face_crops AS crop
                ON crop.id = embedding.face_crop_id
            GROUP BY embedding.model_id, embedding.model_hash
            ORDER BY embedding.model_id, embedding.model_hash;
            """;

        List<ReviewIdentityMatchModelRevision> results = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long count = reader.GetInt64(2);
            results.Add(new ReviewIdentityMatchModelRevision(
                new ModelId(reader.GetString(0)),
                new Sha256Digest(reader.GetString(1)),
                checked((int)count)));
        }

        return results;
    }
}
