using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Read-only, metadata-only shortlist for manually choosing a candidate
/// revision UUID. This is not an enqueue, a file availability check or a
/// permission to run a real-media one-shot. The exact UUID must still pass
/// the separate dry-run enqueue boundary before any later authorization.
/// </summary>
public sealed record VideoOneShotCandidatesOptions(
    string ConnectionEnv, string DatabaseName, int Limit)
{
    public static VideoOneShotCandidatesOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--database" or "--limit") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException(
                    "Video candidate discovery accepts only explicit database and bounded limit arguments.");
        }

        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("Video candidate discovery needs an explicit database connection.");

        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid local PostgreSQL database or connection variable.");

        int limit = 12;
        if (values.TryGetValue("--limit", out string? limitText) &&
            (!int.TryParse(limitText, out limit) || limit is < 1 or > 20))
            throw new ArgumentException("Video candidate limit must be in the range 1..20.");
        return new(env, db, limit);
    }
}

public sealed record VideoOneShotCandidate(
    Guid RevisionId, long SourceBytes, decimal DurationSeconds,
    string Orientation, bool HasAudio);

public static class VideoOneShotCandidatesCommandRunner
{
    private const long MaximumSourceBytes = 256L * 1024 * 1024;

    public static async Task<int> RunAsync(
        VideoOneShotCandidatesOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Selected video catalogue inspection requires Windows.");
        if (options.Limit is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(options.Limit));

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Video candidate discovery requires a configured local database.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new ArgumentException("Video candidate discovery requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;

        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        // No Schema.Initialize call; only an explicitly already-migrated v37
        // database is queried. A mismatched catalogue is never upgraded.
        await using (NpgsqlCommand version = connection.CreateCommand())
        {
            version.CommandText = """
                SELECT COALESCE(MAX(version),0)
                FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Video candidate discovery requires schema v37; no migrations are attempted.");
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id, revision.size_bytes,
                   metadata.duration_seconds,
                   metadata.coded_width, metadata.coded_height,
                   metadata.audio_codec
            FROM asset_revisions revision
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN sources source ON source.id=asset.source_id
            JOIN video_revision_metadata metadata
              ON metadata.asset_revision_id=revision.id
            WHERE revision.media_kind='video' AND asset.media_kind='video'
              AND asset.deleted_at_utc IS NULL
              AND source.kind='local-folder'
              AND metadata.inspection_status='supported'
              AND metadata.color_transfer='bt709'
              AND metadata.video_codec IN ('h264','hevc')
              AND metadata.pixel_format IN ('yuv420p','yuv420p10le')
              AND (metadata.audio_codec IS NULL OR
                   metadata.audio_codec IN ('aac','pcm_s16le','pcm_s24le'))
              AND metadata.coded_width BETWEEN 16 AND 4096
              AND metadata.coded_height BETWEEN 16 AND 4096
              AND metadata.duration_seconds BETWEEN 0.2 AND 600
              AND metadata.display_rotation_degrees IN (0,90,-90,180,-180)
              AND revision.size_bytes BETWEEN 1 AND @maxSourceBytes
              AND revision.id=(
                  SELECT latest.id FROM asset_revisions latest
                  WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                  ORDER BY latest.observed_at_utc DESC,latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key)
            ORDER BY metadata.duration_seconds ASC, revision.id ASC
            LIMIT @take;
            """;
        command.Parameters.AddWithValue("maxSourceBytes", MaximumSourceBytes);
        command.Parameters.AddWithValue("take", options.Limit + 1);

        List<VideoOneShotCandidate> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            int width = reader.GetInt32(3);
            int height = reader.GetInt32(4);
            // Display rotation 90/-90 changes the presentation axes;
            // avoid claiming a raw coded-width orientation is normalized.
            rows.Add(new VideoOneShotCandidate(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetDecimal(2),
                width > height ? "coded-landscape" :
                    height > width ? "coded-portrait" : "coded-square",
                !reader.IsDBNull(5)));
        }
        Write(output, rows.Take(options.Limit).ToArray(),
            rows.Count > options.Limit);
        return 0;
    }

    public static void Write(
        TextWriter output, IReadOnlyList<VideoOneShotCandidate> items, bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(items);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-one-shot-candidates-v1",
            dryRun = true,
            schemaUsed = true,
            originalsOpened = false,
            oneDriveHydrationRequested = false,
            videoWorkersActivated = false,
            jobsEnqueued = false,
            localAvailabilityChecked = false,
            // Only opaque revision UUID and coarse numeric stream attributes.
            // No source key, source path, filename, location, person, tags,
            // capture time or EXIF/GPS values are queried or serialized.
            candidates = items,
            hasMore
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
