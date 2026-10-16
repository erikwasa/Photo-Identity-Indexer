using System.Data;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Explicit video-only persistence boundary. No photo scanner, API route or background
/// worker instantiates this repository yet. Callers must verify the source bytes and
/// obtain a video revision before persisting technical inspection evidence.
/// </summary>
public sealed class PostgresVideoEvidenceRepository(PostgresCatalogueDatabase database)
{
    private readonly PostgresSourceCopyExclusionRepository _exclusions = new(database);

    public async Task SaveInspectionAsync(
        VideoRevisionInspection inspection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ValidateInspection(inspection);
        await _exclusions.EnsureReadyAsync(cancellationToken);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RequireEligibleAsync(connection, transaction, inspection.RevisionId, cancellationToken);

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO video_revision_metadata (
                    asset_revision_id, inspection_status, unsupported_reason_code,
                    container_name, video_codec, video_profile, audio_codec,
                    pixel_format, color_transfer, coded_width, coded_height,
                    display_rotation_degrees, duration_seconds, average_frame_rate,
                    capture_at_utc, capture_time_provenance, camera_make, camera_model,
                    gps_latitude, gps_longitude, inspected_at_utc, tool_sha256,
                    inspection_profile_id)
                VALUES (
                    @revision, @status, @reason,
                    @container, @codec, @profile, @audio,
                    @pixel, @transfer, @width, @height,
                    @rotation, @duration, @frame_rate,
                    @capture, @provenance, @make, @model,
                    @latitude, @longitude, @inspected, @tool, @inspection_profile)
                ON CONFLICT (asset_revision_id) DO NOTHING;
                """;
            Add(command, "revision", NpgsqlDbType.Uuid, inspection.RevisionId.Value);
            Add(command, "status", NpgsqlDbType.Text, inspection.InspectionStatus);
            Add(command, "reason", NpgsqlDbType.Text, inspection.UnsupportedReasonCode);
            Add(command, "container", NpgsqlDbType.Text, inspection.ContainerName);
            Add(command, "codec", NpgsqlDbType.Text, inspection.VideoCodec);
            Add(command, "profile", NpgsqlDbType.Text, inspection.VideoProfile);
            Add(command, "audio", NpgsqlDbType.Text, inspection.AudioCodec);
            Add(command, "pixel", NpgsqlDbType.Text, inspection.PixelFormat);
            Add(command, "transfer", NpgsqlDbType.Text, inspection.ColorTransfer);
            Add(command, "width", NpgsqlDbType.Integer, inspection.CodedWidth);
            Add(command, "height", NpgsqlDbType.Integer, inspection.CodedHeight);
            Add(command, "rotation", NpgsqlDbType.Integer, inspection.DisplayRotationDegrees);
            Add(command, "duration", NpgsqlDbType.Numeric, inspection.DurationSeconds);
            Add(command, "frame_rate", NpgsqlDbType.Text, inspection.AverageFrameRate);
            Add(command, "capture", NpgsqlDbType.TimestampTz, inspection.CaptureAtUtc?.ToUniversalTime());
            Add(command, "provenance", NpgsqlDbType.Text, inspection.CaptureTimeProvenance);
            Add(command, "make", NpgsqlDbType.Text, inspection.CameraMake);
            Add(command, "model", NpgsqlDbType.Text, inspection.CameraModel);
            Add(command, "latitude", NpgsqlDbType.Double, inspection.GpsLatitude);
            Add(command, "longitude", NpgsqlDbType.Double, inspection.GpsLongitude);
            Add(command, "inspected", NpgsqlDbType.TimestampTz, inspection.InspectedAtUtc.ToUniversalTime());
            Add(command, "tool", NpgsqlDbType.Text, inspection.ToolSha256);
            Add(command, "inspection_profile", NpgsqlDbType.Text, inspection.InspectionProfileId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        VideoRevisionInspection? stored = await ReadInspectionAsync(
            connection, transaction, inspection.RevisionId, cancellationToken);
        // A second observation of the same immutable revision may have a later inspection
        // time. Changes to content/profile/tool/metadata need explicit invalidation policy.
        if (stored is null || (stored with { InspectedAtUtc = inspection.InspectedAtUtc }) != inspection)
            throw new InvalidOperationException(
                "Video inspection evidence differs for an existing revision; explicit invalidation is required.");

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<VideoRevisionInspection?> GetInspectionAsync(
        AssetRevisionId revisionId, CancellationToken cancellationToken = default)
    {
        await _exclusions.EnsureReadyAsync(cancellationToken);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await ReadInspectionAsync(connection, null, revisionId, cancellationToken);
    }

    public async Task SavePosterAsync(
        VideoStillPoster poster, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poster);
        ValidatePoster(poster);
        await _exclusions.EnsureReadyAsync(cancellationToken);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RequireEligibleAsync(connection, transaction, poster.RevisionId, cancellationToken);

        // Unsupported/HDR-deferred evidence must not acquire an unverified SDR poster.
        await using (NpgsqlCommand check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT inspection_status
                FROM video_revision_metadata WHERE asset_revision_id = @revision;
                """;
            Add(check, "revision", NpgsqlDbType.Uuid, poster.RevisionId.Value);
            if ((string?)await check.ExecuteScalarAsync(cancellationToken) != "supported")
                throw new InvalidOperationException("A supported video inspection is required before saving a poster.");
        }

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO video_still_posters (
                    asset_revision_id, poster_profile_id, tool_sha256,
                    poster_sha256, poster_relative_path, encoded_bytes,
                    width, height, generated_at_utc)
                VALUES (
                    @revision, @profile, @tool,
                    @hash, @path, @bytes, @width, @height, @generated)
                ON CONFLICT (asset_revision_id, poster_profile_id, tool_sha256) DO NOTHING;
                """;
            Add(command, "revision", NpgsqlDbType.Uuid, poster.RevisionId.Value);
            Add(command, "profile", NpgsqlDbType.Text, poster.PosterProfileId);
            Add(command, "tool", NpgsqlDbType.Text, poster.ToolSha256);
            Add(command, "hash", NpgsqlDbType.Text, poster.PosterSha256);
            Add(command, "path", NpgsqlDbType.Text, poster.PosterRelativePath);
            Add(command, "bytes", NpgsqlDbType.Bigint, poster.EncodedBytes);
            Add(command, "width", NpgsqlDbType.Integer, poster.Width);
            Add(command, "height", NpgsqlDbType.Integer, poster.Height);
            Add(command, "generated", NpgsqlDbType.TimestampTz, poster.GeneratedAtUtc.ToUniversalTime());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        VideoStillPoster? stored = await ReadPosterAsync(
            connection, transaction, poster.RevisionId, poster.PosterProfileId,
            poster.ToolSha256, cancellationToken);
        if (stored is null || (stored with { GeneratedAtUtc = poster.GeneratedAtUtc }) != poster)
            throw new InvalidOperationException(
                "Video poster evidence differs for an existing revision/profile/tool; explicit invalidation is required.");

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<VideoStillPoster?> GetPosterAsync(
        AssetRevisionId revisionId, string posterProfileId, string toolSha256,
        CancellationToken cancellationToken = default)
    {
        await _exclusions.EnsureReadyAsync(cancellationToken);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await ReadPosterAsync(connection, null, revisionId, posterProfileId, toolSha256, cancellationToken);
    }

    private static async Task RequireEligibleAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        AssetRevisionId revisionId, CancellationToken cancellationToken)
    {
        // Lock the source row: concurrent exclusion inserts need a referenced-source
        // key-share lock. Check the tombstone AFTER acquiring the lock to avoid
        // persisting evidence past an exclusion already in flight.
        await using (NpgsqlCommand lockSource = connection.CreateCommand())
        {
            lockSource.Transaction = transaction;
            lockSource.CommandText = """
                SELECT revision.media_kind, asset.media_kind, asset.deleted_at_utc
                FROM asset_revisions revision
                JOIN assets asset ON asset.id = revision.asset_id
                JOIN sources source ON source.id = asset.source_id
                WHERE revision.id = @revision
                FOR UPDATE OF source;
                """;
            Add(lockSource, "revision", NpgsqlDbType.Uuid, revisionId.Value);
            await using NpgsqlDataReader reader = await lockSource.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                reader.GetString(0) != "video" || reader.GetString(1) != "video" || !reader.IsDBNull(2))
                throw new InvalidOperationException("Video revision is missing, deleted or not marked video.");
        }

        await using NpgsqlCommand exclusion = connection.CreateCommand();
        exclusion.Transaction = transaction;
        exclusion.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM asset_revisions revision
                JOIN assets asset ON asset.id = revision.asset_id
                JOIN source_copy_exclusions excluded
                  ON excluded.source_id = asset.source_id AND excluded.source_key = asset.source_key
                WHERE revision.id = @revision);
            """;
        Add(exclusion, "revision", NpgsqlDbType.Uuid, revisionId.Value);
        if (await exclusion.ExecuteScalarAsync(cancellationToken) is true)
            throw new InvalidOperationException("Source copy is excluded; video evidence must not be persisted.");
    }

    private static async Task<VideoRevisionInspection?> ReadInspectionAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        AssetRevisionId revisionId, CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT m.inspection_status, m.unsupported_reason_code, m.container_name,
                   m.video_codec, m.video_profile, m.audio_codec, m.pixel_format,
                   m.color_transfer, m.coded_width, m.coded_height, m.display_rotation_degrees,
                   m.duration_seconds, m.average_frame_rate, m.capture_at_utc,
                   m.capture_time_provenance, m.camera_make, m.camera_model,
                   m.gps_latitude, m.gps_longitude, m.inspected_at_utc,
                   m.tool_sha256, m.inspection_profile_id
            FROM video_revision_metadata m
            JOIN asset_revisions revision ON revision.id = m.asset_revision_id
            JOIN assets asset ON asset.id = revision.asset_id
            WHERE m.asset_revision_id = @revision
              AND asset.media_kind = 'video' AND revision.media_kind = 'video'
              AND asset.deleted_at_utc IS NULL
              AND NOT EXISTS (SELECT 1 FROM source_copy_exclusions x
                              WHERE x.source_id = asset.source_id AND x.source_key = asset.source_key);
            """;
        Add(command, "revision", NpgsqlDbType.Uuid, revisionId.Value);
        await using NpgsqlDataReader r = await command.ExecuteReaderAsync(token);
        if (!await r.ReadAsync(token)) return null;
        return new VideoRevisionInspection(
            revisionId, r.GetString(0), Text(r, 1), Text(r, 2), Text(r, 3),
            Text(r, 4), Text(r, 5), Text(r, 6), Text(r, 7),
            NullableInt(r, 8), NullableInt(r, 9), NullableInt(r, 10),
            r.IsDBNull(11) ? null : r.GetDecimal(11), Text(r, 12),
            r.IsDBNull(13) ? null : r.GetFieldValue<DateTimeOffset>(13),
            Text(r, 14), Text(r, 15), Text(r, 16),
            r.IsDBNull(17) ? null : r.GetDouble(17),
            r.IsDBNull(18) ? null : r.GetDouble(18),
            r.GetFieldValue<DateTimeOffset>(19), r.GetString(20), r.GetString(21));
    }

    private static async Task<VideoStillPoster?> ReadPosterAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        AssetRevisionId revisionId, string profile, string tool, CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.poster_sha256, p.poster_relative_path, p.encoded_bytes,
                   p.width, p.height, p.generated_at_utc
            FROM video_still_posters p
            JOIN asset_revisions revision ON revision.id = p.asset_revision_id
            JOIN assets asset ON asset.id = revision.asset_id
            WHERE p.asset_revision_id = @revision
              AND p.poster_profile_id = @profile AND p.tool_sha256 = @tool
              AND asset.media_kind = 'video' AND revision.media_kind = 'video'
              AND asset.deleted_at_utc IS NULL
              AND NOT EXISTS (SELECT 1 FROM source_copy_exclusions x
                              WHERE x.source_id = asset.source_id AND x.source_key = asset.source_key);
            """;
        Add(command, "revision", NpgsqlDbType.Uuid, revisionId.Value);
        Add(command, "profile", NpgsqlDbType.Text, profile);
        Add(command, "tool", NpgsqlDbType.Text, tool);
        await using NpgsqlDataReader r = await command.ExecuteReaderAsync(token);
        if (!await r.ReadAsync(token)) return null;
        return new VideoStillPoster(revisionId, profile, tool, r.GetString(0),
            r.GetString(1), r.GetInt64(2), r.GetInt32(3), r.GetInt32(4),
            r.GetFieldValue<DateTimeOffset>(5));
    }

    private static string? Text(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static int? NullableInt(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static void Add(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static void ValidateInspection(VideoRevisionInspection m)
    {
        if (m.RevisionId.IsEmpty || m.InspectionStatus is not ("supported" or "unsupported") ||
            string.IsNullOrWhiteSpace(m.InspectionProfileId) || !ValidHash(m.ToolSha256) ||
            (m.InspectionStatus == "supported" && m.UnsupportedReasonCode is not null) ||
            (m.CaptureAtUtc is null) != (m.CaptureTimeProvenance is null) ||
            (m.GpsLatitude is null) != (m.GpsLongitude is null) ||
            m.DurationSeconds is < 0 ||
            m.CodedWidth is <= 0 || m.CodedHeight is <= 0)
            throw new ArgumentException("Invalid revision-bound video inspection evidence.");
    }

    private static void ValidatePoster(VideoStillPoster p)
    {
        if (p.RevisionId.IsEmpty || string.IsNullOrWhiteSpace(p.PosterProfileId) ||
            !ValidHash(p.ToolSha256) || !ValidHash(p.PosterSha256) ||
            p.EncodedBytes <= 0 || p.Width <= 0 || p.Height <= 0 ||
            !p.PosterRelativePath.StartsWith("video-posters/", StringComparison.Ordinal) ||
            p.PosterRelativePath.Contains("..", StringComparison.Ordinal) ||
            p.PosterRelativePath.Contains(':') || p.PosterRelativePath.Contains('\\'))
            throw new ArgumentException("Invalid durable video poster evidence.");
    }

    private static bool ValidHash(string? value) =>
        value is { Length: 64 } &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
