using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>Rehearsal only: never accepts an unverified or non-local PostgreSQL database.</summary>
public sealed record VideoSchemaRehearsalOptions(
    string PostgresConnectionEnvironmentVariable,
    string RestoreVerificationReportPath)
{
    public static VideoSchemaRehearsalOptions Parse(string[] args)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--restore-report") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !options.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unexpected, duplicate, or incomplete schema rehearsal option.");
        }

        if (!options.TryGetValue("--postgres-connection-env", out string? variable) ||
            !Regex.IsMatch(variable, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Provide a PostgreSQL connection environment-variable name.");

        if (!options.TryGetValue("--restore-report", out string? report) ||
            string.IsNullOrWhiteSpace(report))
            throw new ArgumentException("Provide the restore-verification report from verify-postgres-backup-restore.ps1.");

        return new(variable, Path.GetFullPath(report));
    }
}

public static class VideoSchemaRehearsalCommandRunner
{
    // The backup restore script generates this prefix for isolated databases.
    private const string RehearsalPrefix = "photoidentity_restore_";
    private static readonly Regex SafeTableName = new(
        @"^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<int> RunAsync(
        VideoSchemaRehearsalOptions options,
        TextWriter output,
        CancellationToken token = default,
        int expectedRestoredSchemaVersion = 32)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (expectedRestoredSchemaVersion is not (32 or 33 or 34 or 37))
            throw new ArgumentOutOfRangeException(nameof(expectedRestoredSchemaVersion));

