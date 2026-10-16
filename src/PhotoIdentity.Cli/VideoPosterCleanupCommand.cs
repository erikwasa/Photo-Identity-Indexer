using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>Bounded, default-dry-run, source-scoped poster cleanup.</summary>
public sealed record VideoPosterCleanupOptions(
    string SourceRoot, string DerivativeRoot, string ConnectionEnvironmentVariable,
    string DatabaseName, int MaximumFiles, int MinimumAgeHours, bool Apply)
{
    public static VideoPosterCleanupOptions Parse(string[] args)
    {
        Dictionary<string, string> items = new(StringComparer.Ordinal);
        bool enabled = false, apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--enable-video" && !enabled) { enabled = true; continue; }
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            string key = args[i];
            if (key is not ("--source" or "--derivatives" or "--postgres-connection-env" or
                            "--database" or "--max-files" or "--min-age-hours") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !items.TryAdd(key, args[++i]))
                throw new ArgumentException("Unknown, repeated or incomplete video cleanup option.");
        }
        if (!enabled) throw new ArgumentException("Video cleanup requires --enable-video.");
        string Required(string key) => items.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing {key}.");
        int Bounded(string key, int fallback, int min, int max) =>
            !items.TryGetValue(key, out string? value) ? fallback :
            int.TryParse(value, out int parsed) && parsed >= min && parsed <= max ? parsed :
            throw new ArgumentException($"{key} must be {min}..{max}.");
        string env = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Use a named credentials variable and an explicit database identifier.");
        return new(Required("--source"), Required("--derivatives"), env, database,
            Bounded("--max-files", 25, 1, 25), Bounded("--min-age-hours", 24, 24, 720), apply);
    }
}

public static class VideoPosterCleanupCommandRunner
{
    // The first cleanup lane covers only the previously accepted, exact poster
    // profile. New poster policies must be explicitly audited before deletion.
    public const string Profile = VideoPosterCommandRunner.Profile;

    /// <summary>Only a canonical profile/revision/tool filename is delete-eligible.</summary>
    public static bool TryParseRelativePath(
        string relative, out AssetRevisionId revision, out string toolHash)
    {
        revision = default;
        toolHash = "";
        if (relative.Contains('\\') || relative.Contains(':') || relative.Contains(".."))
            return false;
        string[] parts = relative.Split('/');
        if (parts.Length != 4 || parts[0] != "video-posters" || parts[1] != Profile ||
            !Guid.TryParseExact(parts[2], "D", out Guid parsed) ||
            parsed.ToString("D") != parts[2] ||
            !Regex.IsMatch(parts[3], @"^[0-9a-f]{64}\.jpg$", RegexOptions.CultureInvariant))
            return false;
        revision = AssetRevisionId.From(parsed);
        toolHash = parts[3][..64];
        return true;
    }

    public static bool IsOldEnough(DateTimeOffset when, DateTimeOffset now, int minimumAgeHours) =>
        when <= now.AddHours(-minimumAgeHours);

