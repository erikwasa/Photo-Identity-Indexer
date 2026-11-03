using System.Data;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// WI-0210: one-off, Windows-only, read-only availability preflight for
/// exactly the candidate class measured by the metadata-only survey.
/// DB source locators stay inside the process. No source stream, probe,
/// SHA-256, OneDrive pin, hydrate, job or output file is ever opened/created.
/// </summary>

public sealed record VideoCompatibilityLocalSelectionOptions(
    VideoCompatibilitySurveyOptions Survey)
{
    public static VideoCompatibilityLocalSelectionOptions Parse(string[] args)
    {
        // Deliberate disclosure boundary: the normal local-check remains
        // aggregate-only, and this separate command requires an explicit
        // switch before printing local revision UUIDs to the operator.
        if (args.Count(x => x == "--show-revision-ids") != 1)
            throw new ArgumentException(
                "Local revision selection requires one --show-revision-ids flag.");
        return new(VideoCompatibilitySurveyOptions.Parse(
            args.Where(x => x != "--show-revision-ids").ToArray()));
    }
}

/// <summary>
/// Local-only UUIDs and bounded numeric stream metadata. Never sources,
/// filenames, paths, checksums, tags, capture times or private content.
/// Coded orientation is NOT the displayed rotation-adjusted orientation.
/// </summary>
public sealed record VideoCompatibilityLocalCandidate(
    Guid RevisionId, long SourceBytes, decimal DurationSeconds,
    string CodedOrientation, bool HasAudio);

public static class VideoCompatibilityLocalCheckCommandRunner
{
    private const int MaximumCandidateChecks = 64;
    private const long MaximumSourceBytes = 256L * 1024 * 1024;

    public static bool IsPixelOnlyCandidate(VideoCompatibilityObservation row) =>
        !row.Excluded &&
        row.Pixel == "yuvj420p" &&
        VideoCompatibilitySurvey.WorkerGate(row with { Pixel = "yuv420p" }) ==
            "metadata-eligible";

    public static Task<int> RunAsync(
        VideoCompatibilitySurveyOptions options, TextWriter output,
        CancellationToken token = default) =>
        RunInternalAsync(options, output, showRevisionIds: false, token);

    public static Task<int> RunCandidatesAsync(
        VideoCompatibilityLocalSelectionOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunInternalAsync(options.Survey, output, showRevisionIds: true, token);
    }

