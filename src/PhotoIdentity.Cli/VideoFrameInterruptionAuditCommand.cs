using System.Data;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

public sealed record VideoFrameInterruptionAuditOptions(
    string ConnectionEnv, string Database, Guid FrameJobId, string ScratchRoot)
{
    public static VideoFrameInterruptionAuditOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                "--frame-job-id" or "--scratch-root") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--",
                    StringComparison.Ordinal) || !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid or repeated interruption audit input.");
        }
        string Need(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException(
                    "An explicit frame job, local DB and owned scratch are required.");
        string env = Need("--postgres-connection-env");
        string database = Need("--database");
        string root = Need("--scratch-root");
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                env, "^[A-Za-z_][A-Za-z0-9_]*$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                database, "^[a-z][a-z0-9_]{0,62}$") ||
            !Guid.TryParse(Need("--frame-job-id"), out Guid job) || job == Guid.Empty ||
            !Path.IsPathFullyQualified(root) ||
            OperatingSystem.IsWindows() && root.StartsWith(@"\\",
                StringComparison.Ordinal) || values.Count != 4)
            throw new ArgumentException("Interruption audit arguments are invalid.");
        return new(env, database, job, Path.GetFullPath(root));
    }
}

public sealed record VideoFrameInterruptionSnapshot(
    string Status, string DerivativeKind, bool LeaseActive,
    bool CancellationRequested, bool BatchExists, long FrameRows,
    bool ControlEnabled, bool ControlPaused, bool AnyJobSelected,
    bool ThisJobSelected, bool SelectedGrantActive);

public sealed record VideoFrameInterruptionAssessment(
    bool NoPartialEvidence, bool GrantFenced, bool LeaseFenced,
    bool ControlDisarmed, bool ScratchClean, bool RecoveryComplete,
    string ReasonCode)
{
    public static VideoFrameInterruptionAssessment Assess(
        VideoFrameInterruptionSnapshot? state,
        VideoFrameScratchRecoveryResult scratch)
    {
        if (state is null)
            return new(false, false, false, false, false, false, "job-not-found");
        if (state.DerivativeKind != "FrameEvidence")
            return new(false, false, false, false, false, false, "wrong-job-kind");
        // No batch/rows is necessary for an intentionally interrupted
        // *unpublished* job. A succeeded job is not an interruption test.
        bool unpublished = !state.BatchExists && state.FrameRows == 0 &&
            state.Status != "succeeded";
        bool grantFenced = !state.SelectedGrantActive;
        bool leaseFenced = !state.LeaseActive;
        bool disarmed = !state.ControlEnabled && state.ControlPaused &&
            !state.AnyJobSelected;
        bool scratchClean = scratch.OwnedWorkspaces == 0 &&
            scratch.RefusedWorkspaces == 0 && !scratch.Blocked;
        bool done = unpublished && grantFenced && leaseFenced &&
            disarmed && scratchClean &&
            state.Status is "cancelled" or "failed";
        string reason = !unpublished ? "published-or-partial-evidence" :
            !grantFenced ? "grant-still-active" :
            !leaseFenced ? "lease-still-active" :
            !disarmed ? "control-not-disarmed" :
            !scratchClean ? "scratch-not-clean" :
            state.Status is not ("cancelled" or "failed")
                ? "job-not-terminal" : "interruption-recovered";
        return new(unpublished, grantFenced, leaseFenced,
            disarmed, scratchClean, done, reason);
    }
}

/// <summary>
/// Metadata-only after-the-fact check; does not disarm controls, recover
/// leases, delete temporary files, fetch captions/embeddings or contact
/// private media or cloud services. Repeat after the standard operator
/// control/lease recovery and scratch cleanup procedures.
/// </summary>
public static class VideoFrameInterruptionAuditCommandRunner
{
    public static async Task<int> RunAsync(
        VideoFrameInterruptionAuditOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Frame interruption audit requires Windows-owned scratch.");
        VideoFrameScratchRecoveryResult scratch =
            VideoFrameScratchRecovery.InspectAndRecover(
                options.ScratchRoot, apply: false, DateTimeOffset.UtcNow);

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("A local PostgreSQL connection variable is required.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Interruption audit requires loopback PostgreSQL.");
        builder.Database = options.Database;

        await using PostgresCatalogueDatabase db = new(builder.ConnectionString);
        await using NpgsqlConnection connection = await db.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, token);
        await using (NpgsqlCommand ro = connection.CreateCommand())
        {
            ro.Transaction = tx;
            ro.CommandText = "SET TRANSACTION READ ONLY;";
            await ro.ExecuteNonQueryAsync(token);
        }
        await using (NpgsqlCommand version = connection.CreateCommand())
        {
            version.Transaction = tx;
            version.CommandText =
                "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
            if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "An already-rehearsed current local schema is required.");
        }

        VideoFrameInterruptionSnapshot? state = null;
        await using (NpgsqlCommand query = connection.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = """
                SELECT j.status, j.derivative_kind,
                       COALESCE(j.leased_until_utc > now(),false),
                       j.cancellation_requested_at_utc IS NOT NULL,
                       b.job_id IS NOT NULL,
                       (SELECT COUNT(*) FROM video_frame_evidence_frames frame
                        WHERE frame.job_id=j.id),
                       c.enabled, c.paused,
                       c.selected_job_id IS NOT NULL,
                       COALESCE(c.selected_job_id=j.id,false),
                       COALESCE(c.selected_job_id=j.id
                         AND c.authorization_expires_at_utc>now(),false)
                FROM video_processing_jobs j
                CROSS JOIN video_processing_control c
                LEFT JOIN video_frame_evidence_batches b ON b.job_id=j.id
                WHERE j.id=@job AND c.singleton_id=1;
                """;
            query.Parameters.AddWithValue("job", options.FrameJobId);
            await using NpgsqlDataReader reader =
                await query.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
                state = new(reader.GetString(0), reader.GetString(1),
                    reader.GetBoolean(2), reader.GetBoolean(3),
                    reader.GetBoolean(4), reader.GetInt64(5),
                    reader.GetBoolean(6), reader.GetBoolean(7),
                    reader.GetBoolean(8), reader.GetBoolean(9),
                    reader.GetBoolean(10));
        }
        await tx.CommitAsync(token);
        VideoFrameInterruptionAssessment result =
            VideoFrameInterruptionAssessment.Assess(state, scratch);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-frame-interruption-audit-v1",
            readOnly = true, jobFound = state is not null,
            jobStatus = state?.Status ?? "not-found",
            state?.LeaseActive, state?.CancellationRequested,
            state?.BatchExists, durableFrames = state?.FrameRows ?? 0,
            state?.ControlEnabled, state?.ControlPaused,
            state?.AnyJobSelected, state?.ThisJobSelected,
            state?.SelectedGrantActive,
            scratchWorkspaces = scratch.OwnedWorkspaces,
            unknownScratchEntries = scratch.RefusedWorkspaces,
            scratchActive = scratch.ActiveWorkspaces,
            scratchYoung = scratch.YoungWorkspaces,
            scratchRecoverable = scratch.RecoverableWorkspaces,
            result.NoPartialEvidence, result.GrantFenced,
            result.LeaseFenced, result.ControlDisarmed,
            result.ScratchClean, result.RecoveryComplete,
            result.ReasonCode,
            originalsOpened = false, oneDriveHydrationRequested = false,
            captionsReturned = false, embeddingsReturned = false,
            videoJobsEnqueued = false, schemaChanged = false,
            controlChanged = false, scratchDeleted = false,
            cloudInvoked = false
        }));
        return result.RecoveryComplete ? 0 : 1;
    }
}
