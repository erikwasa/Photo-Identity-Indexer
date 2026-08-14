using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Pages revisions that have not yet received a capture-metadata inspection record.
/// Paging lets the executor move beyond deferred online-only placeholders without marking them complete.
/// </summary>
public sealed class SqlitePhotoMetadataBackfillRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqlitePhotoMetadataBackfillRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<PhotoMetadataBackfillCandidate>> GetCandidatesAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Metadata backfill page size must be between 1 and 1000.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsurePhotoMetadataSchemaAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset_revisions.id,
                asset_revisions.content_sha256,
                asset_revisions.size_bytes,
                sources.root_locator,
                assets.source_key,
                asset_revisions.media_type
            FROM asset_revisions
            INNER JOIN assets ON assets.id = asset_revisions.asset_id
            INNER JOIN sources ON sources.id = assets.source_id
            LEFT JOIN photo_capture_metadata
                ON photo_capture_metadata.asset_revision_id = asset_revisions.id
            WHERE photo_capture_metadata.asset_revision_id IS NULL
              AND assets.deleted_at_utc IS NULL
              AND sources.kind = 'local-folder'
            ORDER BY asset_revisions.observed_at_utc, asset_revisions.id
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        List<PhotoMetadataBackfillCandidate> candidates = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new PhotoMetadataBackfillCandidate(
                AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
                new Sha256Digest(reader.GetString(1)),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return candidates;
    }

    private static async Task EnsurePhotoMetadataSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_capture_metadata (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                taken_at_local TEXT NULL,
                utc_offset_minutes INTEGER NULL CHECK (utc_offset_minutes IS NULL OR utc_offset_minutes BETWEEN -840 AND 840),
                latitude REAL NULL CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
                longitude REAL NULL CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
                extracted_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CHECK ((latitude IS NULL) = (longitude IS NULL)),
                CHECK (utc_offset_minutes IS NULL OR taken_at_local IS NOT NULL));
            CREATE INDEX IF NOT EXISTS ix_photo_capture_metadata_taken
                ON photo_capture_metadata (taken_at_local, asset_revision_id);
            CREATE INDEX IF NOT EXISTS ix_photo_capture_metadata_location
                ON photo_capture_metadata (latitude, longitude, asset_revision_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
