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
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        string? connectionString = Environment.GetEnvironmentVariable(
            options.PostgresConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("The selected PostgreSQL connection environment variable is unset.");

        NpgsqlConnectionStringBuilder builder = new(connectionString);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("The rehearsal only accepts PostgreSQL on localhost.");
        if (string.IsNullOrEmpty(builder.Database) ||
            !builder.Database.StartsWith(RehearsalPrefix, StringComparison.Ordinal) ||
            !Regex.IsMatch(builder.Database, @"^[a-z][a-z0-9_]{0,62}$"))
            throw new ArgumentException("Rehearsal requires a uniquely named photoidentity_restore_* database.");

        if (!File.Exists(options.RestoreVerificationReportPath))
            throw new FileNotFoundException("The restore verification report does not exist.");

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(options.RestoreVerificationReportPath, token));
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("passed", out JsonElement passed) ||
            passed.ValueKind != JsonValueKind.True ||
            !TryString(root, "verificationDatabase", out string restoredName) ||
            !TryString(root, "sourceDatabase", out string sourceName) ||
            !restoredName.Equals(builder.Database, StringComparison.Ordinal) ||
            restoredName.Equals(sourceName, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Restore report is not a successful isolated verification for the selected database.");

        JsonElement evidence = root.GetProperty("restoredEvidence");
        if (evidence.GetProperty("schemaVersion").GetInt32() != PostgresCatalogueDatabase.CurrentSchemaVersion - 1)
            throw new InvalidOperationException(
                "The restored source must be schema v32 to verify the v32-to-v33 migration.");

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
        if (originalVersion != PostgresCatalogueDatabase.CurrentSchemaVersion - 1)
            throw new InvalidOperationException(
                "The selected rehearsal database is not at schema v32; nothing was changed.");

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
        // The existing migration history gains exactly one row for v33.
        Dictionary<string, long> expectedAfter = new(expectedCounts, StringComparer.Ordinal)
        {
            ["photo_identity_schema_migrations"] =
                expectedCounts["photo_identity_schema_migrations"] + 1
        };
        AssertCounts(expectedAfter, afterCounts, "post-migration preserved tables");

        if (await SchemaVersionAsync(beforeConnection, token) != PostgresCatalogueDatabase.CurrentSchemaVersion)
            throw new InvalidDataException("Rehearsal did not record schema v33.");

        await using NpgsqlCommand verify = beforeConnection.CreateCommand();
        verify.CommandText = """
            SELECT
                (SELECT count(*) FROM assets WHERE media_kind <> 'photo'),
                (SELECT count(*) FROM asset_revisions WHERE media_kind <> 'photo'),
                (SELECT count(*) FROM video_revision_metadata),
                (SELECT count(*) FROM video_still_posters),
                (SELECT count(*) FROM photo_identity_schema_migrations
                    WHERE version = 33 AND name = 'opt-in-video-revision-metadata-and-posters');
            """;
        await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) ||
            reader.GetInt64(0) != 0 || reader.GetInt64(1) != 0 ||
            reader.GetInt64(2) != 0 || reader.GetInt64(3) != 0 ||
            reader.GetInt64(4) != 1)
            throw new InvalidDataException(
                "Video/photo isolation or empty video-table verification failed.");

        output.WriteLine("Schema v32 -> v33 rehearsal passed on the isolated restored database.");
        output.WriteLine($"Rehearsal database: {restoredName}");
        output.WriteLine($"Unchanged catalogue tables verified: {expectedCounts.Count - 1}");
        output.WriteLine("All existing assets/revisions remain photo; video evidence tables are empty.");
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
