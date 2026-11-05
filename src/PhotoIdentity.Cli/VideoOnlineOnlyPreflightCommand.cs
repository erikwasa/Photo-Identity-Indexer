using System.Data;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// WI-0207 checkpoint 1: read-only current-video metadata plus Windows
/// Files On-Demand attribute inspection. Not a hydration request, an
/// enqueue/processing command or permission to choose private source files.
///
/// Only aggregate fixed-category results leave this process. Root locators,
/// source keys, UUIDs, thumbnails and original bytes never reach output.
/// </summary>
public static class VideoOnlineOnlyPreflightCommandRunner
{
    public const int MaximumCandidates = 64;
    private const long MaximumSourceBytes = 256L * 1024 * 1024;

    // Fixed labels: never return Windows exception messages, user paths,
    // synced file names or raw platform-provided error strings.
    public static string Classify(OneDriveFilesOnDemandState state) =>
        state.Availability switch
        {
            AssetAvailability.OnlineOnly when state.IsPinned =>
                "pinned-downloading",
            AssetAvailability.OnlineOnly => "online-only",
            AssetAvailability.Downloading when state.IsPinned =>
                "pinned-downloading",
            AssetAvailability.Downloading => "downloading",
            AssetAvailability.Local when state.IsPinned => "pinned-local",
            AssetAvailability.Local => "local",
            AssetAvailability.Unavailable => "unavailable",
            _ => "unknown-or-error"
        };

