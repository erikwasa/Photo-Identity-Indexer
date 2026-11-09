using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Metadata-only, one-current-revision operator refresh. Immutable source
/// inspection evidence is never rewritten: only previously NULL fields are
/// populated and a new revision-bound provenance action is appended.
/// </summary>
public sealed record VideoTechnicalMetadataRefreshTarget(
    SourceId SourceId, string SourceKey, string ContentSha256, long SizeBytes);

public sealed record VideoTechnicalMetadataRefreshFacts(
    string? CameraMake, string? CameraModel, long? VideoBitrateBps,
    int? AudioSampleRateHz, int? AudioChannels, string? AudioChannelLayout,
    string ToolSha256, string InspectionProfileId, DateTimeOffset InspectedAtUtc);

public sealed class PostgresVideoTechnicalMetadataRefreshRepository(
    PostgresCatalogueDatabase database)
{
    public async Task<VideoTechnicalMetadataRefreshTarget?> GetCurrentTargetAsync(
        AssetRevisionId revisionId, string registeredRoot, CancellationToken token = default)
    {
        string root = Path.GetFullPath(registeredRoot);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source.id, asset.source_key, revision.content_sha256, revision.size_bytes
            FROM asset_revisions revision
            JOIN assets asset ON asset.id = revision.asset_id
            JOIN sources source ON source.id = asset.source_id
            JOIN video_revision_metadata m ON m.asset_revision_id = revision.id
            WHERE revision.id = @revision
              AND revision.media_kind = 'video' AND asset.media_kind = 'video'
              AND asset.deleted_at_utc IS NULL
              AND source.kind = 'local-folder' AND source.root_locator = @root
              AND revision.id = (
                  SELECT newest.id FROM asset_revisions newest
                  WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                  ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id = asset.source_id
                    AND excluded.source_key = asset.source_key);
            """;
        command.Parameters.AddWithValue("revision", revisionId.Value);
        command.Parameters.AddWithValue("root", root);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new(SourceId.From(reader.GetGuid(0)), reader.GetString(1),
                reader.GetString(2), reader.GetInt64(3))
            : null;
    }

    public async Task<int> FillMissingAsync(
        AssetRevisionId revisionId, string registeredRoot, string verifiedContentSha256,
        VideoTechnicalMetadataRefreshFacts facts, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (revisionId.IsEmpty || verifiedContentSha256.Length != 64 ||
            !verifiedContentSha256.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            facts.ToolSha256.Length != 64 ||
            !facts.ToolSha256.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            facts.InspectionProfileId.Length is < 1 or > 100 ||
            facts.VideoBitrateBps is <= 0 or > 10_000_000_000 ||
            facts.AudioSampleRateHz is <= 0 or > 384000 ||
            facts.AudioChannels is <= 0 or > 32 ||
            InvalidCamera(facts.CameraMake) || InvalidCamera(facts.CameraModel) ||
            (facts.AudioChannelLayout is { } layout &&
             (layout.Length is < 1 or > 64 || layout.Any(char.IsControl))))
            throw new ArgumentException("Invalid bounded revision metadata refresh evidence.");

        string root = Path.GetFullPath(registeredRoot);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);

        // Lock the same source row as source-copy exclusion insertion. Then
        // establish exact live revision, source root and verified content hash.
        await using NpgsqlCommand inspect = connection.CreateCommand();
        inspect.Transaction = transaction;
        inspect.CommandText = """
            SELECT m.camera_make, m.camera_model, m.video_bitrate_bps,
                   m.audio_sample_rate_hz, m.audio_channels, m.audio_channel_layout
            FROM asset_revisions revision
            JOIN assets asset ON asset.id = revision.asset_id
            JOIN sources source ON source.id = asset.source_id
            JOIN video_revision_metadata m ON m.asset_revision_id = revision.id
            WHERE revision.id = @revision AND revision.content_sha256 = @hash
              AND revision.media_kind = 'video' AND asset.media_kind = 'video'
              AND asset.deleted_at_utc IS NULL
              AND source.kind = 'local-folder' AND source.root_locator = @root
              AND revision.id = (
                  SELECT newest.id FROM asset_revisions newest
                  WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                  ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id = asset.source_id
                    AND excluded.source_key = asset.source_key)
            FOR UPDATE OF source;
            """;
        inspect.Parameters.AddWithValue("revision", revisionId.Value);
        inspect.Parameters.AddWithValue("hash", verifiedContentSha256);
        inspect.Parameters.AddWithValue("root", root);

        List<string> changed = [];
        await using (NpgsqlDataReader reader = await inspect.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
                throw new InvalidOperationException(
                    "Current video revision, verified hash, source or exclusion changed; no refresh written.");
            if (reader.IsDBNull(0) && facts.CameraMake is not null) changed.Add("camera_make");
            if (reader.IsDBNull(1) && facts.CameraModel is not null) changed.Add("camera_model");
            if (reader.IsDBNull(2) && facts.VideoBitrateBps is not null) changed.Add("video_bitrate_bps");
            if (reader.IsDBNull(3) && facts.AudioSampleRateHz is not null) changed.Add("audio_sample_rate_hz");
            if (reader.IsDBNull(4) && facts.AudioChannels is not null) changed.Add("audio_channels");
            if (reader.IsDBNull(5) && facts.AudioChannelLayout is not null) changed.Add("audio_channel_layout");
        }

        if (changed.Count == 0)
        {
            await transaction.CommitAsync(token);
            return 0;
        }

        await using (NpgsqlCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE video_revision_metadata
                SET camera_make = COALESCE(camera_make, @make),
                    camera_model = COALESCE(camera_model, @model),
                    video_bitrate_bps = COALESCE(video_bitrate_bps, @bitrate),
                    audio_sample_rate_hz = COALESCE(audio_sample_rate_hz, @sample_rate),
                    audio_channels = COALESCE(audio_channels, @channels),
                    audio_channel_layout = COALESCE(audio_channel_layout, @layout)
                WHERE asset_revision_id = @revision;
                """;
            Add(update, "revision", NpgsqlDbType.Uuid, revisionId.Value);
            Add(update, "make", NpgsqlDbType.Text, facts.CameraMake);
            Add(update, "model", NpgsqlDbType.Text, facts.CameraModel);
            Add(update, "bitrate", NpgsqlDbType.Bigint, facts.VideoBitrateBps);
            Add(update, "sample_rate", NpgsqlDbType.Integer, facts.AudioSampleRateHz);
            Add(update, "channels", NpgsqlDbType.Integer, facts.AudioChannels);
            Add(update, "layout", NpgsqlDbType.Text, facts.AudioChannelLayout);
            if (await update.ExecuteNonQueryAsync(token) != 1)
                throw new InvalidOperationException("Refresh metadata row changed; no audit action recorded.");
        }

        await using (NpgsqlCommand audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO video_metadata_refresh_actions
                    (asset_revision_id, inspected_at_utc, tool_sha256,
                     inspection_profile_id, changed_fields)
                VALUES (@revision, @inspected, @tool, @profile, @fields);
                """;
            audit.Parameters.AddWithValue("revision", revisionId.Value);
            audit.Parameters.AddWithValue("inspected", facts.InspectedAtUtc.ToUniversalTime());
            audit.Parameters.AddWithValue("tool", facts.ToolSha256);
            audit.Parameters.AddWithValue("profile", facts.InspectionProfileId);
            audit.Parameters.AddWithValue("fields", string.Join(',', changed));
            if (await audit.ExecuteNonQueryAsync(token) != 1)
                throw new InvalidOperationException("Could not record metadata refresh provenance.");
        }
        await transaction.CommitAsync(token);
        return changed.Count;
    }

    private static bool InvalidCamera(string? value) =>
        value is { } text && (text.Length is < 1 or > 128 || text.Any(char.IsControl));

    private static void Add(NpgsqlCommand command, string key, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(key, type) { Value = value ?? DBNull.Value });
}
