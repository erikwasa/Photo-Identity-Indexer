using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// WI-0210: metadata-only archive census. No media paths, source keys, revision
/// identifiers, captures, GPS, persons, camera labels or frame text are selected.
/// Availability is the last catalogue observation, NOT a filesystem probe.
/// </summary>
public sealed record VideoCompatibilitySurveyOptions(string ConnectionEnv, string Database)
{
    public static VideoCompatibilitySurveyOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--database") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Compatibility survey accepts only --postgres-connection-env and --database.");
        }
        if (!values.TryGetValue("--postgres-connection-env", out string? env) ||
            !Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !values.TryGetValue("--database", out string? database) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Specify a valid connection variable and explicit database name.");
        return new(env, database);
    }
}

public sealed record VideoCompatibilityObservation(
    bool Excluded, string? SourceKind, string? CachedAvailability,
    string? Inspection, string? Container, string? Codec, string? Pixel,
    string? Transfer, string? Audio, int? Width, int? Height, int? Rotation,
    decimal? DurationSeconds, long SourceBytes, bool PosterReceipt,
    bool ProxyReceipt, bool FrameReceipt);

public sealed class VideoCompatibilitySurvey
{
    private const long WorkerSourceLimit = 256L * 1024 * 1024;
    private readonly Dictionary<(string Dimension, string Bucket), long> _dimensions = new();
    private readonly Dictionary<(string Stage, string State), long> _stages = new();
    private readonly Dictionary<string, long> _workerGates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _overlappingGaps = new(StringComparer.Ordinal);
    // One metadata gate hypothetically changed; NOT permission to decode or publish.
    private readonly Dictionary<(string Candidate, string Availability), long> _singleGateProjections = new();
    public long ActiveVideoCount { get; private set; }
    public long ExcludedCurrentCount { get; private set; }

    private static string Known(string? value, params string[] choices) =>
        value is null ? "unknown" :
        choices.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? value.ToLowerInvariant() : "other";

    private static void Increment<TKey>(Dictionary<TKey, long> counts, TKey key)
        where TKey : notnull =>
        counts[key] = counts.TryGetValue(key, out long old) ? checked(old + 1) : 1;

    public static string WorkerGate(VideoCompatibilityObservation row)
    {
        if (row.SourceKind != "local-folder") return "source-kind";
        if (row.Inspection is null) return "no-inspection";
        if (row.Inspection != "supported") return "inspection-unsupported";
        if (row.Codec is not ("h264" or "hevc")) return "codec";
        if (row.Transfer != "bt709") return "transfer";
        if (row.Pixel is not ("yuv420p" or "yuv420p10le")) return "pixel-format";
        if (row.Audio is not (null or "aac" or "pcm_s16le" or "pcm_s24le"))
            return "audio-codec";
        if (row.Width is < 16 or > 4096 || row.Height is < 16 or > 4096 ||
            row.Width is null || row.Height is null) return "dimensions";
        if (row.Rotation is not (0 or 90 or -90 or 180 or -180)) return "rotation";
        if (row.DurationSeconds is null or < 0.2m or > 600m) return "duration";
        if (row.SourceBytes is < 1 or > WorkerSourceLimit) return "source-size";
        return "metadata-eligible";
    }

