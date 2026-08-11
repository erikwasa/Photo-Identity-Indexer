using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueIdentityMatchModelRevision(
    ModelId ModelId,
    Sha256Digest ModelHash,
    int FaceCount);

/// <summary>
/// Lists exact embedding revisions that can be regenerated even when no suggestion ranking exists yet.
/// </summary>
public sealed class SqliteIdentityMatchRegenerationModelRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteIdentityMatchRegenerationModelRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<CatalogueIdentityMatchModelRevision>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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

        List<CatalogueIdentityMatchModelRevision> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CatalogueIdentityMatchModelRevision(
                new ModelId(reader.GetString(0)),
                new Sha256Digest(reader.GetString(1)),
                reader.GetInt32(2)));
        }

        return results;
    }
}