        string? connectionString = Environment.GetEnvironmentVariable(
            options.PostgresConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("The selected PostgreSQL connection environment variable is unset.");

        NpgsqlConnectionStringBuilder builder = new(connectionString);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("The rehearsal only accepts PostgreSQL on localhost.");
        if (!File.Exists(options.RestoreVerificationReportPath))
            throw new FileNotFoundException("The restore verification report does not exist.");

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(options.RestoreVerificationReportPath, token));
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("passed", out JsonElement passed) ||
            passed.ValueKind != JsonValueKind.True ||
            !TryString(root, "verificationDatabase", out string restoredName) ||
            !TryString(root, "sourceDatabase", out string sourceName) ||
            restoredName.Equals(sourceName, StringComparison.Ordinal) ||
            !restoredName.StartsWith(RehearsalPrefix, StringComparison.Ordinal) ||
            !Regex.IsMatch(restoredName,
                @"^photoidentity_restore_[0-9]{14}_[a-f0-9]{6}$",
                RegexOptions.CultureInvariant))
            throw new InvalidDataException(
                "Restore report is not a successful isolated verification for a safe restored database.");

        // The restore report is the only authority for selecting the target database.
        // The environment variable supplies only localhost server settings and credentials.
        // This avoids discrepancies between DbConnectionStringBuilder and Npgsql aliases
        // and never opens the source database embedded in the original connection string.
        builder.Database = restoredName;
        if (new NpgsqlConnectionStringBuilder(builder.ConnectionString).Database != restoredName)
            throw new InvalidDataException(
                "The restored database could not be selected unambiguously.");

        JsonElement evidence = root.GetProperty("restoredEvidence");
        if (evidence.GetProperty("schemaVersion").GetInt32() != expectedRestoredSchemaVersion)
            throw new InvalidOperationException(
                $"The restored source must be schema v{expectedRestoredSchemaVersion} for this rehearsal.");

        JsonElement tableCountsJson = evidence.GetProperty("tableCounts");
        Dictionary<string, long> expectedCounts = new(StringComparer.Ordinal);
        foreach (JsonProperty table in tableCountsJson.EnumerateObject())
        {
            if (!SafeTableName.IsMatch(table.Name) || !expectedCounts.TryAdd(table.Name, table.Value.GetInt64()))
                throw new InvalidDataException("Restore report contains an invalid table name or row count.");
        }
        if (!expectedCounts.ContainsKey("assets") ||
            !expectedCounts.ContainsKey("asset_revisions") ||
            !expectedCounts.ContainsKey("photo_identity_schema_migrations"))
            throw new InvalidDataException("Restore report is missing core catalogue table counts.");

        builder.Pooling = false;
        await using NpgsqlConnection beforeConnection = new(builder.ConnectionString);
        await beforeConnection.OpenAsync(token);

        int originalVersion = await SchemaVersionAsync(beforeConnection, token);
        if (originalVersion != expectedRestoredSchemaVersion)
            throw new InvalidOperationException(
                $"The selected rehearsal database is not at schema v{expectedRestoredSchemaVersion}; nothing was changed.");

        Dictionary<string, long> beforeCounts = await TableCountsAsync(
            beforeConnection, expectedCounts.Keys, token);
        AssertCounts(expectedCounts, beforeCounts, "pre-migration restored backup");

        // All destructive migration work is confined to the restored, verified local database.
        // The production database is never opened or named in the connection string.
        await using (PostgresCatalogueDatabase database = new(builder.ConnectionString))
        {
            await database.InitializeAsync(token);
            await database.InitializeAsync(token); // idempotency
        }

        Dictionary<string, long> afterCounts = await TableCountsAsync(
            beforeConnection, expectedCounts.Keys, token);
        // Current schema upgrades every intervening migration, including v33
        // for historic restores made before video acceptance.
        Dictionary<string, long> expectedAfter = new(expectedCounts, StringComparer.Ordinal)
        {
            ["photo_identity_schema_migrations"] =
                expectedCounts["photo_identity_schema_migrations"] +
                PostgresCatalogueDatabase.CurrentSchemaVersion - expectedRestoredSchemaVersion
        };
        AssertCounts(expectedAfter, afterCounts, "post-migration preserved tables");

        if (await SchemaVersionAsync(beforeConnection, token) != PostgresCatalogueDatabase.CurrentSchemaVersion)
            throw new InvalidDataException("Rehearsal did not record the current schema version.");

        if (expectedRestoredSchemaVersion == 37)
        {
            // A current maintained v37 catalogue may have real video jobs,
            // completed proxy receipts and non-default operator controls.
            // Never require the original v34 empty/default-state assertions.
            // The restore report's table counts were independently verified
            // before and after migration above.
            await using NpgsqlCommand frameSchema = beforeConnection.CreateCommand();
            frameSchema.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM photo_identity_schema_migrations
                     WHERE version=38 AND name='transactional-video-frame-evidence'),
                    (SELECT COUNT(*) FROM video_frame_evidence_batches),
                    (SELECT COUNT(*) FROM video_frame_evidence_frames),
                    (SELECT COUNT(*) FROM video_processing_control
                     WHERE singleton_id=1);
                """;
            await using NpgsqlDataReader frameRows = await frameSchema.ExecuteReaderAsync(token);
            if (!await frameRows.ReadAsync(token) ||
                frameRows.GetInt64(0) != 1 || frameRows.GetInt64(1) != 0 ||
                frameRows.GetInt64(2) != 0 || frameRows.GetInt64(3) != 1)
                throw new InvalidDataException(
                    "v38 frame evidence migration was not empty, complete and isolated.");
            output.WriteLine("Schema v37 -> v38 rehearsal passed on the isolated restored database.");
            output.WriteLine($"Rehearsal database: {restoredName}");
            output.WriteLine($"Unchanged catalogue tables verified: {expectedCounts.Count - 1}");
            output.WriteLine("Frame evidence tables empty; existing photo/video table counts and control row preserved.");
            output.WriteLine("Repeated schema initialization passed. The maintained catalogue was not opened.");
            return 0;
        }

        await using NpgsqlCommand verify = beforeConnection.CreateCommand();
        verify.CommandText = """
            SELECT
                (SELECT count(*) FROM assets WHERE media_kind <> 'photo'),
                (SELECT count(*) FROM asset_revisions WHERE media_kind <> 'photo'),
                (SELECT count(*) FROM video_revision_metadata),
                (SELECT count(*) FROM video_still_posters),
                (SELECT count(*) FROM photo_identity_schema_migrations
                    WHERE version = 33 AND name = 'opt-in-video-revision-metadata-and-posters'),
                (SELECT count(*) FROM video_processing_jobs),
                (SELECT count(*) FROM photo_identity_schema_migrations
                    WHERE version = 34 AND name = 'isolated-video-processing-jobs'),
                (SELECT count(*) FROM video_processing_control
                    WHERE singleton_id=1 AND NOT enabled AND paused
                      AND generation=0 AND last_action='initial'),
                (SELECT count(*) FROM photo_identity_schema_migrations
                    WHERE version=35 AND name='isolated-video-processing-operator-control'),
                (SELECT count(*) FROM video_published_derivatives),
                (SELECT count(*) FROM photo_identity_schema_migrations
                    WHERE version=36 AND name='lease-fenced-video-derivative-publication'),
                (SELECT count(*) FROM photo_identity_schema_migrations
                    WHERE version=37 AND name='scoped-one-shot-video-operator-authorization'),
                (SELECT count(*) FROM video_processing_control
                    WHERE singleton_id=1 AND selected_job_id IS NULL
                      AND authorization_expires_at_utc IS NULL);
            """;
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) ||
            (expectedRestoredSchemaVersion == 32 &&
             (reader.GetInt64(0) != 0 || reader.GetInt64(1) != 0 ||
              reader.GetInt64(2) != 0 || reader.GetInt64(3) != 0)) ||
            reader.GetInt64(4) != 1 ||
            (expectedRestoredSchemaVersion < 34 && reader.GetInt64(5) != 0) ||
            (expectedRestoredSchemaVersion == 34 &&
             reader.GetInt64(5) != expectedCounts["video_processing_jobs"]) ||
            reader.GetInt64(6) != 1 || reader.GetInt64(7) != 1 ||
            reader.GetInt64(8) != 1 || reader.GetInt64(9) != 0 ||
            reader.GetInt64(10) != 1 || reader.GetInt64(11) != 1 ||
            reader.GetInt64(12) != 1)
            throw new InvalidDataException(
                "Video v37 scoped control or v36 private publication registry verification failed.");

        output.WriteLine($"Schema v{expectedRestoredSchemaVersion} -> v{PostgresCatalogueDatabase.CurrentSchemaVersion} rehearsal passed on the isolated restored database.");
        output.WriteLine($"Rehearsal database: {restoredName}");
        output.WriteLine($"Unchanged catalogue tables verified: {expectedCounts.Count - 1}");
        output.WriteLine(expectedRestoredSchemaVersion == 32
            ? "Original photo-only catalogue preserved; video tables and new queue empty."
            : expectedRestoredSchemaVersion == 33
                ? "Original photo/video catalogue preserved; new video job queue empty; video control disabled and paused."
                : "Original photo/video catalogue and queued job counts preserved; v37 video control disabled, paused and unselected.");
        output.WriteLine("Repeated schema initialization passed. The maintained catalogue was not opened.");
        return 0;
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out JsonElement item) ||
            item.ValueKind != JsonValueKind.String) return false;
        value = item.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static async Task<int> SchemaVersionAsync(
        NpgsqlConnection connection, CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(version),0)
            FROM photo_identity_schema_migrations;
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(token));
    }

    private static async Task<Dictionary<string, long>> TableCountsAsync(
        NpgsqlConnection connection, IEnumerable<string> tableNames, CancellationToken token)
    {
        Dictionary<string, long> counts = new(StringComparer.Ordinal);
        foreach (string table in tableNames)
        {
            if (!SafeTableName.IsMatch(table))
                throw new InvalidDataException("Unsafe catalogue table name.");

            await using NpgsqlCommand command = connection.CreateCommand();
            // Name originated from the verified restore report; strict identifier allowlist
            // prevents injection and every table is schema-qualified.
            command.CommandText = $"SELECT count(*) FROM public.{table};";
            counts.Add(table, Convert.ToInt64(await command.ExecuteScalarAsync(token)));
        }
        return counts;
    }

    private static void AssertCounts(
        IReadOnlyDictionary<string, long> expected,
        IReadOnlyDictionary<string, long> actual,
        string stage)
    {
        foreach ((string name, long count) in expected)
        {
            if (!actual.TryGetValue(name, out long real) || count != real)
                throw new InvalidDataException(
                    $"Catalogue row counts differed during {stage}. Inspect the retained rehearsal database.");
        }
    }
}
