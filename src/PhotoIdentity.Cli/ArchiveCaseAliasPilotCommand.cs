using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Bounded operator-facing entrypoint to the already-guarded persistence
/// retire/restore service. Preview is the default; no media files are opened.
/// </summary>
public sealed record ArchiveCaseAliasPilotOptions(
    string ManifestPath,
    string MetadataPath,
    string ConnectionEnvironmentVariable,
    string DatabaseName,
    string Operation,
    int MaximumPairs,
    string? ApprovedManifestSha256,
    string? RestoreVerificationReportPath,
    string? ReceiptDirectory,
    string? Actor,
    bool ApplicationStopped,
    bool AcknowledgeReversibleRetirement)
{
    private static readonly Regex EnvironmentName = new(
        "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex DatabaseNamePattern = new(
        "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant);

    public static ArchiveCaseAliasPilotOptions Parse(string[] arguments)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool applicationStopped = false, acknowledged = false;
        for (int i = 0; i < arguments.Length; i++)
        {
            string arg = arguments[i];
            if (arg == "--application-stopped" && !applicationStopped)
            {
                applicationStopped = true;
                continue;
            }
            if (arg == "--acknowledge-reversible-retirement" && !acknowledged)
            {
                acknowledged = true;
                continue;
            }
            if (arg is not ("--manifest" or "--metadata" or "--postgres-connection-env"
                or "--database" or "--operation" or "--max-pairs"
                or "--approve-manifest-sha256" or "--restore-report"
                or "--receipt-directory" or "--actor")
                || i + 1 == arguments.Length
                || arguments[i + 1].StartsWith("--", StringComparison.Ordinal)
                || !values.TryAdd(arg, arguments[++i]))
            {
                throw new ArgumentException(
                    "Unknown, repeated, or incomplete archive alias pilot argument.");
            }
        }

        string Required(string key) => values.TryGetValue(key, out string? value)
            && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing {key}.");

        string operation = values.TryGetValue("--operation", out string? requested)
            ? requested : "preview";
        if (operation is not ("preview" or "apply" or "restore"))
            throw new ArgumentException("Operation must be preview, apply or restore.");

        int maximum = values.TryGetValue("--max-pairs", out string? rawLimit)
            ? int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && n is >= 1 and <= 5 ? n
                : throw new ArgumentException("--max-pairs must be 1..5.")
            : 5;

        string environment = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!EnvironmentName.IsMatch(environment) || !DatabaseNamePattern.IsMatch(database))
            throw new ArgumentException("Invalid explicit connection environment or database name.");

        string manifest = Path.GetFullPath(Required("--manifest"));
        string metadata = Path.GetFullPath(Required("--metadata"));
        if (manifest == metadata || !File.Exists(manifest) || !File.Exists(metadata))
            throw new ArgumentException("Both distinct private pilot files must exist.");

        string? approval = values.GetValueOrDefault("--approve-manifest-sha256");
        string? restoreReport = values.GetValueOrDefault("--restore-report");
        string? receiptDirectory = values.GetValueOrDefault("--receipt-directory");
        string? actor = values.GetValueOrDefault("--actor");
        bool write = operation is "apply" or "restore";
        if (write != acknowledged || write != applicationStopped
            || write != (approval is not null)
            || write != (receiptDirectory is not null)
            || write != (actor is not null)
            || (operation == "apply") != (restoreReport is not null))
        {
            throw new ArgumentException(
                "Write operations require explicit stopped-app acknowledgement, " +
                "the exact manifest SHA-256, actor, private receipt directory, and " +
                "(for apply) a passed isolated backup-restore report. Preview must omit all write approvals.");
        }
        if (write && (!Regex.IsMatch(approval!, "^[0-9a-f]{64}$")
            || string.IsNullOrWhiteSpace(actor)
            || actor!.Length > 200
            || !Path.IsPathFullyQualified(receiptDirectory!)))
            throw new ArgumentException("Write approval, actor or receipt-directory value is invalid.");

        return new(manifest, metadata, environment, database, operation,
            maximum, approval, restoreReport is null ? null : Path.GetFullPath(restoreReport),
            receiptDirectory is null ? null : Path.GetFullPath(receiptDirectory),
            actor, applicationStopped, acknowledged);
    }
}

