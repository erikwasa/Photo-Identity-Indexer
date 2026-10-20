using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Dedicated, dry-run-first CLI for private v36 uncommitted artifact recovery.
/// No root initialization, no schema upgrades, no original media access.
/// </summary>
public sealed record VideoDerivativeRecoveryOptions(
    string ConnectionVariable, string DatabaseName, string CacheRoot,
    TimeSpan MinimumAge, int MaximumFiles, bool Apply)
{
    public static VideoDerivativeRecoveryOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply)
            {
                apply = true;
                continue;
            }
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                               "--cache-root" or "--min-age-hours" or "--max-files") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid or repeated video derivative cleanup option.");
        }

        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Required video derivative cleanup option is missing.");
        string variable = Required("--postgres-connection-env");
        string db = Required("--database");
        string root = Required("--cache-root");
        if (!Regex.IsMatch(variable, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Path.IsPathFullyQualified(root))
            throw new ArgumentException("Specify a safe environment name, database and absolute local cache root.");

        int ParseRange(string key, int fallback, int min, int max)
        {
            if (!values.TryGetValue(key, out string? value)) return fallback;
            if (!int.TryParse(value, out int number) || number < min || number > max)
                throw new ArgumentException("Video derivative cleanup limit is outside the safe range.");
            return number;
        }

        return new(variable, db, Path.GetFullPath(root),
            TimeSpan.FromHours(ParseRange("--min-age-hours", 72, 48, 720)),
            ParseRange("--max-files", 32, 1, 64), apply);
    }
}

public static class VideoDerivativeRecoveryCommandRunner
{
    public static async Task<int> RunAsync(
        VideoDerivativeRecoveryOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Video derivative recovery requires configured PostgreSQL.");

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new ArgumentException("Video derivative recovery requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;

        await using PostgresCatalogueDatabase db = new(builder.ConnectionString);
        // A maintenance command must never advance schema implicitly.
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlCommand check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await check.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Video derivative recovery requires an already-upgraded catalogue; no migration attempted.");
        }

        VideoDerivativeRecoveryReport report =
            await new VideoDerivativeRecoveryJanitor(db).SweepAsync(
                options.CacheRoot, options.MinimumAge, options.MaximumFiles,
                options.Apply, token);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-derivative-recovery-v1",
            dryRun = report.DryRun,
            report.Considered,
            report.Eligible,
            report.Deleted,
            report.Protected,
            report.Refused,
            report.InvalidatedRegistered
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return 0;
    }
}