    public static async Task<int> RunAsync(
        VideoPosterCleanupOptions options, TextWriter output, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string sourceRoot = Path.GetFullPath(options.SourceRoot);
        string derivativeRoot = Path.GetFullPath(options.DerivativeRoot);
        VideoPosterCommandRunner.ValidateRoots(sourceRoot, derivativeRoot);

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("PostgreSQL credentials environment variable is unavailable.");
        NpgsqlConnectionStringBuilder connection = new(raw);
        if (connection.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Video poster cleanup requires localhost PostgreSQL.");
        connection.Database = options.DatabaseName;

        await using PostgresCatalogueDatabase db = new(connection.ConnectionString);
        var source = await new PostgresCatalogueSourceRepository(db)
            .FindLocalFolderSourceAsync(sourceRoot, token) ??
            throw new InvalidOperationException("Selected video source is not registered.");
        PostgresVideoPosterCleanupRepository catalogue = new(db);
        IReadOnlyList<VideoPosterCleanupRow> snapshot =
            await catalogue.SnapshotAsync(source.SourceId, token);
        Dictionary<Guid, List<VideoPosterCleanupRow>> byRevision = snapshot
            .GroupBy(row => row.RevisionId.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        string posterRoot = Path.Combine(derivativeRoot, "video-posters");
        string profileRoot = Path.Combine(posterRoot, Profile);
        AssertSafeDirectory(posterRoot);
        AssertSafeDirectory(profileRoot);
        List<(string Relative, string Absolute, DateTimeOffset Written)> files = [];

        if (Directory.Exists(profileRoot))
        {
            foreach (string folder in Directory.EnumerateDirectories(profileRoot))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (!Guid.TryParseExact(Path.GetFileName(folder), "D", out Guid revisionGuid) ||
                    revisionGuid.ToString("D") != Path.GetFileName(folder))
                    continue;
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    token.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                    string relative = Path.GetRelativePath(derivativeRoot, file).Replace('\\', '/');
                    if (!TryParseRelativePath(relative, out _, out _)) continue;
                    files.Add((relative, file, new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero)));
                }
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Dictionary<string, (string Absolute, DateTimeOffset Written)> knownFiles =
            files.ToDictionary(file => file.Relative, file => (file.Absolute, file.Written),
                StringComparer.Ordinal);
        int staleRows = 0, orphans = 0, removedFiles = 0, removedRows = 0;
        int missing = 0, retained = 0, unsafeFiles = 0, errors = 0;
        int attempted = 0;

        // Rows are authoritative for known exclusions and superseded revisions.
        foreach (VideoPosterCleanupRow row in snapshot)
        {
            token.ThrowIfCancellationRequested();
            if (row.RelativePath is null || row.PosterProfileId != Profile ||
                !TryParseRelativePath(row.RelativePath, out AssetRevisionId identity,
                    out string tool) ||
                identity != row.RevisionId || tool != row.ToolSha256)
            { retained++; continue; }
            if (row.IsCurrent) { retained++; continue; }
            if (row.GeneratedAtUtc is not DateTimeOffset generated ||
                !IsOldEnough(generated, now, options.MinimumAgeHours))
            { retained++; continue; }
            bool exists = knownFiles.TryGetValue(row.RelativePath,
                out (string Absolute, DateTimeOffset Written) entry);
            if (exists && !IsOldEnough(entry.Written, now, options.MinimumAgeHours))
            { retained++; continue; }
            if (exists && !await MatchesEvidenceAsync(entry.Absolute, row, token))
            { unsafeFiles++; continue; }

            staleRows++;
            if (!exists) missing++;
            if (!options.Apply || attempted >= options.MaximumFiles) continue;
            attempted++;
            if (!await catalogue.RetireStalePosterAsync(source.SourceId, row, token))
            { retained++; continue; }
            removedRows++;
            if (!exists) continue;
            // Recheck AFTER the DB row is retired, including active revision state:
            // a concurrent edit/reversion or a new poster row must block deletion.
            if (!await catalogue.MayRemoveUnreferencedAsync(
                    source.SourceId, row.RevisionId, Profile, tool, token) ||
                !await MatchesEvidenceAsync(entry.Absolute, row, token))
            { unsafeFiles++; continue; }
            File.Delete(entry.Absolute);
            removedFiles++;
        }

        // Orphans are only safe when a still-existing DB revision GUID identifies
        // this source and the revision is no longer current. Purged/foreign/unknown
        // GUIDs cannot be owned reliably and are intentionally NOT deleted.
        foreach ((string relative, string absolute, DateTimeOffset written) in files)
        {
            token.ThrowIfCancellationRequested();
            if (!TryParseRelativePath(relative, out AssetRevisionId rev, out string tool))
                continue;
            if (byRevision.TryGetValue(rev.Value, out List<VideoPosterCleanupRow>? related))
            {
                if (related.Any(row => row.RelativePath == relative)) continue;
                if (related.Any(row => row.IsCurrent)) { retained++; continue; }
            }
            else { retained++; continue; }
            if (!IsOldEnough(written, now, options.MinimumAgeHours))
            { retained++; continue; }
            if (!IsValidPosterFile(absolute)) { unsafeFiles++; continue; }
            if (!await catalogue.MayRemoveUnreferencedAsync(
                    source.SourceId, rev, Profile, tool, token))
            { retained++; continue; }
            orphans++;
            if (!options.Apply || attempted >= options.MaximumFiles) continue;
            attempted++;
            if (!await catalogue.MayRemoveUnreferencedAsync(
                    source.SourceId, rev, Profile, tool, token) ||
                !IsValidPosterFile(absolute) ||
                !IsOldEnough(new DateTimeOffset(File.GetLastWriteTimeUtc(absolute), TimeSpan.Zero),
                    now, options.MinimumAgeHours))
            { unsafeFiles++; continue; }
            File.Delete(absolute);
            removedFiles++;
        }

        output.WriteLine(options.Apply
            ? "Video poster cleanup applied to bounded verified candidates."
            : "Video poster cleanup preview only: no records or files deleted.");
        output.WriteLine($"Stale recorded {staleRows}; unreferenced owned {orphans}; missing recorded files {missing}; unchanged/unknown {retained}; unsafe candidates {unsafeFiles}; errors {errors}.");
        output.WriteLine($"Rows retired {removedRows}; JPEG files deleted {removedFiles}; maximum deletions per invocation {options.MaximumFiles}.");
        output.WriteLine("Originals and photo derivatives were not deleted. Unattributable/purged-revision files are never automatically removed.");
        return unsafeFiles == 0 && errors == 0 ? 0 : 1;
    }

    private static async Task<bool> MatchesEvidenceAsync(
        string path, VideoPosterCleanupRow row, CancellationToken token)
    {
        if (!IsValidPosterFile(path) || row.EncodedBytes != new FileInfo(path).Length)
            return false;
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant()
            == row.PosterSha256;
    }

    private static bool IsValidPosterFile(string path)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            return false;
        try { _ = VideoPosterCommandRunner.ReadJpegDimensions(path); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static void AssertSafeDirectory(string path)
    {
        if (Directory.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Poster cleanup root includes a reparse point.");
    }
}
