using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// Dry-run-first exact revision selection. No source path option and no
/// video processing permission: merely queue an already-local supported
/// video revision for a separate explicit single-job trial.
/// </summary>
public sealed record VideoSelectedEnqueueOptions(
    string ConnectionEnv, string DatabaseName, Guid RevisionId,
    string FfmpegSha256, bool Apply)
{
    public static VideoSelectedEnqueueOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                               "--revision-id" or "--ffmpeg-sha256") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unknown, repeated or incomplete video queue selection.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Selected video queue requires all explicit fields.");
        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--revision-id"), out Guid revision) ||
            revision == Guid.Empty ||
            !VideoDerivativeArtifactPath.IsHash(Required("--ffmpeg-sha256")))
            throw new ArgumentException("Invalid explicit revision/database or FFmpeg digest.");
        return new(env, db, revision, Required("--ffmpeg-sha256"), apply);
    }
}

public static class VideoSelectedEnqueueCommandRunner
{
    private const long MaxSourceBytes = 256L * 1024 * 1024;
    private const long MaxReservationBytes = 512L * 1024 * 1024;
    public static async Task<int> RunAsync(
        VideoSelectedEnqueueOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        // A previously eligible local revision is not a green light to
        // enqueue real media. Block before opening PostgreSQL or inspecting
        // original-file availability until the WI-0188 containment gates
        // are independently accepted in a subsequent reviewed PR.
        if (options.Apply)
        {
            WriteApplyDenied(output);
            return 1;
        }
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Selected video enqueue requires Windows.");
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Video queue selection needs PostgreSQL configuration.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new ArgumentException("Video queue selection requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;
        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
        await using (NpgsqlConnection conn = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand version = conn.CreateCommand())
        {
            version.CommandText = """
                SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Explicit queue selection requires rehearsed v37 schema; no automatic migration.");
        }

        var control = await new PostgresVideoProcessingControlRepository(database)
            .GetAsync(token);
        if (control.Enabled || !control.Paused ||
            (await new PostgresVideoPhotoPriorityProbe(database)
                .GetAsync(token)).PhotoWorkActive)
        {
            Write(output, true, false, null);
            return 1;
        }

        Guid sourceId;
        string root, key;
        long size;
        await using (NpgsqlConnection conn = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand query = conn.CreateCommand())
        {
            query.CommandText = """
                SELECT source.id, source.root_locator, asset.source_key,
                       revision.size_bytes
                FROM asset_revisions revision
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                JOIN video_revision_metadata metadata
                  ON metadata.asset_revision_id=revision.id
                WHERE revision.id=@revision
                  AND revision.media_kind='video' AND asset.media_kind='video'
                  AND asset.deleted_at_utc IS NULL
                  AND source.kind='local-folder'
                  AND metadata.inspection_status='supported'
                  AND revision.size_bytes BETWEEN 1 AND @maxbytes
                  AND revision.id=(
                    SELECT latest.id FROM asset_revisions latest
                    WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                    ORDER BY latest.observed_at_utc DESC,latest.id DESC LIMIT 1)
                  AND NOT EXISTS (
                    SELECT 1 FROM source_copy_exclusions excluded
                    WHERE excluded.source_id=asset.source_id
                      AND excluded.source_key=asset.source_key);
                """;
            query.Parameters.AddWithValue("revision", options.RevisionId);
            query.Parameters.AddWithValue("maxbytes", MaxSourceBytes);
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                Write(output, true, false, null);
                return 1;
            }
            sourceId = reader.GetGuid(0);
            root = reader.GetString(1);
            key = reader.GetString(2);
            size = reader.GetInt64(3);
        }
        bool local = false;
        try
        {
            if (Path.IsPathFullyQualified(root) &&
                (!OperatingSystem.IsWindows() ||
                 !root.StartsWith(@"\\", StringComparison.Ordinal)))
            {
                var source = new OneDriveVideoAssetSource(SourceId.From(sourceId), root);
                var reference = new SourceAssetReference(source.SourceId, key);
                local = await source.GetAvailabilityAsync(reference, token) ==
                    AssetAvailability.Local;
                if (local)
                {
                    string path = source.GetLocalPath(reference);
                    local = File.Exists(path) &&
                        new FileInfo(path).Length == size &&
                        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
                }
            }
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            local = false;
        }

        var inspection = await new PostgresVideoEvidenceRepository(database)
            .GetInspectionAsync(AssetRevisionId.From(options.RevisionId), token);
        bool eligible = local && VideoProxyCandidateProfile.Eligible(inspection);
        if (!options.Apply || !eligible)
        {
            Write(output, true, eligible, null);
            return eligible ? 0 : 1;
        }

        // Defensive currentness and exclusion checks are repeated by
        // EnqueueAsync under the authoritative source-row lock.
        VideoProcessingBudget budget = new(
            Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
            MaximumSourceBytes: MaxSourceBytes,
            MaximumTemporaryBytes: MaxReservationBytes,
            MaximumExtractedFrames: 1, MaximumJobSeconds: 660);
        var identity = new VideoDerivativeJobIdentity(
            AssetRevisionId.From(options.RevisionId),
            VideoDerivativeKind.PlayableProxy, "wi0188-v1",
            VideoProxyCandidateProfile.Version, options.FfmpegSha256);
        var candidate = new VideoProcessingCandidate(
            IsCurrentRevision: true, IsExcluded: false,
            IsLocallyAvailable: true, HasSupportedInspection: true,
            SourceBytes: size, ExpectedTemporaryBytes: size + 256L * 1024 * 1024,
            RequestedFrames: 0);
        Guid jobId = await new PostgresVideoProcessingJobRepository(database)
            .EnqueueAsync(identity, budget, candidate, token);
        Write(output, false, true, jobId);
        return 0;
    }

    // Keep the established successful preview response shape unchanged.
    // Only denied apply includes a fixed, non-private reason code.
    private static void WriteApplyDenied(TextWriter output) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-selected-enqueue-v1",
            dryRun = true, eligible = false, jobId = (Guid?)null,
            mediaOpened = false, videoExecuted = false,
            reasonCode = "media-isolation-not-approved"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    private static void Write(TextWriter output, bool dryRun, bool eligible, Guid? jobId) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-selected-enqueue-v1",
            dryRun, eligible, jobId,
            mediaOpened = false, videoExecuted = false
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}
