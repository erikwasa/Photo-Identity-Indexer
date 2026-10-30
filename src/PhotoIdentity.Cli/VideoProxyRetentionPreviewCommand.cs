using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>Operator-only, read-only WI-0189 registered proxy retention preview.</summary>
public sealed record VideoProxyRetentionPreviewOptions(
    string ConnectionVariable, string DatabaseName, string CacheRoot,
    long QuotaMiB, long MaximumProxyMiB, long IncomingMiB,
    int MaximumRecords, TimeSpan MinimumAge)
{
    public static VideoProxyRetentionPreviewOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                "--cache-root" or "--quota-mib" or "--max-proxy-mib" or
                "--incoming-mib" or "--max-records" or "--min-age-hours") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException(
                    "Invalid, repeated or incomplete video retention preview option.");
        }

        string Required(string name) =>
            values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Required video retention preview option is missing.");

        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        string root = Required("--cache-root");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Path.IsPathFullyQualified(root))
            throw new ArgumentException(
                "Video retention preview requires a valid database, environment variable and absolute cache root.");

        long Bounded(string name, long fallback, long minimum, long maximum)
        {
            if (!values.TryGetValue(name, out string? raw)) return fallback;
            if (!long.TryParse(raw, out long result) ||
                result < minimum || result > maximum)
                throw new ArgumentException("Video retention preview budget exceeds its bounds.");
            return result;
        }

        long quota = Bounded("--quota-mib", 1024, 64, 16384);
        long maxProxy = Bounded("--max-proxy-mib", 256, 1, 256);
        long incoming = Bounded("--incoming-mib", 256, 1, 256);
        if (maxProxy > quota)
            throw new ArgumentException("Proxy size limit cannot exceed cache quota.");

        return new(env, db, Path.GetFullPath(root),
            quota, maxProxy, incoming,
            (int)Bounded("--max-records", 1024, 1, 2048),
            TimeSpan.FromHours(Bounded("--min-age-hours", 48, 24, 720)));
    }
}

public static class VideoProxyRetentionPreviewCommandRunner
{
    public static async Task<int> RunAsync(
        VideoProxyRetentionPreviewOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Video retention preview requires configured PostgreSQL.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Video retention preview requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;

        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
        // Read-only operators must never upgrade a production database.
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await check.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Video retention preview requires current schema; no migration attempted.");
        }

        var limits = new VideoProxyRetentionLimits(
            checked(options.QuotaMiB * 1024 * 1024),
            checked(options.MaximumProxyMiB * 1024 * 1024));
        VideoProxyRetentionPreviewReport report =
            await new PostgresVideoProxyRetentionPreview(database).PreviewAsync(
                options.CacheRoot, limits,
                checked(options.IncomingMiB * 1024 * 1024),
                options.MinimumAge, options.MaximumRecords, token);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0189-registered-proxy-retention-preview-v1",
            report.DryRun, report.Admitted, report.Reason,
            report.RegisteredRecords, report.PinnedRecords,
            report.RegisteredBytesBefore, report.IncomingReservedBytes,
            report.PlannedEvictions, report.PlannedReclaimedBytes,
            report.ProjectedRegisteredBytes,
            registeredOnly = true,
            filesystemQuotaEnforced = false
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return report.Admitted ? 0 : 1;
    }
}