    public void Add(VideoCompatibilityObservation row)
    {
        if (row.Excluded) { ExcludedCurrentCount++; return; }
        ActiveVideoCount++;
        void Dimension(string dimension, string bucket) =>
            Increment(_dimensions, (dimension, bucket));
        void Stage(string stage, string state) =>
            Increment(_stages, (stage, state));
        void Gap(string reason)
        {
            Increment(_overlappingGaps, reason);
        }

        Dimension("source-kind", Known(row.SourceKind, "local-folder"));
        Dimension("last-recorded-availability", Known(row.CachedAvailability,
            "local", "online-only", "downloading", "unavailable", "error"));
        Dimension("inspection", Known(row.Inspection, "supported", "unsupported"));
        Dimension("container", Known(row.Container, "mov,mp4,m4a,3gp,3g2,mj2", "mov", "mp4"));
        Dimension("video-codec", Known(row.Codec, "h264", "hevc", "av1", "vp9", "mpeg4"));
        Dimension("transfer", Known(row.Transfer, "bt709", "arib-std-b67", "smpte2084"));
        Dimension("pixel-format", Known(row.Pixel, "yuv420p", "yuv420p10le", "yuvj420p", "p010le"));
        Dimension("audio-codec", Known(row.Audio, "aac", "pcm_s16le", "pcm_s24le", "mp3", "opus"));
        Dimension("rotation", row.Rotation switch
        {
            0 => "0", 90 or -90 => "90", 180 or -180 => "180",
            null => "unknown", _ => "other"
        });
        Dimension("coded-resolution", row.Width is null || row.Height is null ? "unknown" :
            Math.Max(row.Width.Value, row.Height.Value) <= 1280 ? "up-to-720p" :
            Math.Max(row.Width.Value, row.Height.Value) <= 1920 ? "up-to-1080p" :
            Math.Max(row.Width.Value, row.Height.Value) <= 4096 ? "up-to-4k" : "above-4k");
        Dimension("duration", row.DurationSeconds is null ? "unknown" :
            row.DurationSeconds < 0.2m ? "below-0.2s" :
            row.DurationSeconds <= 600m ? "up-to-600s" : "above-600s");
        Dimension("source-size", row.SourceBytes <= 0 ? "unknown" :
            row.SourceBytes <= WorkerSourceLimit ? "up-to-256mib" :
            row.SourceBytes <= 1024L * 1024 * 1024 ? "256mib-to-1gib" : "above-1gib");

        Stage("poster-receipt", row.PosterReceipt ? "registered" : "missing");
        Stage("playable-proxy-receipt", row.ProxyReceipt ? "registered" : "missing");
        Stage("frame-evidence-receipt", row.FrameReceipt ? "registered" : "missing");

        string gate = WorkerGate(row);
        Increment(_workerGates, gate);

        // Counterfactual single-gate checks keep ALL other worker rules
        // unchanged. In particular they do not certify source availability,
        // safe colour conversion, correct orientation or decodability.
        void Project(string candidate) => Increment(_singleGateProjections,
            (candidate, Known(row.CachedAvailability,
                "local", "online-only", "downloading", "unavailable", "error")));
        if (row.Pixel == "yuvj420p" &&
            WorkerGate(row with { Pixel = "yuv420p" }) == "metadata-eligible")
            Project("rec709-yuvj420p-pixel-only");
        if (row.Transfer == "arib-std-b67" &&
            WorkerGate(row with { Transfer = "bt709" }) == "metadata-eligible")
            Project("hlg-transfer-only");
        if (row.Transfer is null &&
            WorkerGate(row with { Transfer = "bt709" }) == "metadata-eligible")
            Project("unknown-transfer-only");
        if (row.Rotation is null &&
            WorkerGate(row with { Rotation = 0 }) == "metadata-eligible")
            Project("missing-rotation-only");

        Stage("worker-metadata-admission", gate == "metadata-eligible"
            ? "candidate-only" : "blocked");

        // Counts of candidate gaps overlap: e.g. a >256 MiB HLG video counts
        // in both classes. Never treat these as independently unlockable totals.
        if (row.Inspection is null) Gap("needs-inspection");
        if (row.Inspection == "unsupported") Gap("unsupported-inspection");
        if (row.Inspection == "supported")
        {
            if (row.Transfer != "bt709") Gap("non-bt709-or-unknown-transfer");
            if (row.Codec is not ("h264" or "hevc")) Gap("other-video-codec");
            if (row.Pixel is not ("yuv420p" or "yuv420p10le")) Gap("other-pixel-format");
            if (row.Audio is not (null or "aac" or "pcm_s16le" or "pcm_s24le")) Gap("other-audio-codec");
            if (row.Rotation is not (0 or 90 or -90 or 180 or -180)) Gap("unknown-or-other-rotation");
            if (row.DurationSeconds is > 600m) Gap("duration-over-600s");
            if (row.Width is > 4096 || row.Height is > 4096) Gap("coded-dimension-over-4096");
            if (row.SourceBytes > WorkerSourceLimit) Gap("source-over-256mib");
        }
        Stage("poster-metadata", row.Inspection != "supported" ? "no-supported-inspection" :
            row.Transfer != "bt709" ? "transfer-not-approved" : "candidate-only");
        Stage("frame-precondition", row.ProxyReceipt ? "proxy-receipt-present" : "no-proxy-receipt");
    }