    private static async Task<int> RunInternalAsync(
        VideoCompatibilitySurveyOptions options, TextWriter output,
        bool showRevisionIds, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Live OneDrive availability inspection requires Windows.");

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("PostgreSQL connection variable is missing.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Local availability audit requires loopback PostgreSQL.");
        builder.Database = options.Database;
        await using PostgresCatalogueDatabase db = new(builder.ConnectionString);

        // Select private locators ONLY for this local, bounded attribute check.
        // Unlike the general compatibility census these keys must be in process
        // memory, but never escape into the JSON or an exception message.
        List<(Guid Revision, Guid Source, string Root, string Key,
            long Bytes, decimal Duration, int Width, int Height, bool Audio)> candidates = [];
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, token))
        {
            await using (NpgsqlCommand guard = connection.CreateCommand())
            {
                guard.Transaction = transaction;
                guard.CommandText = "SET TRANSACTION READ ONLY;";
                await guard.ExecuteNonQueryAsync(token);
            }
            await using (NpgsqlCommand version = connection.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText =
                    "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
                if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                    PostgresCatalogueDatabase.CurrentSchemaVersion)
                    throw new InvalidOperationException(
                        "Local availability audit requires current schema; never migrates.");
            }
            await using (NpgsqlCommand query = connection.CreateCommand())
            {
                query.Transaction = transaction;
                query.CommandTimeout = 60;
                query.CommandText = """
                    SELECT s.id, s.root_locator, a.source_key,
                           m.inspection_status, m.video_codec, m.pixel_format,
                           m.color_transfer, m.audio_codec, m.coded_width,
                           m.coded_height, m.display_rotation_degrees,
                           m.duration_seconds, r.size_bytes, r.id
                    FROM assets a
                    JOIN sources s ON s.id=a.source_id
                    JOIN LATERAL (
                        SELECT id,size_bytes FROM asset_revisions
                        WHERE asset_id=a.id AND media_kind='video'
                        ORDER BY observed_at_utc DESC,id DESC LIMIT 1
                    ) r ON true
                    JOIN video_revision_metadata m ON m.asset_revision_id=r.id
                    WHERE a.media_kind='video'
                      AND a.deleted_at_utc IS NULL
                      AND s.kind='local-folder'
                      AND m.pixel_format='yuvj420p'
                      AND NOT EXISTS (
                          SELECT 1 FROM source_copy_exclusions excluded
                          WHERE excluded.source_id=a.source_id
                            AND excluded.source_key=a.source_key);
                    """;
                await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(
                    CommandBehavior.SequentialAccess, token);
                while (await reader.ReadAsync(token))
                {
                    // SequentialAccess requires monotonic column reads;
                    // keep private locators in memory, never the report.
                    Guid sourceId = reader.GetGuid(0);
                    string sourceRoot = reader.GetString(1);
                    string sourceKey = reader.GetString(2);
                    string? T(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                    int? I(int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
                    // Npgsql SequentialAccess may ONLY move forward. Materialize
                    // every field in SELECT ordinal order before building the row;
                    // reading bytes (12) before metadata (3..11) is invalid.
                    string? inspection = T(3);
                    string? codec = T(4);
                    string? pixel = T(5);
                    string? transfer = T(6);
                    string? audio = T(7);
                    int? width = I(8);
                    int? height = I(9);
                    int? rotation = I(10);
                    decimal? duration = reader.IsDBNull(11)
                        ? null : reader.GetDecimal(11);
                    long bytes = reader.GetInt64(12);
                    Guid revisionId = reader.GetGuid(13);
                    VideoCompatibilityObservation row = new(
                        false, "local-folder", null, inspection, null, codec,
                        pixel, transfer, audio, width, height, rotation,
                        duration, bytes, false, false, false);
                    if (!IsPixelOnlyCandidate(row)) continue;
                    if (candidates.Count >= MaximumCandidateChecks)
                        throw new InvalidOperationException(
                            "More than 64 candidates; audit refuses a partial result.");
                    candidates.Add((revisionId, sourceId, sourceRoot, sourceKey,
                        bytes, duration!.Value, width!.Value, height!.Value,
                        audio is not null));
                }
            }
            await transaction.CommitAsync(token);
        }

        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        List<VideoCompatibilityLocalCandidate> matched = [];
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            string status = await ClassifyAsync(candidate.Source, candidate.Root,
                candidate.Key, candidate.Bytes, token);
            counts[status] = counts.TryGetValue(status, out int prior)
                ? checked(prior + 1) : 1;
            if (showRevisionIds && status == "local-metadata-match")
                matched.Add(new(candidate.Revision, candidate.Bytes,
                    candidate.Duration,
                    candidate.Width > candidate.Height ? "coded-landscape" :
                    candidate.Height > candidate.Width ? "coded-portrait" :
                    "coded-square", candidate.Audio));
        }

        if (showRevisionIds)
        {
            // These UUIDs are shown ONLY through the separate explicit
            // selection command. No automatic pick, no local media read.
            // A proof of unchanged original bytes requires a later
            // independently authorized --read-local-bytes operation.
            output.WriteLine(JsonSerializer.Serialize(new
            {
                protocol = "wi0210-explicit-local-revision-selection-v1",
                readOnly = true, candidateClass = "rec709-yuvj420p-pixel-only",
                revisionIdsShown = true, operatorSelectionRequired = true,
                candidateCount = candidates.Count,
                locallyMatchedCount = matched.Count,
                sourceContentOpened = false, oneDriveHydrationRequested = false,
                videosDecoded = false, jobsEnqueued = false,
                derivativesPublished = false, localIsNotIntegrityProven = true,
                candidates = matched.OrderBy(x => x.DurationSeconds)
                    .ThenBy(x => x.SourceBytes)
                    .ThenBy(x => x.RevisionId).ToArray()
            }));
            return 0;
        }

        var results = counts.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new { status = x.Key, count = x.Value });
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0210-live-local-candidate-availability-v1",
            readOnly = true, schemaChanged = false,
            candidateClass = "rec709-yuvj420p-pixel-only",
            candidateCount = candidates.Count,
            filesystemMetadataChecked = true,
            localIsNotIntegrityProven = true,
            sourceContentOpened = false,
            oneDriveHydrationRequested = false,
            videosDecoded = false, videoJobsEnqueued = false,
            derivativesPublished = false, outputsAreAggregated = true,
            availability = results
        }));
        return 0;
    }

    /// <summary>
    /// Attributes and length only, NEVER stream/file contents. Unsafe directory
    /// junctions, symlinks, escapes and network paths fail closed.
    /// The 'local-metadata-match' result is NOT a SHA or local-read proof.
    /// </summary>
    public static async Task<string> ClassifyAsync(
        Guid sourceId, string root, string key, long expectedBytes,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (sourceId == Guid.Empty || !Path.IsPathFullyQualified(root) ||
            root.StartsWith(@"\\", StringComparison.Ordinal) ||
            expectedBytes is < 1 or > MaximumSourceBytes ||
            string.IsNullOrWhiteSpace(key) ||
            Path.GetExtension(key).ToLowerInvariant() is not (".mov" or ".mp4"))
            return "unsafe-source";

        try
        {
            var source = new OneDriveVideoAssetSource(SourceId.From(sourceId), root);
            var reference = new SourceAssetReference(source.SourceId, key);
            string normalizedRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(root));
            string file = Path.GetFullPath(Path.Combine(normalizedRoot,
                key.Replace('/', Path.DirectorySeparatorChar)));
            StringComparison comparison = StringComparison.OrdinalIgnoreCase;
            if (!file.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
                return "unsafe-source";

            // Never traverse even a registered root with a junction/symlink.
            // Check each parent BEFORE requesting the leaf's status.
            for (string? parent = Path.GetDirectoryName(file); parent is not null;
                 parent = Path.GetDirectoryName(parent))
            {
                FileAttributes attributes = File.GetAttributes(parent);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return "unsafe-source";
                if (parent == Path.GetPathRoot(parent)) break;
            }

            AssetAvailability availability =
                await source.GetAvailabilityAsync(reference, token);
            if (availability != AssetAvailability.Local)
                return availability switch
                {
                    AssetAvailability.OnlineOnly => "online-only",
                    AssetAvailability.Downloading => "downloading",
                    AssetAvailability.Unavailable => "unavailable",
                    _ => "error"
                };

            // Local leaf symlinks are not accepted; online-only OneDrive
            // placeholders can legitimately have reparse attributes.
            if (!File.Exists(file) ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                return "unsafe-source";
            return new FileInfo(file).Length == expectedBytes
                ? "local-metadata-match" : "size-mismatch";
        }
        catch (Exception error) when (error is IOException or
            UnauthorizedAccessException or ArgumentException or
            System.Security.SecurityException or NotSupportedException)
        {
            return "error";
        }
    }
}
