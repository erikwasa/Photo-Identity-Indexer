using Npgsql;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>Snapshot of a video revision and its poster evidence for one selected source.</summary>
public sealed record VideoPosterCleanupRow(
    AssetRevisionId RevisionId, bool IsCurrent, string? RelativePath,
    string? PosterSha256, long? EncodedBytes, DateTimeOffset? GeneratedAtUtc,
    string? PosterProfileId, string? ToolSha256);

/// <summary>
/// PostgreSQL authority for a bounded, operator-driven poster cleanup.
/// Never infer ownership solely from a directory name or a file checksum.
/// </summary>
public sealed class PostgresVideoPosterCleanupRepository(PostgresCatalogueDatabase database)
{
    private readonly PostgresSourceCopyExclusionRepository _exclusions = new(database);

    public async Task<IReadOnlyList<VideoPosterCleanupRow>> SnapshotAsync(
        SourceId sourceId, CancellationToken token = default)
    {
        await _exclusions.EnsureReadyAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id,
                   (asset.deleted_at_utc IS NULL AND excluded.source_id IS NULL
                    AND revision.id = (
                        SELECT newest.id FROM asset_revisions newest
                        WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                        ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1)
                   ) AS is_current,
                   poster.poster_relative_path, poster.poster_sha256,
                   poster.encoded_bytes, poster.generated_at_utc,
                   poster.poster_profile_id, poster.tool_sha256
            FROM assets asset
            JOIN asset_revisions revision ON revision.asset_id = asset.id
            LEFT JOIN source_copy_exclusions excluded
                ON excluded.source_id = asset.source_id AND excluded.source_key = asset.source_key
            LEFT JOIN video_still_posters poster ON poster.asset_revision_id = revision.id
            WHERE asset.source_id = @source
              AND asset.media_kind = 'video' AND revision.media_kind = 'video'
            ORDER BY revision.id, poster.poster_profile_id, poster.tool_sha256;
            """;
        command.Parameters.AddWithValue("source", sourceId.Value);
        List<VideoPosterCleanupRow> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            rows.Add(new VideoPosterCleanupRow(
                AssetRevisionId.From(reader.GetGuid(0)), reader.GetBoolean(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return rows;
    }

    /// <summary>
    /// Rechecks ownership and staleness under the same source lock used by video
    /// registration, then removes exactly the unchanged stale poster evidence row.
    /// The caller must separately verify and delete only its matching JPEG bytes.
    /// </summary>
    public async Task<bool> RetireStalePosterAsync(
        SourceId sourceId, VideoPosterCleanupRow row, CancellationToken token = default)
    {
        if (row.PosterProfileId is null || row.ToolSha256 is null ||
            row.PosterSha256 is null || row.RelativePath is null)
            throw new ArgumentException("A recorded poster is required.", nameof(row));
        await _exclusions.EnsureReadyAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await LockSourceAsync(connection, transaction, sourceId, token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM video_still_posters poster
            USING asset_revisions revision, assets asset
            WHERE poster.asset_revision_id = revision.id AND revision.asset_id = asset.id
              AND asset.source_id = @source
              AND asset.media_kind = 'video' AND revision.media_kind = 'video'
              AND poster.asset_revision_id = @revision
              AND poster.poster_profile_id = @profile AND poster.tool_sha256 = @tool
              AND poster.poster_sha256 = @sha AND poster.poster_relative_path = @path
              AND (asset.deleted_at_utc IS NOT NULL OR EXISTS (
                       SELECT 1 FROM source_copy_exclusions x
                       WHERE x.source_id = asset.source_id AND x.source_key = asset.source_key)
                   OR revision.id IS DISTINCT FROM (
                       SELECT newest.id FROM asset_revisions newest
                       WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                       ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1));
            """;
        command.Parameters.AddWithValue("source", sourceId.Value);
        command.Parameters.AddWithValue("revision", row.RevisionId.Value);
        command.Parameters.AddWithValue("profile", row.PosterProfileId);
        command.Parameters.AddWithValue("tool", row.ToolSha256);
        command.Parameters.AddWithValue("sha", row.PosterSha256);
        command.Parameters.AddWithValue("path", row.RelativePath);
        int count = await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return count == 1;
    }

    /// <summary>
    /// For unreferenced JPEGs, require the embedded revision GUID to belong to the
    /// selected source and be non-current. Deleted/purged revisions cannot be
    /// attributed safely and are NEVER authorized by this method.
    /// </summary>
    public async Task<bool> MayRemoveUnreferencedAsync(
        SourceId sourceId, AssetRevisionId revisionId, string profile, string tool,
        CancellationToken token = default)
    {
        await _exclusions.EnsureReadyAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await LockSourceAsync(connection, transaction, sourceId, token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM asset_revisions revision
                JOIN assets asset ON asset.id = revision.asset_id
                WHERE revision.id = @revision AND revision.media_kind = 'video'
                  AND asset.media_kind = 'video' AND asset.source_id = @source
                  AND (asset.deleted_at_utc IS NOT NULL OR EXISTS (
                       SELECT 1 FROM source_copy_exclusions x
                       WHERE x.source_id = asset.source_id AND x.source_key = asset.source_key)
                   OR revision.id IS DISTINCT FROM (
                       SELECT newest.id FROM asset_revisions newest
                       WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                       ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1))
                  AND NOT EXISTS (
                       SELECT 1 FROM video_still_posters poster
                       WHERE poster.asset_revision_id = revision.id
                         AND poster.poster_profile_id = @profile
                         AND poster.tool_sha256 = @tool));
            """;
        command.Parameters.AddWithValue("source", sourceId.Value);
        command.Parameters.AddWithValue("revision", revisionId.Value);
        command.Parameters.AddWithValue("profile", profile);
        command.Parameters.AddWithValue("tool", tool);
        bool permitted = await command.ExecuteScalarAsync(token) is true;
        await transaction.CommitAsync(token);
        return permitted;
    }

    private static async Task LockSourceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, SourceId sourceId,
        CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sources WHERE id = @source FOR UPDATE;";
        command.Parameters.AddWithValue("source", sourceId.Value);
        if (await command.ExecuteScalarAsync(token) is null)
            throw new InvalidOperationException("The selected video source no longer exists.");
    }
}
