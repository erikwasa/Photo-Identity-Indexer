using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresAssetRevisionLookupRepository : IAssetRevisionLookupRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresAssetRevisionLookupRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }
    public async Task<AssetRevisionLookup?> GetRevisionAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = AssetRevisionSelect + " WHERE revision.id = @revision_id;";
        command.Parameters.AddWithValue("@revision_id", revisionId.Value);
        return await ReadAssetRevisionAsync(command, cancellationToken);
    }

    public async Task<AssetRevisionLookup?> FindRevisionAsync(
        string sourceKey,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = AssetRevisionSelect + "\n" + """
            WHERE asset.source_key = @source_key
              AND revision.content_sha256 = @content_sha256
            ORDER BY revision.observed_at_utc DESC, revision.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@source_key", sourceKey);
        command.Parameters.AddWithValue("@content_sha256", contentHash.ToString());
        return await ReadAssetRevisionAsync(command, cancellationToken);
    }

    private const string AssetRevisionSelect = """
        SELECT
            revision.id,
            revision.asset_id,
            asset.source_id,
            source.kind,
            source.root_locator,
            asset.source_key,
            revision.content_sha256,
            revision.size_bytes,
            revision.media_type
        FROM asset_revisions AS revision
        INNER JOIN assets AS asset ON asset.id = revision.asset_id
        INNER JOIN sources AS source ON source.id = asset.source_id
        """;

    private static async Task<AssetRevisionLookup?> ReadAssetRevisionAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AssetRevisionLookup(
            AssetRevisionId.From(reader.GetGuid(0)),
            AssetId.From(reader.GetGuid(1)),
            SourceId.From(reader.GetGuid(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            new Sha256Digest(reader.GetString(6)),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

}
