using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>Operator-only registered v36 derivative maintenance and explicit requeue.</summary>
public sealed record VideoRegisteredDerivativeOptions(
    string Action, string ConnectionVariable, string DatabaseName, string? CacheRoot,
    int MaximumRecords, long? QuotaMiB, TimeSpan MinimumQuotaAge,
    bool VerifyHashes, Guid? JobId, bool Apply)
{
    public static VideoRegisteredDerivativeOptions Parse(string action, string[] args)
    {
        if (action is not ("maintain" or "requeue"))
            throw new ArgumentException("Unknown registered video derivative action.");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false, hashes = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            if (args[i] == "--verify-hashes" && !hashes) { hashes = true; continue; }
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                               "--cache-root" or "--max-records" or "--quota-mib" or
                               "--min-age-hours" or "--job-id") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid, repeated or incomplete registered derivative option.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Required registered derivative option is missing.");
        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid connection environment name or database name.");

        if (action == "requeue")
        {
            if (hashes || values.ContainsKey("--cache-root") ||
                values.ContainsKey("--max-records") || values.ContainsKey("--quota-mib") ||
                values.ContainsKey("--min-age-hours") ||
                !Guid.TryParse(Required("--job-id"), out Guid job) || job == Guid.Empty)
                throw new ArgumentException("Requeue requires only an explicit UUID and database.");
            return new(action, env, db, null, 0, null, TimeSpan.Zero,
                false, job, apply);
        }
        if (values.ContainsKey("--job-id"))
            throw new ArgumentException("Bulk maintenance does not accept a job ID.");
        string root = Required("--cache-root");
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("Registered derivative cache root must be absolute.");
        int Bounded(string key, int fallback, int min, int max)
        {
            if (!values.TryGetValue(key, out string? raw)) return fallback;
            if (!int.TryParse(raw, out int n) || n < min || n > max)
                throw new ArgumentException("Registered derivative limit exceeds the operator bounds.");
            return n;
        }
        int maximum = Bounded("--max-records", 16, 1, 32);
        long? quota = null;
        if (values.TryGetValue("--quota-mib", out string? rawQuota))
        {
            if (!long.TryParse(rawQuota, out long bytes) || bytes is < 64 or > 16384)
                throw new ArgumentException("Registered derivative quota must be 64..16384 MiB.");
            quota = bytes;
        }
        return new(action, env, db, Path.GetFullPath(root), maximum, quota,
            TimeSpan.FromHours(Bounded("--min-age-hours", 48, 24, 720)),
            hashes, null, apply);
    }
}

public static class VideoRegisteredDerivativeCommandRunner
{
    public static async Task<int> RunAsync(
        VideoRegisteredDerivativeOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Video derivative operator requires configured PostgreSQL.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Registered derivative operations require localhost PostgreSQL.");
        builder.Database = options.DatabaseName;

        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
        // No implicit schema upgrade through any maintenance operation.
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await check.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Video derivative maintenance requires the current schema; no upgrade attempted.");
        }

        var maintenance = new VideoRegisteredDerivativeMaintenance(database);
        if (options.Action == "requeue")
        {
            if (!options.Apply)
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    protocol = "wi0188-derivative-requeue-v1", dryRun = true,
                    changed = false
                }));
                return 0;
            }

            bool changed = await maintenance.RequeueRevokedAsync(options.JobId!.Value, token);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                protocol = "wi0188-derivative-requeue-v1", dryRun = false, changed
            }));
            return changed ? 0 : 1;
        }

        VideoRegisteredMaintenanceReport report = await maintenance.SweepAsync(
            options.CacheRoot!, options.MaximumRecords, options.QuotaMiB,
            options.MinimumQuotaAge, options.VerifyHashes, options.Apply, token);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-registered-derivative-maintenance-v1",
            report.DryRun, report.Inspected, report.Planned, report.Invalid,
            report.Missing, report.Corrupt, report.QuotaSelected, report.Revoked,
            report.FilesDeleted, report.DeleteFailures,
            report.RegisteredBytesBefore, report.ProjectedBytesAfter,
            report.MoreCandidates
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return report.DeleteFailures == 0 ? 0 : 1;
    }
}
