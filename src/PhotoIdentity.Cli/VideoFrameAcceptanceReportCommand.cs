using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

public sealed record VideoFrameAcceptanceReportManifest(
    string Protocol, VideoFrameAcceptanceReportCase[] Cases);

public sealed record VideoFrameAcceptanceReportCase(
    Guid JobId, string[] Categories, long[]? SceneCutsMs,
    long? ExecutionWallMs, long? CpuTimeMs,
    long? GpuTimeMs, long? AzureCostMinorUnits,
    int? CaptionsJudged, int? CaptionsAccurate);

/// <summary>All options are strict. No use of model, media or source path.</summary>
public sealed record VideoFrameAcceptanceReportOptions(
    string ConnectionEnv, string Database, string ManifestPath)
{
    public static VideoFrameAcceptanceReportOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--database" or "--manifest") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unexpected or repeated acceptance report option.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Acceptance report requires explicit private inputs.");
        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Local PostgreSQL selector is invalid.");
        return new(env, db,
            VideoFrameLiveRetrievalOptions.PhysicalLocalFile(Required("--manifest")));
    }
}

public static class VideoFrameAcceptanceReportCommandRunner
{
    private const int MaximumManifestBytes = 64 * 1024;

    public static void ValidateManifest(VideoFrameAcceptanceReportManifest data)
    {
        if (data is null || data.Protocol != VideoFrameAcceptanceEvidence.Protocol ||
            data.Cases is null ||
            data.Cases.Length is < 1 or > VideoFrameAcceptanceEvidence.MaximumClips)
            throw new ArgumentException("Invalid bounded acceptance report manifest.");
        HashSet<Guid> jobs = [];
        foreach (VideoFrameAcceptanceReportCase item in data.Cases)
        {
            if (item is null || item.JobId == Guid.Empty ||
                !jobs.Add(item.JobId) ||
                item.Categories is null ||
                item.Categories.Length is < 1 or > VideoFrameAcceptanceEvidence.RequiredCategories.Count ||
                item.Categories.Any(c => !VideoFrameAcceptanceEvidence.RequiredCategories.Contains(c)) ||
                item.Categories.Distinct(StringComparer.Ordinal).Count() != item.Categories.Length ||
                (item.SceneCutsMs is { } sceneLimits &&
                    sceneLimits.Length > VideoFrameSamplingPlanner.HardMaximumSceneBoundaries) ||
                item.SceneCutsMs is { } cuts && cuts.Where((v, i) =>
                    v <= 0 || (i > 0 && v <= cuts[i - 1])).Any() ||
                item.ExecutionWallMs is < 1 or > 86_400_000 ||
                item.CpuTimeMs is < 0 or > 86_400_000 ||
                item.GpuTimeMs is < 0 or > 86_400_000 ||
                item.AzureCostMinorUnits is < 0 or > 1_000_000_000 ||
                item.CaptionsJudged.HasValue != item.CaptionsAccurate.HasValue ||
                item.CaptionsJudged is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
                item.CaptionsAccurate is < 0 ||
                item.CaptionsAccurate > item.CaptionsJudged)
                throw new ArgumentException("Invalid private acceptance case.");
        }
    }

