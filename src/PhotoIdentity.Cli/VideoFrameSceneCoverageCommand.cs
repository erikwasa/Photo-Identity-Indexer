using System.Data;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Human-annotated numeric scene cuts are supplied explicitly by the operator.
/// No scene detection, visual media read or scene metadata is inferred from SQL.
/// </summary>
public sealed record VideoFrameSceneCoverageOptions(
    VideoFrameEvidenceAuditOptions Audit, IReadOnlyList<long> SceneCutsMs)
{
    public static VideoFrameSceneCoverageOptions Parse(string[] args)
    {
        List<string> audit = [];
        string? cuts = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Incomplete scene coverage option.");
            string key = args[i++], value = args[i];
            if (key == "--scene-boundaries-ms" && cuts is null)
                cuts = value;
            else if (key is "--postgres-connection-env" or "--database" or "--job-id")
            {
                audit.Add(key);
                audit.Add(value);
            }
            else
                throw new ArgumentException("Unexpected or repeated scene coverage option.");
        }
        if (cuts is null || cuts.Length > 1200)
            throw new ArgumentException("Explicit bounded scene cuts are required.");
        long[] boundaries;
        if (cuts == "none")
            boundaries = [];
        else
        {
            string[] parts = cuts.Split(',');
            if (parts.Length is < 1 or > VideoFrameSamplingPlanner.HardMaximumSceneBoundaries)
                throw new ArgumentException("Too many operator-annotated scene cuts.");
            boundaries = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!long.TryParse(parts[i], NumberStyles.None,
                        CultureInfo.InvariantCulture, out boundaries[i]) ||
                    boundaries[i] <= 0 ||
                    (i > 0 && boundaries[i] <= boundaries[i - 1]))
                    throw new ArgumentException(
                        "Scene cuts must be positive, strictly increasing integer milliseconds.");
        }
        return new(VideoFrameEvidenceAuditOptions.Parse(audit.ToArray()), boundaries);
    }
}

public sealed record VideoFrameSceneCoverageSummary(
    int SampledFrames, int TotalScenes, int CoveredScenes, int MissedScenes,
    double CoveragePercent, int MaximumFramesInOneScene,
    IReadOnlyList<int> MissedSceneIndices);

/// <summary>
/// Pure quantitative coverage of supplied sampled timestamps relative to
/// privately annotated scene boundaries. Cannot detect missed scenes that
/// the operator did not annotate, motion, speech or caption inaccuracies.
/// </summary>
public static class VideoFrameSceneCoveragePolicy
{
    public const string Version = "wi0190-manual-scene-coverage-v1";

    public static VideoFrameSceneCoverageSummary Assess(
        long durationMs, IReadOnlyList<long> timestamps, IReadOnlyList<long> cutsMs)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(cutsMs);
        if (durationMs is < 200 or > 3_600_000 ||
            timestamps.Count is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
            cutsMs.Count > VideoFrameSamplingPlanner.HardMaximumSceneBoundaries)
            throw new ArgumentOutOfRangeException(nameof(durationMs),
                "Scene coverage limits exceeded.");

        long prior = 0;
        for (int i = 0; i < cutsMs.Count; i++)
        {
            long cut = cutsMs[i];
            if (cut <= prior || cut >= durationMs)
                throw new ArgumentException(
                    "Annotated cuts must be strictly inside the video duration.");
            prior = cut;
        }

        int[] counts = new int[cutsMs.Count + 1];
        long previousFrame = -1;
        int scene = 0;
        foreach (long ms in timestamps)
        {
            if (ms <= previousFrame || ms < 0 || ms >= durationMs)
                throw new ArgumentException(
                    "Frame timestamps must be increasing within the duration.");
            while (scene < cutsMs.Count && ms >= cutsMs[scene])
                scene++;
            counts[scene]++;
            previousFrame = ms;
        }
        int covered = counts.Count(x => x > 0);
        int[] missing = Enumerable.Range(0, counts.Length)
            .Where(i => counts[i] == 0).ToArray();
        return new(timestamps.Count, counts.Length, covered,
            missing.Length, Math.Round(100.0 * covered / counts.Length, 1),
            counts.Max(), missing);
    }
}