    public static async Task<int> RunAsync(
        VideoCompatibilitySurveyOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "OneDrive attribute preflight requires the maintained Windows host.");

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Approved local PostgreSQL configuration is absent.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException(
                "Online-only preflight requires a loopback PostgreSQL host.");
        builder.Database = options.Database;

        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
        // Never call InitializeAsync: this command is strictly read-only
        // and refuses an unrecognized schema rather than migrating it.
        List<EligibleVideo> rows = [];
        await using (NpgsqlConnection conn = await database.OpenConnectionAsync(token))
        await using (NpgsqlTransaction transaction = await conn.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, token))
        {
            await using (NpgsqlCommand readOnly = conn.CreateCommand())
            {
                readOnly.Transaction = transaction;
                readOnly.CommandText = "SET TRANSACTION READ ONLY;";
                await readOnly.ExecuteNonQueryAsync(token);
            }

            await using (NpgsqlCommand version = conn.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText =
                    "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
                if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                    PostgresCatalogueDatabase.CurrentSchemaVersion)
                    throw new InvalidOperationException(
                        "OneDrive preflight requires the already-migrated catalogue.");
            }

            await using (NpgsqlCommand command = conn.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = 60;
                command.CommandText = """
                    SELECT source.id, source.root_locator, asset.source_key,
                           revision.size_bytes, metadata.pixel_format,
                           EXISTS (
                             SELECT 1 FROM video_processing_jobs job
                             WHERE job.asset_revision_id=revision.id
                               AND job.status IN ('queued','running')
                               AND job.cancellation_requested_at_utc IS NULL),
                           EXISTS (
                             SELECT 1 FROM asset_revision_managed_hydrations managed
                             WHERE managed.asset_revision_id=revision.id
                               AND managed.released_at_utc IS NULL)
                    FROM assets asset
                    JOIN sources source ON source.id=asset.source_id
                    JOIN LATERAL (
                        SELECT id,size_bytes FROM asset_revisions
                        WHERE asset_id=asset.id AND media_kind='video'
                        ORDER BY observed_at_utc DESC,id DESC LIMIT 1
                    ) revision ON true
                    JOIN video_revision_metadata metadata
                      ON metadata.asset_revision_id=revision.id
                    WHERE asset.media_kind='video'
                      AND asset.deleted_at_utc IS NULL
                      AND source.kind='local-folder'
                      AND metadata.inspection_status='supported'
                      AND metadata.color_transfer='bt709'
                      AND metadata.video_codec IN ('h264','hevc')
                      AND metadata.pixel_format IN (
                        'yuv420p','yuv420p10le','yuvj420p')
                      AND (metadata.audio_codec IS NULL OR
                           metadata.audio_codec IN ('aac','pcm_s16le','pcm_s24le'))
                      AND metadata.coded_width BETWEEN 16 AND 4096
                      AND metadata.coded_height BETWEEN 16 AND 4096
                      AND metadata.duration_seconds BETWEEN 0.2 AND 600
                      AND metadata.display_rotation_degrees IN (0,90,-90,180,-180)
                      AND revision.size_bytes BETWEEN 1 AND @maxBytes
                      AND NOT EXISTS (
                        SELECT 1 FROM source_copy_exclusions excluded
                        WHERE excluded.source_id=asset.source_id
                          AND excluded.source_key=asset.source_key)
                      AND NOT EXISTS (
                        SELECT 1 FROM video_published_derivatives proxy
                        JOIN video_processing_jobs completed
                          ON completed.id=proxy.job_id
                        WHERE proxy.asset_revision_id=revision.id
                          AND completed.status='succeeded'
                          AND completed.derivative_kind='PlayableProxy')
                    ORDER BY revision.id
                    LIMIT @limit;
                    """;
                command.Parameters.AddWithValue("maxBytes", MaximumSourceBytes);
                command.Parameters.AddWithValue("limit", MaximumCandidates + 1);
                await using NpgsqlDataReader reader =
                    await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    // Read all returned attributes in SQL column order.
                    Guid source = reader.GetGuid(0);
                    string root = reader.GetString(1);
                    string key = reader.GetString(2);
                    long size = reader.GetInt64(3);
                    string pixel = reader.GetString(4);
                    bool queued = reader.GetBoolean(5);
                    bool managed = reader.GetBoolean(6);
                    if (rows.Count >= MaximumCandidates)
                        throw new InvalidOperationException(
                            "More than 64 eligible videos: refusing partial preflight results.");
                    rows.Add(new EligibleVideo(
                        source, root, key, size, pixel, queued, managed));
                }
            }
            await transaction.CommitAsync(token);
        }

        IOneDriveFilesOnDemandPlatform oneDrive =
            new WindowsOneDriveFilesOnDemandPlatform();
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        int queuedWork = 0, activeManaged = 0, standard = 0, fullRange = 0;
        foreach (EligibleVideo row in rows)
        {
            token.ThrowIfCancellationRequested();
            string category = ReadFileStatus(row, oneDrive);
            counts[category] = counts.TryGetValue(category, out int count)
                ? checked(count + 1) : 1;
            if (row.Queued) queuedWork++;
            if (row.Managed) activeManaged++;
            if (row.Pixel == "yuvj420p") fullRange++;
            else standard++;
        }

        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0207-online-only-preflight-v1",
            dryRun = true, readOnly = true, schemaChanged = false,
            originalContentOpened = false,
            oneDriveHydrationRequested = false,
            oneDriveReleaseRequested = false,
            archiveAdvanceStarted = false,
            videoJobsEnqueued = false,
            ffmpegInvoked = false,
            candidateSelectionOnly = true,
            operatorAuthorizationRequired = true,
            catalogueCurrentSupportedWithoutPublishedProxy = rows.Count,
            standardOrCompactProfileCandidates = standard,
            fullRangeProfileCandidates = fullRange,
            queuedOrRunningDependents = queuedWork,
            activePhotoManagedHydrationRecords = activeManaged,
            attributesObserved = rows.Count,
            availability = counts.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { status = x.Key, count = x.Value }).ToArray(),
            caveat = "Metadata and attributes are not proof of actual local bytes, ongoing source eligibility, cross-job hydration ownership or processing success."
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return 0;
    }

    /// <summary>
    /// Read Windows metadata only. Never open, pin, unpin, hash or hydrate the
    /// original. Refuse UNC/network roots, relative escapes and linked
    /// directory ancestors before querying the source's file attributes.
    /// Online-only leaf reparse attributes are expected and not traversed.
    /// </summary>
    private static string ReadFileStatus(
        EligibleVideo row, IOneDriveFilesOnDemandPlatform platform)
    {
        if (row.SourceId == Guid.Empty ||
            !Path.IsPathFullyQualified(row.Root) ||
            row.Root.StartsWith(@"\\", StringComparison.Ordinal) ||
            row.SourceBytes is < 1 or > MaximumSourceBytes ||
            Path.GetExtension(row.Key).ToLowerInvariant() is not (".mp4" or ".mov"))
            return "unsafe-source";

        try
        {
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(row.Root));
            string file = Path.GetFullPath(Path.Combine(root,
                row.Key.Replace('/', Path.DirectorySeparatorChar)));
            StringComparison compare = StringComparison.OrdinalIgnoreCase;
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, compare))
                return "unsafe-source";

            for (string? parent = Path.GetDirectoryName(file); parent is not null;
                 parent = Path.GetDirectoryName(parent))
            {
                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    return "unsafe-source";
                if (parent == Path.GetPathRoot(parent)) break;
            }

            return Classify(platform.GetState(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException
            or System.Security.SecurityException)
        {
            return "unknown-or-error";
        }
    }

    private sealed record EligibleVideo(
        Guid SourceId, string Root, string Key, long SourceBytes,
        string Pixel, bool Queued, bool Managed);
}
