using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoMetadataBackfillRepository :
    IPhotoMetadataBackfillRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPhotoMetadataBackfillRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<PhotoMetadataBackfillRefreshCandidate>> GetRefreshCandidatesAsync(
        int limit,
        int offset,
        int currentVersion,
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Metadata backfill page size must be between 1 and 1000.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (currentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentVersion));
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                revision.id,
                revision.content_sha256,
                revision.size_bytes,
                source.root_locator,
                asset.source_key,
                revision.media_type,
                metadata.asset_revision_id,
                inspection.extraction_contract_version
            FROM asset_revisions AS revision
            INNER JOIN assets AS asset ON asset.id = revision.asset_id
            INNER JOIN sources AS source ON source.id = asset.source_id
            LEFT JOIN photo_capture_metadata AS metadata
                ON metadata.asset_revision_id = revision.id
            LEFT JOIN photo_metadata_inspections AS inspection
                ON inspection.asset_revision_id = revision.id
            WHERE asset.deleted_at_utc IS NULL
              AND source.kind = 'local-folder'
              AND (
                    @force
                    OR metadata.asset_revision_id IS NULL
                    OR COALESCE(
                        inspection.extraction_contract_version,
                        @legacy_version) < @current_version
                  )
            ORDER BY revision.observed_at_utc, revision.id
            LIMIT @limit OFFSET @offset;
            """;
        command.Parameters.AddWithValue("force", force);
        command.Parameters.AddWithValue("legacy_version", PhotoMetadataExtractionContract.LegacyVersion);
        command.Parameters.AddWithValue("current_version", currentVersion);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        List<PhotoMetadataBackfillRefreshCandidate> candidates = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new PhotoMetadataBackfillRefreshCandidate(
                AssetRevisionId.From(reader.GetGuid(0)),
                new Sha256Digest(reader.GetString(1)),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                !reader.IsDBNull(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }

        return candidates;
    }
}