public static class ArchiveCaseAliasPilotCommandRunner
{
    private sealed record PilotManifest(
        IReadOnlyList<ArchiveCaseAliasRetirementRequest> Pairs,
        string Sha256, DateTimeOffset GeneratedAtUtc);

    public static async Task<int> RunAsync(
        ArchiveCaseAliasPilotOptions options, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        PilotManifest manifest = ReadVerifiedManifest(options);
        bool writing = options.Operation is "apply" or "restore";
        string? originalConnection = Environment.GetEnvironmentVariable(
            options.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(originalConnection))
            throw new InvalidOperationException("The selected PostgreSQL environment variable is not set.");

        NpgsqlConnectionStringBuilder settings = new(originalConnection);
        if (settings.Host is not ("localhost" or "127.0.0.1" or "::1") ||
            !string.Equals(settings.Database, options.DatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Database connection must explicitly target the named localhost catalogue.");

        // A preview never initializes/migrates the catalogue or modifies rows.
        await using PostgresCatalogueDatabase catalogue = new(settings.ConnectionString);
        await VerifyDatabaseAsync(catalogue, options.DatabaseName, writing, cancellationToken);

        if (options.Operation == "preview")
        {
            await output.WriteLineAsync(
                $"PILOT PREVIEW: {manifest.Pairs.Count} pinned pairs; SHA-256 {manifest.Sha256}; " +
                "no database writes; no originals opened; no rows deleted.");
            return 0;
        }

        if (!string.Equals(options.ApprovedManifestSha256, manifest.Sha256,
            StringComparison.Ordinal))
            throw new InvalidOperationException("Explicit SHA-256 approval does not match the private pilot manifest.");
        if (options.Operation == "apply")
            RequireRecentRestoreVerification(options.RestoreVerificationReportPath!,
                options.DatabaseName);

        if (!Directory.Exists(options.ReceiptDirectory))
            throw new InvalidOperationException(
                "The private receipt directory must exist before approving writes.");

        // Create and flush the receipt header BEFORE any individual database write.
        // An interrupted batch leaves a durable identifier and each completed
        // operation can be replayed or restored idempotently.
        string receiptPath = Path.Combine(options.ReceiptDirectory!,
            $"archive-alias-{options.Operation}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}.jsonl");
        await using FileStream file = new(receiptPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough);
        await using StreamWriter receipt = new(file, new UTF8Encoding(false));
        await AppendReceiptAsync(receipt, new
        {
            schemaVersion = 1, status = "started", operation = options.Operation,
            sourceDatabase = options.DatabaseName, manifestSha256 = manifest.Sha256,
            pairCount = manifest.Pairs.Count, createdAtUtc = DateTimeOffset.UtcNow
        });

        PostgresArchiveCaseAliasRetirementService service = new(catalogue);
        int succeeded = 0, alreadyCompleted = 0;
        try
        {
            foreach (ArchiveCaseAliasRetirementRequest request in manifest.Pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Guard against a process starting between sequential pair writes.
                await VerifyDatabaseAsync(catalogue, options.DatabaseName, true, cancellationToken);
                ArchiveAliasRetirementOutcome outcome;
                try
                {
                    outcome = options.Operation == "apply"
                        ? await service.RetireAsync(request, cancellationToken)
                        : await service.RestoreAsync(request, cancellationToken);
                }
                catch (InvalidOperationException e) when (
                    options.Operation == "restore" &&
                    e.Message == "No retirement mapping exists for the secondary asset.")
                {
                    // A failed/partial apply may have left later candidates untouched.
                    await AppendReceiptAsync(receipt, new
                    {
                        status = "not-retired", request.SecondaryAssetId,
                        request.SurvivorAssetId, completedAtUtc = DateTimeOffset.UtcNow
                    });
                    continue;
                }

                if (outcome is ArchiveAliasRetirementOutcome.AlreadyRetired
                    or ArchiveAliasRetirementOutcome.AlreadyRestored)
                    alreadyCompleted++;
                else succeeded++;
                await AppendReceiptAsync(receipt, new
                {
                    status = outcome.ToString(), request.SecondaryAssetId,
                    request.SurvivorAssetId, completedAtUtc = DateTimeOffset.UtcNow
                });
            }
            await AppendReceiptAsync(receipt, new
            {
                status = "completed", succeeded, alreadyCompleted,
                completedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (Exception exception)
        {
            await AppendReceiptAsync(receipt, new
            {
                status = "failed-stop", succeeded, alreadyCompleted,
                errorType = exception.GetType().Name,
                completedAtUtc = DateTimeOffset.UtcNow
            });
            throw;
        }

        await output.WriteLineAsync(
            $"Pilot {options.Operation} completed: {succeeded} newly changed; " +
            $"{alreadyCompleted} already in requested state; {manifest.Pairs.Count} examined.");
        await output.WriteLineAsync($"Private receipt: {receiptPath}");
        return 0;
    }

    private static PilotManifest ReadVerifiedManifest(ArchiveCaseAliasPilotOptions options)
    {
        byte[] manifestBytes = File.ReadAllBytes(options.ManifestPath);
        string sha256 = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(options.MetadataPath));
        JsonElement meta = document.RootElement;
        if (meta.GetProperty("schemaVersion").GetInt32() != 1 ||
            meta.GetProperty("status").GetString() != "preview-only" ||
            meta.GetProperty("expectedDatabaseSchemaVersion").GetInt32() != 44 ||
            meta.GetProperty("applyEnabled").GetBoolean() ||
            !meta.GetProperty("approvalRequired").GetBoolean() ||
            meta.GetProperty("sourceFilesToDelete").GetInt32() != 0 ||
            meta.GetProperty("databaseRowsToDelete").GetInt32() != 0 ||
            meta.GetProperty("privatePilotManifestSha256").GetString() != sha256)
            throw new InvalidOperationException("Pilot metadata safety values or manifest fingerprint differ.");

        DateTimeOffset generatedAt = meta.GetProperty("generatedAtUtc")
            .GetDateTimeOffset().ToUniversalTime();
        if (options.Operation == "apply" &&
            (generatedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
             generatedAt < DateTimeOffset.UtcNow.AddHours(-4)))
            throw new InvalidOperationException(
                "Pilot preview is older than four hours. Refresh the two read-only audits and preview.");

        List<ArchiveCaseAliasRetirementRequest> requests = [];
        HashSet<Guid> uniqueSecondaries = [];
        HashSet<string> uniquePaths = new(StringComparer.Ordinal);
        using TextFieldParser parser = new(options.ManifestPath);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        string[] columns = parser.ReadFields()
            ?? throw new InvalidOperationException("Pilot manifest has no header.");
        string[] expected = [
            "SourceId", "NormalizedSourceKey", "SurvivorAssetId", "SurvivorSourceKey",
            "SurvivorCreatedAtUtc", "SurvivorVerifiedRevisionId",
            "SecondaryAssetId", "SecondarySourceKey", "SecondaryCreatedAtUtc",
            "SecondaryVerifiedRevisionId", "ExpectedSha256", "ExpectedSizeBytes",
            "SecondaryAutomaticReviewActions", "Action", "ApprovalRequired",
            "ApplyEnabled", "DeleteSourceOriginal", "DeleteDatabaseRows"
        ];
        if (!columns.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("Pilot manifest header is not the approved schema.");

        while (!parser.EndOfData)
        {
            string[] fields = parser.ReadFields()
                ?? throw new InvalidOperationException("Unexpected empty pilot manifest row.");
            if (fields.Length != expected.Length)
                throw new InvalidOperationException("Malformed pilot manifest row.");
            string Field(string name) => fields[Array.IndexOf(expected, name)];
            if (Field("Action") != "proposed-reversible-retirement" ||
                Field("ApprovalRequired") != "true" ||
                Field("ApplyEnabled") != "false" ||
                Field("DeleteSourceOriginal") != "false" ||
                Field("DeleteDatabaseRows") != "false")
                throw new InvalidOperationException("Pilot row has a write/delete authorization contradiction.");

            Guid ReadId(string field) => Guid.TryParse(Field(field), out Guid guid)
                && guid != Guid.Empty ? guid
                : throw new InvalidOperationException("Pilot row contains an invalid asset or source identifier.");
            DateTimeOffset ReadUtc(string field) => DateTimeOffset.TryParse(
                Field(field), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out DateTimeOffset date)
                ? date.ToUniversalTime() : throw new InvalidOperationException("Invalid pilot timestamp.");

            string normalized = Field("NormalizedSourceKey");
            string survivorKey = Field("SurvivorSourceKey");
            string secondaryKey = Field("SecondarySourceKey");
            static string Normalize(string s) => s.Replace('\\', '/').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) ||
                Normalize(survivorKey) != normalized ||
                Normalize(secondaryKey) != normalized ||
                survivorKey == secondaryKey)
                throw new InvalidOperationException("Pilot row does not represent an exact case-alias pair.");

            if (!long.TryParse(Field("ExpectedSizeBytes"), NumberStyles.None,
                CultureInfo.InvariantCulture, out long size) || size <= 0 ||
                !long.TryParse(Field("SecondaryAutomaticReviewActions"),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long actions) ||
                actions < 0 || !Regex.IsMatch(Field("ExpectedSha256"), "^[0-9a-f]{64}$"))
                throw new InvalidOperationException("Pilot row has invalid content or review evidence.");

            Guid source = ReadId("SourceId");
            Guid secondary = ReadId("SecondaryAssetId");
            if (!uniqueSecondaries.Add(secondary) ||
                !uniquePaths.Add($"{source:D}:{normalized}"))
                throw new InvalidOperationException("Pilot contains duplicate or overlapping assets.");

            requests.Add(new ArchiveCaseAliasRetirementRequest(
                source, ReadId("SurvivorAssetId"), secondary,
                survivorKey, secondaryKey,
                ReadId("SurvivorVerifiedRevisionId"),
                ReadId("SecondaryVerifiedRevisionId"),
                ReadUtc("SurvivorCreatedAtUtc"),
                ReadUtc("SecondaryCreatedAtUtc"),
                Field("ExpectedSha256"), size,
                options.Actor ?? "archive-alias:preview-only"));
        }

        if (requests.Count is < 1 or > 5 || requests.Count > options.MaximumPairs
            || requests.Count != meta.GetProperty("pilotPairs").GetInt32())
            throw new InvalidOperationException("Pilot manifest exceeds the approved bounded pair count.");

        return new PilotManifest(requests, sha256, generatedAt);
    }

    private static async Task VerifyDatabaseAsync(
        PostgresCatalogueDatabase catalogue, string expectedDatabase, bool requireStopped,
        CancellationToken token)
    {
        await using NpgsqlConnection connection = await catalogue.OpenConnectionAsync(token);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_database(),
                (SELECT COALESCE(MAX(version), 0)
                 FROM photo_identity_schema_migrations),
                (SELECT count(*) FROM pg_stat_activity
                 WHERE datname = current_database()
                   AND backend_type = 'client backend'
                   AND pid <> pg_backend_pid());
            """;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) ||
            reader.GetString(0) != expectedDatabase ||
            reader.GetInt32(1) != 44)
            throw new InvalidOperationException(
                "Selected catalogue is not the named schema-v44 database.");
        if (requireStopped && reader.GetInt64(2) != 0)
            throw new InvalidOperationException(
                "Other clients are connected to this database. Stop the app and retry.");
    }

    private static void RequireRecentRestoreVerification(
        string reportPath, string database)
    {
        if (!File.Exists(reportPath))
            throw new InvalidOperationException("The isolated backup-restore verification report is missing.");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(reportPath));
        JsonElement root = doc.RootElement;
        string? backupPath = root.GetProperty("backupPath").GetString();
        if (string.IsNullOrWhiteSpace(backupPath) || !Path.IsPathFullyQualified(backupPath)
            || !File.Exists(backupPath))
            throw new InvalidOperationException(
                "The restore-tested production backup file is missing. Refusing Apply.");
        if (!root.GetProperty("passed").GetBoolean() ||
            root.GetProperty("sourceDatabase").GetString() != database ||
            root.GetProperty("cleanupStatus").GetString() != "removed" ||
            root.GetProperty("cleanupRequired").GetBoolean() ||
            !Regex.IsMatch(root.GetProperty("backupSha256").GetString() ?? "",
                "^[0-9a-f]{64}$") ||
            root.GetProperty("capturedAtUtc").GetDateTimeOffset()
                < DateTimeOffset.UtcNow.AddHours(-24) ||
            root.GetProperty("capturedAtUtc").GetDateTimeOffset()
                > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidOperationException(
                "The isolated backup restore must have passed for this database within 24 hours, " +
                "with completed cleanup. Refresh the backup and verify its restore before Apply.");

    }

    private static async Task AppendReceiptAsync(StreamWriter receipt, object result)
    {
        await receipt.WriteLineAsync(JsonSerializer.Serialize(result));
        await receipt.FlushAsync();
        receipt.BaseStream.Flush(flushToDisk: true);
    }
}