/// <summary>
/// Metadata-only read-only PG snapshot. Reuses the audited full-batch gate
/// before querying ordinals and frame timestamps in the SAME transaction.
/// No caption text, vectors, source paths or model data cross the connection.
/// </summary>
public static class VideoFrameSceneCoverageCommandRunner
{
    public static async Task<int> RunAsync(
        VideoFrameSceneCoverageOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string? raw = Environment.GetEnvironmentVariable(options.Audit.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Local PostgreSQL connection environment is missing.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Scene coverage requires loopback PostgreSQL.");
        builder.Database = options.Audit.Database;

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
                    "Scene coverage requires the already-rehearsed current schema.");
        }

        VideoFrameEvidenceAuditSnapshot? state =
            await VideoFrameEvidenceAuditCommandRunner.ReadSnapshotAsync(
                connection, transaction, options.Audit.JobId, token);
        string reason = VideoFrameEvidenceAuditPolicy.Classify(state);
        if (reason != "current-complete" ||
            state!.ExpectedFrames > VideoFrameSamplingPlanner.HardMaximumFrames)
        {
            await transaction.CommitAsync(token);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                protocol = VideoFrameSceneCoveragePolicy.Version,
                readOnly = true, current = false,
                reasonCode = reason == "current-complete"
                    ? "unsupported-frame-count" : reason,
                sceneBoundariesOperatorAnnotated = true,
                originalsOpened = false, oneDriveHydrationRequested = false,
                captionsReturned = false, embeddingsReturned = false,
                schemaChanged = false, videoJobsEnqueued = false
            }));
            return 1;
        }

        long durationMs = (long)decimal.Round(state.DurationSeconds * 1000m,
            0, MidpointRounding.AwayFromZero);
        // Reject invalid private human annotations BEFORE querying any frame
        // rows; no coordinates or source-specific details enter diagnostics.
        VideoFrameSceneCoveragePolicy.Assess(durationMs,
            [0L], options.SceneCutsMs);

        List<long> observed = [];
        await using (NpgsqlCommand frames = connection.CreateCommand())
        {
            frames.Transaction = transaction;
            frames.CommandText = """
                SELECT ordinal, timestamp_ms
                FROM video_frame_evidence_frames
                WHERE job_id=@job
                ORDER BY ordinal;
                """;
            frames.Parameters.AddWithValue("job", options.Audit.JobId);
            await using NpgsqlDataReader reader = await frames.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (reader.GetInt32(0) != observed.Count)
                    throw new InvalidDataException("Frame ordinals are not contiguous.");
                observed.Add(reader.GetInt64(1));
            }
        }
        if (observed.Count != state.ExpectedFrames)
            throw new InvalidDataException("Frame evidence changed inside the snapshot.");

        VideoFrameSceneCoverageSummary actual =
            VideoFrameSceneCoveragePolicy.Assess(
                durationMs, observed, options.SceneCutsMs);
        var interval = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(state.DurationSeconds,
                MaximumFrames: observed.Count));
        var sceneBalanced = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(state.DurationSeconds,
                Mode: VideoFrameSamplingMode.SceneBoundaries,
                SceneBoundariesSeconds: options.SceneCutsMs
                    .Select(ms => ms / 1000m).ToArray(),
                MaximumFrames: observed.Count, MaximumFramesPerScene: 1));
        VideoFrameSceneCoverageSummary plannedInterval =
            VideoFrameSceneCoveragePolicy.Assess(
                durationMs, interval.Samples.Select(x => x.TimestampMilliseconds).ToArray(),
                options.SceneCutsMs);
        VideoFrameSceneCoverageSummary plannedScene =
            VideoFrameSceneCoveragePolicy.Assess(
                durationMs, sceneBalanced.Samples.Select(x => x.TimestampMilliseconds).ToArray(),
                options.SceneCutsMs);
        bool positionsMatchInterval = observed.SequenceEqual(
            interval.Samples.Select(x => x.TimestampMilliseconds));
        await transaction.CommitAsync(token);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = VideoFrameSceneCoveragePolicy.Version,
            readOnly = true, current = true, reasonCode = "current-complete",
            sceneBoundariesOperatorAnnotated = true,
            annotatedCuts = options.SceneCutsMs.Count,
            durationMs, actual,
            hypotheticalInterval = plannedInterval,
            hypotheticalSceneBalanced = plannedScene,
            observedPositionsMatchIntervalPlan = positionsMatchInterval,
            // The comparisons are hypothetical only; NO scene detection or
            // scene-based real-media extraction is activated by this command.
            sceneDetectionPerformed = false,
            mediaDecoded = false, originalsOpened = false,
            oneDriveHydrationRequested = false, captionsReturned = false,
            embeddingsReturned = false, videoJobsEnqueued = false,
            schemaChanged = false
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return 0;
    }
}
