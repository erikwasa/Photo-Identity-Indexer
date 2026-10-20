using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Explicit operator-only queue inspection / request for cancellation. This
/// command NEVER enqueues, claims, processes, hydrates or publishes video.
/// A cancellation write requires --apply, while status is always read-only.
/// </summary>
public sealed record VideoJobsCommandOptions(
    string Action, string ConnectionEnvironmentVariable, string DatabaseName,
    Guid? JobId, bool Apply)
{
    public static VideoJobsCommandOptions Parse(string action, string[] args)
    {
        if (action is not ("status" or "cancel" or "pause" or "resume" or "stop" or "recover"))
            throw new ArgumentException("Unknown video jobs action.");

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply)
            {
                apply = true;
                continue;
            }
            string key = args[i];
            if (key is not ("--postgres-connection-env" or "--database" or "--job-id") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key, args[++i]))
                throw new ArgumentException("Unexpected, duplicate or incomplete video jobs option.");
        }

        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException($"Missing required {key}.");
        string variable = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(variable, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Specify a valid environment variable and explicit database name.");

        if (action == "status")
        {
            if (apply || values.ContainsKey("--job-id"))
                throw new ArgumentException("Video jobs status is read-only; --apply and --job-id are not allowed.");
            return new(action, variable, database, null, false);
        }

        if (action is "pause" or "resume" or "stop" or "recover")
        {
            if (values.ContainsKey("--job-id"))
                throw new ArgumentException("Global video lane operations do not accept --job-id.");
            return new(action, variable, database, null, apply);
        }

        if (!Guid.TryParse(Required("--job-id"), out Guid job) || job == Guid.Empty)
            throw new ArgumentException("--job-id must be a non-empty UUID.");
        return new(action, variable, database, job, apply);
    }
}

public static class VideoJobsCommandRunner
{
    public static async Task<int> RunAsync(
        VideoJobsCommandOptions options, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("PostgreSQL connection environment variable is missing.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Video job administration requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;

        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
        // Do not implicitly migrate an older database while inspecting the queue.
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken))
        await using (NpgsqlCommand version = connection.CreateCommand())
        {
            version.CommandText = """
                SELECT COALESCE(MAX(version), 0)
                FROM photo_identity_schema_migrations;
                """;
            int actual = Convert.ToInt32(await version.ExecuteScalarAsync(cancellationToken));
            if (actual != PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Video job control requires the current PostgreSQL schema; do not upgrade through this command.");
        }

        if (options.Action == "status")
        {
            PostgresVideoProcessingQueueInspector inspector = new(database);
            VideoProcessingQueueOverview overview =
                await inspector.GetOverviewAsync(cancellationToken);
            IReadOnlyList<VideoProcessingJobOperatorRow> recent =
                await inspector.ListRecentAsync(20, cancellationToken);
            VideoPhotoPrioritySnapshot photoPriority =
                await new PostgresVideoPhotoPriorityProbe(database)
                    .GetAsync(cancellationToken);
            VideoProcessingControlState control =
                await new PostgresVideoProcessingControlRepository(database)
                    .GetAsync(cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                protocol = "wi0188-video-jobs-operator-status-v1",
                schemaVersion = PostgresCatalogueDatabase.CurrentSchemaVersion,
                executionEnabled = false, // no background worker is registered
                photoPriority,
                control, overview, recent
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }

        if (options.Action == "recover")
        {
            // Stale leases must also be recoverable while a lane is paused
            // or stopped, as no hosted worker is registered to clean them up.
            // Status/preview remains read-only; recovery requires --apply.
            VideoProcessingQueueOverview before =
                await new PostgresVideoProcessingQueueInspector(database)
                    .GetOverviewAsync(cancellationToken);
            if (!options.Apply)
            {
                output.WriteLine("Dry run: no expired video leases were changed. Add --apply to recover.");
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    protocol = "wi0188-video-lease-recovery-v1",
                    dryRun = true, visibleExpiredLeases = before.ExpiredLeases
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return 0;
            }

            int changed = await new PostgresVideoProcessingJobRepository(database)
                .RecoverExpiredAsync(cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                protocol = "wi0188-video-lease-recovery-v1",
                dryRun = false, recovered = changed
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }

        if (options.Action is "pause" or "resume" or "stop")
        {
            var control = new PostgresVideoProcessingControlRepository(database);
            VideoProcessingControlState currentControl = await control.GetAsync(cancellationToken);
            if (!options.Apply)
            {
                output.WriteLine("Dry run: no video lane control was changed. Add --apply to request the action.");
                output.WriteLine(JsonSerializer.Serialize(currentControl,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return 0;
            }

            if (options.Action == "stop")
            {
                VideoStopResult stopped = await control.StopAsync(cancellationToken);
                output.WriteLine(JsonSerializer.Serialize(stopped,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return 0;
            }

            VideoProcessingControlState updated = options.Action == "pause"
                ? await control.PauseAsync(cancellationToken)
                : await control.ResumeAsync(cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(updated,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }

        if (options.Action != "cancel" || options.JobId is not Guid jobId)
            throw new InvalidOperationException("No valid video job action is selected.");

        var queue = new PostgresVideoProcessingJobRepository(database);
        VideoProcessingJobState? current = await queue.GetAsync(jobId, cancellationToken);
        if (current is null)
        {
            output.WriteLine("Video job not found or no longer visible.");
            return 1;
        }

        if (!options.Apply)
        {
            output.WriteLine("Dry run: no cancellation requested. Add --apply to cancel the specified job.");
            output.WriteLine("Current status: " + current.Status);
            return 0;
        }

        bool changed = await queue.RequestCancellationAsync(jobId, cancellationToken);
        output.WriteLine(changed
            ? "Cancellation requested; active work must still acknowledge or expire its lease."
            : "Cancellation not applied; job was already terminal or changed.");
        return changed ? 0 : 1;
    }
}
