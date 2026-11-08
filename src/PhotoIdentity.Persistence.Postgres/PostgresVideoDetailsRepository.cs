using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>Manual video presence never constitutes a face observation.</summary>
public sealed record VideoPersonPresence(
    string Id, string DisplayName, string Actor, DateTimeOffset AssignedAtUtc);

public sealed record VideoPlacePresence(
    string Value, string Source, string Actor, DateTimeOffset AssignedAtUtc);

public sealed record VideoDetailsRecord(
    string RevisionId,
    string FileName,
    string? InspectionStatus,
    DateTimeOffset? ExtractedCaptureAtUtc,
    string? CaptureTimeProvenance,
    DateTimeOffset? EffectiveCaptureAtUtc,
    string CaptureSource,
    string? CameraMake,
    string? CameraModel,
    double? Latitude,
    double? Longitude,
    int? Width,
    int? Height,
    int? RotationDegrees,
    decimal? DurationSeconds,
    string? AverageFrameRate,
    VideoPlacePresence? Place,
    bool ManualPlaceOverride,
    IReadOnlyList<VideoPersonPresence> People);

/// <summary>
/// Video-only revision-scoped metadata and manual annotation boundary. Reads
/// PostgreSQL records, never the original, proxy, poster or OneDrive state.
/// Each write locks the source row, checks current revision and exclusions,
/// and commits an append-only history action.
/// </summary>
public sealed class PostgresVideoDetailsRepository(
    PostgresCatalogueDatabase database, TimeProvider timeProvider)
{
    // The query must not permit past revisions, excluded copies, deleted
    // assets or photo revisions to leak metadata or accept writes.
    private const string CurrentVideoPredicate = """
        revision.media_kind = 'video'
        AND asset.media_kind = 'video'
        AND asset.deleted_at_utc IS NULL
        AND revision.id = (
            SELECT latest.id
            FROM asset_revisions latest
            WHERE latest.asset_id = asset.id AND latest.media_kind = 'video'
            ORDER BY latest.observed_at_utc DESC, latest.id DESC
            LIMIT 1)
        AND NOT EXISTS (
            SELECT 1 FROM source_copy_exclusions exclusion
            WHERE exclusion.source_id = asset.source_id
              AND exclusion.source_key = asset.source_key)
        """;

    public async Task<VideoDetailsRecord?> GetAsync(
        AssetRevisionId revisionId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT asset.source_key, m.inspection_status, m.capture_at_utc,
                   m.capture_time_provenance, m.camera_make, m.camera_model,
                   m.gps_latitude, m.gps_longitude, m.coded_width, m.coded_height,
                   m.display_rotation_degrees, m.duration_seconds, m.average_frame_rate,
                   d.action_kind, d.captured_at_utc,
                   place.action_kind, place.source_kind, place.actor, place.created_at_utc,
                   tag.display_name
            FROM asset_revisions revision
            JOIN assets asset ON asset.id = revision.asset_id
            LEFT JOIN video_revision_metadata m ON m.asset_revision_id = revision.id
            LEFT JOIN LATERAL (
                SELECT action_kind, captured_at_utc
                FROM video_capture_date_actions
                WHERE asset_revision_id = revision.id
                ORDER BY id DESC LIMIT 1
            ) d ON TRUE
            LEFT JOIN LATERAL (
                SELECT action_kind, tag_id, source_kind, actor, created_at_utc
                FROM video_place_actions
                WHERE asset_revision_id = revision.id
                ORDER BY id DESC LIMIT 1
            ) place ON TRUE
            LEFT JOIN photo_tags tag ON tag.id = place.tag_id
            WHERE revision.id = @revision AND {CurrentVideoPredicate};
            """;
        command.Parameters.AddWithValue("revision", revisionId.Value);
        VideoDetailsRecord? details = null;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                string sourceKey = reader.GetString(0).Replace('\\', '/');
                string filename = sourceKey[(sourceKey.LastIndexOf('/') + 1)..];
                DateTimeOffset? extracted = reader.IsDBNull(2)
                    ? null : reader.GetFieldValue<DateTimeOffset>(2);
                string? action = reader.IsDBNull(13) ? null : reader.GetString(13);
                DateTimeOffset? manual = reader.IsDBNull(14)
                    ? null : reader.GetFieldValue<DateTimeOffset>(14);
                bool manualPlace = !reader.IsDBNull(16) && reader.GetString(16) == "manual";
                VideoPlacePresence? place = null;
                if (!reader.IsDBNull(15) && reader.GetString(15) == "set" &&
                    !reader.IsDBNull(19))
                {
                    PhotoPlacePath path = PhotoPlacePath.Parse(reader.GetString(19));
                    place = new VideoPlacePresence(
                        path.DisplayValue,
                        reader.GetString(16),
                        reader.GetString(17),
                        reader.GetFieldValue<DateTimeOffset>(18));
                }
                details = new VideoDetailsRecord(
                    revisionId.ToString(),
                    string.IsNullOrWhiteSpace(filename) ? "Unknown" : filename,
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    extracted,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    action == "set" ? manual : action == "clear" ? extracted : extracted,
                    action == "set" ? "manual" : action == "clear" ? "extracted (manual cleared)" :
                        extracted.HasValue ? "extracted" : "unknown",
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    place, manualPlace, []);
            }
        }
        if (details is null) return null;
        return details with { People = await ReadPeopleAsync(connection, revisionId, cancellationToken) };
    }

    public async Task SetCaptureDateAsync(
        AssetRevisionId revisionId, DateTimeOffset? value,
        string actor, CancellationToken cancellationToken = default)
    {
        if (value.HasValue && (value.Value.Year is < 1800 or > 2100))
            throw new ArgumentException("Recording date must be between years 1800 and 2100.");
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockCurrentVideoAsync(connection, transaction, revisionId, cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO video_capture_date_actions
                (asset_revision_id, action_kind, captured_at_utc, actor, created_at_utc)
            VALUES (@revision, @action, @value, @actor, @at);
            """;
        command.Parameters.AddWithValue("revision", revisionId.Value);
        command.Parameters.AddWithValue("action", value.HasValue ? "set" : "clear");
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.TimestampTz)
            { Value = value.HasValue ? value.Value.ToUniversalTime() : DBNull.Value });
        command.Parameters.AddWithValue("actor", Actor(actor));
        command.Parameters.AddWithValue("at", timeProvider.GetUtcNow().ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetPersonAsync(
        AssetRevisionId revisionId, Guid personId, bool present,
        string actor, CancellationToken cancellationToken = default)
    {
        if (personId == Guid.Empty) throw new ArgumentException("Invalid person identifier.");
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockCurrentVideoAsync(connection, transaction, revisionId, cancellationToken);
        await using NpgsqlCommand person = connection.CreateCommand();
        person.Transaction = transaction;
        person.CommandText = """
            SELECT merged_into_person_id, display_name
            FROM people WHERE id = @person FOR UPDATE;
            """;
        person.Parameters.AddWithValue("person", personId);
        await using (NpgsqlDataReader reader = await person.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("The selected person does not exist.");
            if (!reader.IsDBNull(0) || reader.IsDBNull(1) ||
                string.IsNullOrWhiteSpace(reader.GetString(1)))
                throw new InvalidOperationException("Select an active, unmerged person.");
        }

        // A merged person's old presence history is still visible under its
        // canonical target. Removing that target also removes active alias
        // evidence; it does not rewrite historical actions.
        await using NpgsqlCommand action = connection.CreateCommand();
        action.Transaction = transaction;
        action.CommandText = present ? """
            INSERT INTO video_person_actions
                (asset_revision_id, person_id, action_kind, actor, created_at_utc)
            SELECT @revision, @person, 'add', @actor, @at
            WHERE NOT EXISTS (
                SELECT 1 FROM video_person_actions existing
                WHERE existing.asset_revision_id = @revision
                  AND existing.person_id = @person
                  AND existing.id = (
                      SELECT MAX(id) FROM video_person_actions
                      WHERE asset_revision_id = @revision AND person_id = @person)
                  AND existing.action_kind = 'add');
            """ : """
            WITH RECURSIVE aliases(id) AS (
                SELECT id FROM people WHERE id = @person
                UNION
                SELECT p.id FROM people p JOIN aliases a ON p.merged_into_person_id = a.id
            ), active AS (
                SELECT aliases.id
                FROM aliases
                JOIN LATERAL (
                    SELECT action_kind FROM video_person_actions
                    WHERE asset_revision_id = @revision AND person_id = aliases.id
                    ORDER BY id DESC LIMIT 1
                ) latest ON latest.action_kind = 'add'
            )
            INSERT INTO video_person_actions
                (asset_revision_id, person_id, action_kind, actor, created_at_utc)
            SELECT @revision, active.id, 'remove', @actor, @at FROM active;
            """;
        action.Parameters.AddWithValue("revision", revisionId.Value);
        action.Parameters.AddWithValue("person", personId);
        action.Parameters.AddWithValue("actor", Actor(actor));
        action.Parameters.AddWithValue("at", timeProvider.GetUtcNow().ToUniversalTime());
        await action.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetPlaceAsync(
        AssetRevisionId revisionId, string? value, string source,
        string actor, string? provider = null, CancellationToken cancellationToken = default)
    {
        if (source is not ("manual" or "automatic"))
            throw new ArgumentException("Invalid Place source.", nameof(source));
        PhotoPlacePath? path = value is null ? null : PhotoPlacePath.Parse(value);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockCurrentVideoAsync(connection, transaction, revisionId, cancellationToken);
        await using NpgsqlCommand last = connection.CreateCommand();
        last.Transaction = transaction;
        last.CommandText = """
            SELECT action_kind, tag_id, source_kind
            FROM video_place_actions WHERE asset_revision_id = @revision
            ORDER BY id DESC LIMIT 1;
            """;
        last.Parameters.AddWithValue("revision", revisionId.Value);
        string? action = null, previousSource = null;
        long? previousTag = null;
        await using (NpgsqlDataReader r = await last.ExecuteReaderAsync(cancellationToken))
        {
            if (await r.ReadAsync(cancellationToken))
            {
                action = r.GetString(0);
                previousTag = r.IsDBNull(1) ? null : r.GetInt64(1);
                previousSource = r.GetString(2);
            }
        }
        // Manual selection/clear always wins over asynchronous provider
        // results, including a previous explicit clear.
        if (source == "automatic" && previousSource == "manual")
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        long? tagId = null;
        if (path is not null)
        {
            string normalized = "", display = "";
            foreach (PhotoTagName segment in path.CanonicalTagPath.Segments)
            {
                normalized = normalized.Length == 0 ? segment.NormalizedName :
                    normalized + "/" + segment.NormalizedName;
                display = display.Length == 0 ? segment.DisplayName :
                    display + "/" + segment.DisplayName;
                await using NpgsqlCommand tag = connection.CreateCommand();
                tag.Transaction = transaction;
                tag.CommandText = """
                    INSERT INTO photo_tags
                        (normalized_name, display_name, created_by, created_at_utc)
                    VALUES (@normalized, @display, @actor, @at)
                    ON CONFLICT (normalized_name) DO UPDATE
                        SET normalized_name = EXCLUDED.normalized_name
                    RETURNING id;
                    """;
                tag.Parameters.AddWithValue("normalized", normalized);
                tag.Parameters.AddWithValue("display", display);
                tag.Parameters.AddWithValue("actor", Actor(actor));
                tag.Parameters.AddWithValue("at", timeProvider.GetUtcNow().ToUniversalTime());
                tagId = (long)(await tag.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Place vocabulary insert failed."));
            }
        }
        string kind = path is null ? "clear" : "set";
        if (action == kind && tagId == previousTag && source == previousSource)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        await using NpgsqlCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO video_place_actions
                (asset_revision_id, tag_id, action_kind, source_kind,
                 provider, actor, created_at_utc)
            VALUES (@revision, @tag, @kind, @source, @provider, @actor, @at);
            """;
        insert.Parameters.AddWithValue("revision", revisionId.Value);
        insert.Parameters.Add(new NpgsqlParameter("tag", NpgsqlDbType.Bigint)
            { Value = tagId.HasValue ? tagId.Value : DBNull.Value });
        insert.Parameters.AddWithValue("kind", kind);
        insert.Parameters.AddWithValue("source", source);
        insert.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Text)
            { Value = provider is null ? DBNull.Value : provider });
        insert.Parameters.AddWithValue("actor", Actor(actor));
        insert.Parameters.AddWithValue("at", timeProvider.GetUtcNow().ToUniversalTime());
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<VideoPersonPresence>> ReadPeopleAsync(
        NpgsqlConnection connection, AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE latest AS (
                SELECT DISTINCT ON (person_id) person_id, action_kind, actor, created_at_utc
                FROM video_person_actions WHERE asset_revision_id = @revision
                ORDER BY person_id, id DESC
            ), chain AS (
                SELECT l.person_id AS original, p.id AS current, p.merged_into_person_id AS parent,
                       l.actor, l.created_at_utc
                FROM latest l JOIN people p ON p.id = l.person_id
                WHERE l.action_kind = 'add'
                UNION ALL
                SELECT chain.original, p.id, p.merged_into_person_id,
                       chain.actor, chain.created_at_utc
                FROM chain JOIN people p ON p.id = chain.parent
            )
            SELECT DISTINCT ON (p.id) p.id, p.display_name, chain.actor, chain.created_at_utc
            FROM chain JOIN people p ON p.id = chain.current
            WHERE chain.parent IS NULL AND p.display_name IS NOT NULL
              AND btrim(p.display_name) <> ''
            ORDER BY p.id, chain.created_at_utc DESC;
            """;
        command.Parameters.AddWithValue("revision", revisionId.Value);
        List<VideoPersonPresence> people = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            people.Add(new VideoPersonPresence(
                reader.GetGuid(0).ToString("D"), reader.GetString(1),
                reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return people.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task LockCurrentVideoAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        AssetRevisionId revisionId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = $"""
            SELECT source.id FROM asset_revisions revision
            JOIN assets asset ON asset.id = revision.asset_id
            JOIN sources source ON source.id = asset.source_id
            WHERE revision.id = @revision AND {CurrentVideoPredicate}
            FOR UPDATE OF source;
            """;
        check.Parameters.AddWithValue("revision", revisionId.Value);
        if (await check.ExecuteScalarAsync(cancellationToken) is not Guid)
            throw new KeyNotFoundException("The current included video revision was not found.");
    }

    private static string Actor(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string actor = value.Trim();
        if (actor.Length > 120)
            throw new ArgumentException("Actor is too long.", nameof(value));
        return actor;
    }
}