    public void Write(TextWriter output)
    {
        var dimensions = _dimensions.OrderBy(x => x.Key.Dimension, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Bucket, StringComparer.Ordinal)
            .Select(x => new { dimension = x.Key.Dimension, bucket = x.Key.Bucket, count = x.Value });
        var stages = _stages.OrderBy(x => x.Key.Stage, StringComparer.Ordinal)
            .ThenBy(x => x.Key.State, StringComparer.Ordinal)
            .Select(x => new { stage = x.Key.Stage, state = x.Key.State, count = x.Value });
        var gates = _workerGates.OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new { reason = x.Key, count = x.Value });
        var gaps = _overlappingGaps.OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new { reason = x.Key, affected = x.Value });
        var singleGateProjections = _singleGateProjections
            .OrderBy(x => x.Key.Candidate, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Availability, StringComparer.Ordinal)
            .Select(x => new
            {
                candidate = x.Key.Candidate,
                recordedAvailability = x.Key.Availability,
                count = x.Value
            });
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0210-compatibility-inventory-v1",
            readOnly = true, schemaChanged = false, originalBytesOpened = false,
            filesystemAvailabilityChecked = false, oneDriveHydrationRequested = false,
            jobsEnqueued = false, outputsAreAggregated = true,
            availabilityIsLastRecordedNotLive = true,
            currentNonExcludedVideoCount = ActiveVideoCount,
            excludedCurrentVideoCount = ExcludedCurrentCount,
            dimensions, stages, workerPrimaryGates = gates, overlappingCandidateGaps = gaps,
            singleGateProjections,
            // This report does not choose an expansion or validate a derivative.
            selectionApproved = false, mediaDecodingPerformed = false
        }));
    }
}

public static class VideoCompatibilitySurveyCommandRunner
{
    public static async Task<int> RunAsync(VideoCompatibilitySurveyOptions options,
        TextWriter output, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("The local PostgreSQL connection variable is not set.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new ArgumentException("The compatibility survey requires loopback PostgreSQL.");
        builder.Database = options.Database;

        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, token);
        await using (NpgsqlCommand guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = "SET TRANSACTION READ ONLY;";
            await guard.ExecuteNonQueryAsync(token);
        }
        await using (NpgsqlCommand schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText =
                "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Compatibility inventory requires current PostgreSQL schema; it never migrates.");
        }

        // The SQL projection deliberately omits asset IDs, keys, paths, names,
        // captions, identity data, dates and coordinates. Latest revisions only.
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 60;
        command.CommandText = """
            SELECT
                EXISTS(SELECT 1 FROM source_copy_exclusions e
                    WHERE e.source_id=a.source_id AND e.source_key=a.source_key),
                s.kind, availability.availability, m.inspection_status,
                m.container_name, m.video_codec, m.pixel_format, m.color_transfer,
                m.audio_codec, m.coded_width, m.coded_height,
                m.display_rotation_degrees, m.duration_seconds, r.size_bytes,
                EXISTS(SELECT 1 FROM video_still_posters p
                    WHERE p.asset_revision_id=r.id
                      AND p.poster_profile_id='wi0151-sdr-still-v1'),
                EXISTS(SELECT 1 FROM video_published_derivatives p
                    JOIN video_processing_jobs j ON j.id=p.job_id
                    WHERE p.asset_revision_id=r.id
                      AND j.asset_revision_id=r.id AND j.status='succeeded'
                      AND j.derivative_kind='PlayableProxy'
                      AND j.policy_version='wi0188-v1'
                      AND j.preset_version IN (
                          'wi0188-sdr-h264-aac-720p-pilot-v1',
                          'wi0189-sdr-h264-aac-540p-compact-v1',
                           'wi0210-rec709-fullrange-720p-production-v1')),
                EXISTS(SELECT 1 FROM video_frame_evidence_batches b
                    JOIN video_processing_jobs j ON j.id=b.job_id
                    WHERE b.asset_revision_id=r.id
                      AND j.asset_revision_id=r.id AND j.status='succeeded'
                      AND j.derivative_kind='FrameEvidence')
            FROM assets a
            JOIN sources s ON s.id=a.source_id
            JOIN LATERAL (
                SELECT id,size_bytes FROM asset_revisions
                WHERE asset_id=a.id AND media_kind='video'
                ORDER BY observed_at_utc DESC,id DESC LIMIT 1
            ) r ON true
            LEFT JOIN video_revision_metadata m ON m.asset_revision_id=r.id
            LEFT JOIN archive_asset_availability availability ON availability.asset_id=a.id
            WHERE a.media_kind='video' AND a.deleted_at_utc IS NULL;
            """;

        VideoCompatibilitySurvey survey = new();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess, token))
        {
            while (await reader.ReadAsync(token))
            {
                string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                int? Number(int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
                survey.Add(new VideoCompatibilityObservation(
                    reader.GetBoolean(0), Text(1), Text(2), Text(3), Text(4),
                    Text(5), Text(6), Text(7), Text(8), Number(9), Number(10),
                    Number(11), reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                    reader.GetInt64(13), reader.GetBoolean(14),
                    reader.GetBoolean(15), reader.GetBoolean(16)));
            }
        }
        await transaction.CommitAsync(token);
        survey.Write(output);
        return 0;
    }
}
