using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Operator-selected, metadata-only WI-0190 audit. No source path, media,
/// caption text, vector bytes, local model, FFmpeg or cloud endpoint is read.
/// This command never calls InitializeAsync or changes PostgreSQL state.
/// </summary>
public sealed record VideoFrameEvidenceAuditOptions(
    string ConnectionEnv, string Database, Guid JobId)
{
    public static VideoFrameEvidenceAuditOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--database" or "--job-id") ||
                i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid or repeated frame audit option.");
        }

        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Frame audit requires explicit database and job inputs.");

        string env = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--job-id"), out Guid job) || job == Guid.Empty ||
            values.Count != 3)
            throw new ArgumentException("Frame audit requires a local database and valid job UUID.");
        return new(env, database, job);
    }
}

/// <summary>
/// Only scalar metadata from a single repeatable-read PostgreSQL snapshot.
/// These fields are deliberately not video source paths, captions or vectors.
/// </summary>
public sealed record VideoFrameEvidenceAuditSnapshot(
    string JobStatus, string DerivativeKind, bool BatchExists,
    int ExpectedFrames, int Dimensions, bool SourceEligible,
    bool MatchesJobRevision, bool LatestRevision, bool SupportedSdr,
    bool Excluded, decimal DurationSeconds, int FrameRows,
    long VectorBytes, long CaptionBytes, long FirstTimestampMs,
    long LastTimestampMs, int FirstOrdinal, int LastOrdinal,
    bool ValidVectorLengths, bool IncreasingTimestamps);

public static class VideoFrameEvidenceAuditPolicy
{
    public static string Classify(VideoFrameEvidenceAuditSnapshot? row)
    {
        if (row is null) return "job-not-found";
        if (row.DerivativeKind != "FrameEvidence") return "wrong-job-kind";
        // An exclusion may have already physically purged a once-valid batch.
        if (row.Excluded) return "source-excluded";
        if (!row.SourceEligible) return "source-unavailable";
        if (!row.LatestRevision) return "stale-revision";
        if (!row.SupportedSdr) return "unsupported-media";
        if (!row.BatchExists) return "batch-not-published";
        if (row.JobStatus != "succeeded") return "job-not-succeeded";
        if (!row.MatchesJobRevision || row.ExpectedFrames is < 1 or > 128 ||
            row.Dimensions is < 1 or > 2048 ||
            row.FrameRows != row.ExpectedFrames ||
            row.FirstOrdinal != 0 || row.LastOrdinal != row.ExpectedFrames - 1 ||
            !row.ValidVectorLengths || !row.IncreasingTimestamps ||
            row.VectorBytes != (long)row.FrameRows * row.Dimensions * sizeof(float) ||
            row.CaptionBytes < row.FrameRows ||
            row.DurationSeconds is < 0.2m or > 3600m ||
            row.FirstTimestampMs < 0 || row.LastTimestampMs < row.FirstTimestampMs ||
            row.LastTimestampMs >= row.DurationSeconds * 1000m)
            return "incomplete-or-invalid";
        return "current-complete";
    }
}

public static class VideoFrameEvidenceAuditCommandRunner
{
    public static async Task<int> RunAsync(
        VideoFrameEvidenceAuditOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string? connectionString = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A local PostgreSQL environment variable is required.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Frame audit requires loopback PostgreSQL.");
        builder.Database = options.Database;

        await using PostgresCatalogueDatabase db = new(builder.ConnectionString);
        await using NpgsqlConnection connection = await db.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, token);
        await using (NpgsqlCommand readOnly = connection.CreateCommand())
        {
            readOnly.Transaction = transaction;
            readOnly.CommandText = "SET TRANSACTION READ ONLY;";
            await readOnly.ExecuteNonQueryAsync(token);
        }