    public static async Task<int> RunAsync(
        VideoFrameAcceptanceReportOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        VideoFrameAcceptanceReportManifest manifest;
        try
        {
            string path = VideoFrameLiveRetrievalOptions.PhysicalLocalFile(options.ManifestPath);
            string? current = path;
            while (current is not null)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked manifest refused.");
                current = Path.GetDirectoryName(current);
            }
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new InvalidDataException("Acceptance manifest must be a regular file.");
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length is < 1 or > MaximumManifestBytes)
                throw new InvalidDataException("Acceptance manifest size invalid.");
            byte[] bytes = new byte[checked((int)file.Length)];
            await file.ReadExactlyAsync(bytes, token);
            if (await file.ReadAsync(new byte[1], token) != 0)
                throw new InvalidDataException("Acceptance manifest changed during read.");
            manifest = JsonSerializer.Deserialize<VideoFrameAcceptanceReportManifest>(
                bytes, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    MaxDepth = 12
                }) ?? throw new InvalidDataException("Missing acceptance manifest.");
            ValidateManifest(manifest);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or NotSupportedException)
        {
            throw new InvalidOperationException("Private acceptance manifest is invalid or unavailable.");
        }

        string? connectionString = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Local PostgreSQL connection is not configured.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Acceptance report requires loopback PostgreSQL.");
        builder.Database = options.Database;

        VideoFrameAcceptanceEvidenceSummary summary;
        try
        {
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
            await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, token);
            await using (NpgsqlCommand readOnly = connection.CreateCommand())
            {
                readOnly.Transaction = tx;
                readOnly.CommandText = "SET TRANSACTION READ ONLY;";
                await readOnly.ExecuteNonQueryAsync(token);
            }
            await using (NpgsqlCommand schema = connection.CreateCommand())
            {
                schema.Transaction = tx;
                schema.CommandText =
                    "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
                if (Convert.ToInt32(await schema.ExecuteScalarAsync(token)) !=
                    PostgresCatalogueDatabase.CurrentSchemaVersion)
                    throw new InvalidOperationException("Current PostgreSQL v38 is required.");
            }

            List<VideoFrameAcceptanceObservation> observations = [];
            HashSet<Guid> distinctAssets = [];
            foreach (VideoFrameAcceptanceReportCase item in manifest.Cases)
            {
                VideoFrameEvidenceAuditSnapshot? state =
                    await VideoFrameEvidenceAuditCommandRunner.ReadSnapshotAsync(
                        connection, tx, item.JobId, token);
                if (VideoFrameEvidenceAuditPolicy.Classify(state) != "current-complete" ||
                    state!.ExpectedFrames > VideoFrameSamplingPlanner.HardMaximumFrames)
                    throw new InvalidOperationException(
                        "One or more selected frame batches are not current and complete.");

                // Distinct video assets, not multiple sampling variants of
                // the same clip, count toward the representative corpus.
                await using (NpgsqlCommand asset = connection.CreateCommand())
                {
                    asset.Transaction = tx;
                    asset.CommandText = """
                        SELECT revision.asset_id
                        FROM video_processing_jobs job
                        JOIN asset_revisions revision ON revision.id=job.asset_revision_id
                        WHERE job.id=@job;
                        """;
                    asset.Parameters.AddWithValue("job", item.JobId);
                    if (await asset.ExecuteScalarAsync(token) is not Guid id ||
                        !distinctAssets.Add(id))
                        throw new InvalidOperationException(
                            "Acceptance report requires distinct current video assets.");
                }

                long durationMs = (long)decimal.Round(state.DurationSeconds * 1000m,
                    0, MidpointRounding.AwayFromZero);
                VideoFrameAcceptanceSceneCoverage? coverage = null;
                if (item.SceneCutsMs is not null)
                {
                    // Validate private annotations against inspected duration
                    // before reading timestamp rows.
                    VideoFrameSceneCoveragePolicy.Assess(durationMs,
                        [0L], item.SceneCutsMs);
                    List<long> timestamps = [];
                    await using NpgsqlCommand positions = connection.CreateCommand();
                    positions.Transaction = tx;
                    positions.CommandText = """
                        SELECT ordinal, timestamp_ms
                        FROM video_frame_evidence_frames
                        WHERE job_id=@job ORDER BY ordinal;
                        """;
                    positions.Parameters.AddWithValue("job", item.JobId);
                    await using (NpgsqlDataReader reader =
                        await positions.ExecuteReaderAsync(token))
                    {
                        while (await reader.ReadAsync(token))
                        {
                            if (reader.GetInt32(0) != timestamps.Count)
                                throw new InvalidOperationException(
                                    "Frame positions have invalid ordering.");
                            timestamps.Add(reader.GetInt64(1));
                        }
                    }
                    if (timestamps.Count != state.ExpectedFrames)
                        throw new InvalidOperationException(
                            "Frame positions have incomplete counts.");
                    var assessed = VideoFrameSceneCoveragePolicy.Assess(
                        durationMs, timestamps, item.SceneCutsMs);
                    coverage = new(assessed.SampledFrames, assessed.TotalScenes,
                        assessed.CoveredScenes, assessed.MissedScenes);
                }

                observations.Add(new(item.JobId, item.Categories,
                    state.ExpectedFrames, durationMs,
                    checked(state.CaptionBytes + state.VectorBytes),
                    coverage, item.ExecutionWallMs, item.CpuTimeMs,
                    item.GpuTimeMs, item.AzureCostMinorUnits,
                    item.CaptionsJudged, item.CaptionsAccurate));
            }
            summary = VideoFrameAcceptanceEvidence.Summarize(observations);
            await tx.CommitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            // Never include SQL/provider exception text, media paths,
            // private annotation positions or job IDs in stdout/stderr.
            throw new InvalidOperationException(
                "One or more private frame batches or observations could not be verified.");
        }

        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = VideoFrameAcceptanceEvidence.Protocol,
            readOnly = true, currentSnapshot = true,
            humanObservationsSelfReported = true,
            coverageFromOperatorAnnotatedCutsOnly = true,
            cpuGpuCostFromOperatorProvidedMeasurementsOnly = true,
            evidence = summary,
            originalsOpened = false, oneDriveHydrationRequested = false,
            mediaDecoded = false, captionsReturned = false,
            embeddingsReturned = false, videoJobsEnqueued = false,
            schemaChanged = false
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return 0;
    }
}
