using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed partial class PostgresVideoEvidenceRepository
{
    /// <summary>
    /// Holds the PostgreSQL source-row lock during filesystem poster publication
    /// AND poster evidence insert. Concurrent source-copy exclusion insert needs
    /// a conflicting FK KEY SHARE lock, so it waits until the published file and
    /// its DB row can be discovered together in the durable purge manifest.
    /// If exclusion wins first, the renderer is never invoked.
    /// </summary>
    public async Task<bool> PublishCurrentPosterAsync(
        SourceId sourceId, string sourceKey, AssetRevisionId revisionId,
        Sha256Digest expectedContentHash, string posterProfile, string toolHash,
        Func<CancellationToken, Task<VideoStillPoster?>> renderAndPublish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderAndPublish);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        if (sourceId.IsEmpty || revisionId.IsEmpty ||
            string.IsNullOrWhiteSpace(posterProfile) || !ValidHash(toolHash))
            throw new ArgumentException("Invalid video poster publication identity.");

        await _exclusions.EnsureReadyAsync(cancellationToken);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        // Acquires the source FOR UPDATE lock before checking exclusions.
        await RequireEligibleAsync(connection, transaction, revisionId, cancellationToken);
        await using (NpgsqlCommand current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM asset_revisions revision
                    JOIN assets asset ON asset.id = revision.asset_id
                    JOIN video_revision_metadata m ON m.asset_revision_id = revision.id
                    WHERE revision.id = @revision AND asset.source_id = @source
                      AND asset.source_key = @key
                      AND asset.media_kind = 'video' AND revision.media_kind = 'video'
                      AND asset.deleted_at_utc IS NULL
                      AND revision.content_sha256 = @hash
                      AND revision.id = (
                          SELECT newest.id FROM asset_revisions newest
                          WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                          ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1)
                      AND m.inspection_status = 'supported'
                      AND (@profile <> 'wi0151-sdr-still-v1'
                           OR m.color_transfer = 'bt709')
                      AND NOT EXISTS (
                          SELECT 1 FROM source_copy_exclusions x
                          WHERE x.source_id = asset.source_id AND x.source_key = asset.source_key)
                );
                """;
            Add(current, "revision", NpgsqlDbType.Uuid, revisionId.Value);
            Add(current, "source", NpgsqlDbType.Uuid, sourceId.Value);
            Add(current, "key", NpgsqlDbType.Text, key);
            Add(current, "hash", NpgsqlDbType.Text, expectedContentHash.ToString());
            Add(current, "profile", NpgsqlDbType.Text, posterProfile);
            if (await current.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException(
                    "Poster publication denied: source or revision is excluded, stale or unsupported.");
        }
        if (await ReadPosterAsync(connection, transaction, revisionId,
                posterProfile, toolHash, cancellationToken) is not null)
            throw new InvalidOperationException(
                "Poster already exists; verify it rather than replacing immutable evidence.");

        // Expensive FFmpeg work is deliberately inside the source lock. The
        // command is opt-in and processes one local video at a time.
        VideoStillPoster? poster = await renderAndPublish(cancellationToken);
        if (poster is null) return false;
        if (poster.RevisionId != revisionId || poster.PosterProfileId != posterProfile ||
            poster.ToolSha256 != toolHash)
            throw new InvalidOperationException("Poster producer returned mismatched identity.");
        ValidatePoster(poster);
        await using (NpgsqlCommand write = connection.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO video_still_posters (
                    asset_revision_id, poster_profile_id, tool_sha256,
                    poster_sha256, poster_relative_path, encoded_bytes,
                    width, height, generated_at_utc)
                VALUES (
                    @revision, @profile, @tool, @sha, @path,
                    @bytes, @width, @height, @generated)
                ON CONFLICT (asset_revision_id, poster_profile_id, tool_sha256) DO NOTHING;
                """;
            Add(write, "revision", NpgsqlDbType.Uuid, poster.RevisionId.Value);
            Add(write, "profile", NpgsqlDbType.Text, poster.PosterProfileId);
            Add(write, "tool", NpgsqlDbType.Text, poster.ToolSha256);
            Add(write, "sha", NpgsqlDbType.Text, poster.PosterSha256);
            Add(write, "path", NpgsqlDbType.Text, poster.PosterRelativePath);
            Add(write, "bytes", NpgsqlDbType.Bigint, poster.EncodedBytes);
            Add(write, "width", NpgsqlDbType.Integer, poster.Width);
            Add(write, "height", NpgsqlDbType.Integer, poster.Height);
            Add(write, "generated", NpgsqlDbType.TimestampTz, poster.GeneratedAtUtc.ToUniversalTime());
            if (await write.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Poster was concurrently published by another writer.");
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task RequireCurrentPosterRevisionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        AssetRevisionId revisionId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM asset_revisions revision
                JOIN assets asset ON asset.id = revision.asset_id
                WHERE revision.id = @revision
                  AND revision.media_kind = 'video' AND asset.media_kind = 'video'
                  AND revision.id = (
                      SELECT newest.id FROM asset_revisions newest
                      WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                      ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1)
            );
            """;
        Add(query, "revision", NpgsqlDbType.Uuid, revisionId.Value);
        if (await query.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException(
                "Superseded video revisions cannot acquire new poster evidence.");
    }
}