        await using (NpgsqlCommand schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText =
                "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Frame audit requires an already-rehearsed current PostgreSQL schema.");
        }

        // This query does NOT SELECT source_key, root_locator, any caption,
        // embedding_le_f32 or model string. The server calculates byte lengths
        // and frame ordering; only numeric/bool metadata crosses the connection.
        await using NpgsqlCommand query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT job.status, job.derivative_kind,
                   b.job_id IS NOT NULL,
                   COALESCE(b.frame_count,0),
                   COALESCE(b.embedding_dimensions,0),
                   COALESCE(source.kind='local-folder'
                       AND asset.deleted_at_utc IS NULL
                       AND asset.media_kind='video'
                       AND revision.media_kind='video',false),
                   COALESCE(b.asset_revision_id=job.asset_revision_id,false),
                   COALESCE(revision.id=(
                       SELECT latest.id FROM asset_revisions latest
                       WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                       ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1),false),
                   COALESCE(metadata.inspection_status='supported'
                       AND metadata.color_transfer='bt709',false),
                   EXISTS (
                       SELECT 1 FROM source_copy_exclusions e
                       WHERE e.source_id=asset.source_id AND e.source_key=asset.source_key),
                   COALESCE(metadata.duration_seconds,0),
                   f.frame_rows, f.vector_bytes, f.caption_bytes, f.first_ms, f.last_ms,
                   f.first_ordinal, f.last_ordinal, f.valid_vectors, f.increasing
            FROM video_processing_jobs job
            LEFT JOIN video_frame_evidence_batches b ON b.job_id=job.id
            LEFT JOIN asset_revisions revision ON revision.id=job.asset_revision_id
            LEFT JOIN assets asset ON asset.id=revision.asset_id
            LEFT JOIN sources source ON source.id=asset.source_id
            LEFT JOIN video_revision_metadata metadata
                   ON metadata.asset_revision_id=revision.id
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::integer AS frame_rows,
                       COALESCE(SUM(octet_length(frame.embedding_le_f32)),0)::bigint
                           AS vector_bytes,
                       COALESCE(SUM(octet_length(frame.caption)),0)::bigint
                           AS caption_bytes,
                       COALESCE(MIN(frame.timestamp_ms),0)::bigint AS first_ms,
                       COALESCE(MAX(frame.timestamp_ms),0)::bigint AS last_ms,
                       COALESCE(MIN(frame.ordinal),0)::integer AS first_ordinal,
                       COALESCE(MAX(frame.ordinal),0)::integer AS last_ordinal,
                       COALESCE(BOOL_AND(octet_length(frame.embedding_le_f32)
                           = b.embedding_dimensions*4),false) AS valid_vectors,
                       NOT EXISTS (
                           SELECT 1 FROM (
                               SELECT ordinal, timestamp_ms,
                                   LAG(timestamp_ms) OVER (ORDER BY ordinal) AS previous_ms
                               FROM video_frame_evidence_frames
                               WHERE job_id=b.job_id
                           ) ordered
                           WHERE ordered.ordinal>0
                             AND (ordered.previous_ms IS NULL
                                  OR ordered.timestamp_ms<=ordered.previous_ms)
                       ) AS increasing
                FROM video_frame_evidence_frames frame WHERE frame.job_id=b.job_id
            ) f ON true
            WHERE job.id=@job;
            """;
        query.Parameters.AddWithValue("job", options.JobId);
        VideoFrameEvidenceAuditSnapshot? result = null;
        await using (NpgsqlDataReader reader = await query.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
                result = new(
                    reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetBoolean(5),
                    reader.GetBoolean(6), reader.GetBoolean(7), reader.GetBoolean(8),
                    reader.GetBoolean(9), reader.GetDecimal(10), reader.GetInt32(11),
                    reader.GetInt64(12), reader.GetInt64(13), reader.GetInt64(14),
                    reader.GetInt64(15), reader.GetInt32(16), reader.GetInt32(17),
                    reader.GetBoolean(18), reader.GetBoolean(19));
        }
        await transaction.CommitAsync(token);

        string reason = VideoFrameEvidenceAuditPolicy.Classify(result);
        bool current = reason == "current-complete";
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-frame-evidence-audit-v1",
            readOnly = true, current, reasonCode = reason,
            expectedFrames = result?.ExpectedFrames ?? 0,
            durableFrames = result?.FrameRows ?? 0,
            embeddingDimensions = result?.Dimensions ?? 0,
            captionUtf8Bytes = result?.CaptionBytes ?? 0,
            embeddingBytes = result?.VectorBytes ?? 0,
            evidencePayloadBytes = (result?.CaptionBytes ?? 0) + (result?.VectorBytes ?? 0),
            firstTimestampMs = current ? result!.FirstTimestampMs : (long?)null,
            lastTimestampMs = current ? result!.LastTimestampMs : (long?)null,
            durationMs = current ? (long)(result!.DurationSeconds * 1000m) : (long?)null,
            originalsOpened = false, oneDriveHydrationRequested = false,
            captionsReturned = false, embeddingsReturned = false,
            videoJobsEnqueued = false, schemaChanged = false
        }));
        return current ? 0 : 1;
    }
}
