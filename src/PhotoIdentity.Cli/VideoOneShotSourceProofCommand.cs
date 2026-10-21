using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// Explicit, read-only, exact-revision source-integrity proof. An operator
/// MUST supply --read-local-bytes to authorize opening the already-local
/// original with a no-recall handle. No arbitrary source paths, writes,
/// enqueue, FFmpeg, workers, or derivative publication are supported.
/// </summary>
public sealed record VideoOneShotSourceProofOptions(
    string ConnectionEnv, string DatabaseName, Guid RevisionId,
    bool ReadLocalBytes)
{
    public static VideoOneShotSourceProofOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool read = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--read-local-bytes" && !read)
            {
                read = true;
                continue;
            }
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                                "--revision-id") ||
                i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException(
                    "Source proof accepts only a revision UUID, local DB and explicit --read-local-bytes.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Source integrity proof requires all explicit fields.");
        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$",
                RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$",
                RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--revision-id"), out Guid revision) ||
            revision == Guid.Empty)
            throw new ArgumentException("Invalid exact revision or local PostgreSQL reference.");
        return new(env, db, revision, read);
    }
}

public sealed record VideoOneShotSourceProofResult(
    bool Verified, bool? OriginalOpened, bool SourceMetadataStable,
    string ReasonCode)
{
    public static VideoOneShotSourceProofResult Success() =>
        new(true, true, true, "verified");
    public static VideoOneShotSourceProofResult Fail(bool? opened, string reason, bool stable = false) =>
        new(false, opened, stable, reason);
}

public static class VideoOneShotSourceProofCommandRunner
{
    private const long MaximumBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan MaximumReadTime = TimeSpan.FromMinutes(3);

    public static async Task<int> RunAsync(
        VideoOneShotSourceProofOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        if (!options.ReadLocalBytes)
        {
            Write(output, options.ReadLocalBytes, null);
            return 0;
        }
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("No-recall source proof requires Windows.");

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Source integrity proof needs a configured PostgreSQL variable.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Source integrity proof requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;

        await using PostgresCatalogueDatabase db = new(builder.ConnectionString);
        Guid sourceId = Guid.Empty;
        string? sourceRoot = null, sourceKey = null, expectedSha = null;
        long expectedBytes = 0;

        // This explicit proof never calls schema initialization, and uses
        // a PostgreSQL read-only transaction for metadata authority.
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token))
        {
            await using (NpgsqlCommand readOnly = connection.CreateCommand())
            {
                readOnly.Transaction = transaction;
                readOnly.CommandText = "SET TRANSACTION READ ONLY;";
                await readOnly.ExecuteNonQueryAsync(token);
            }
            await using (NpgsqlCommand version = connection.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText = """
                    SELECT COALESCE(MAX(version),0)
                    FROM photo_identity_schema_migrations;
                    """;
                if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                    PostgresCatalogueDatabase.CurrentSchemaVersion)
                    throw new InvalidOperationException(
                        "Source integrity proof requires already-migrated schema v37.");
            }
            await using (NpgsqlCommand query = connection.CreateCommand())
            {
                query.Transaction = transaction;
                query.CommandText = """
                    SELECT source.id, source.root_locator, asset.source_key,
                           revision.content_sha256, revision.size_bytes
                    FROM asset_revisions revision
                    JOIN assets asset ON asset.id=revision.asset_id
                    JOIN sources source ON source.id=asset.source_id
                    JOIN video_revision_metadata metadata
                      ON metadata.asset_revision_id=revision.id
                    WHERE revision.id=@revision
                      AND revision.media_kind='video' AND asset.media_kind='video'
                      AND asset.deleted_at_utc IS NULL
                      AND source.kind='local-folder'
                      AND metadata.inspection_status='supported'
                      AND metadata.color_transfer='bt709'
                      AND revision.size_bytes BETWEEN 1 AND @maximum
                      AND revision.id=(
                        SELECT latest.id FROM asset_revisions latest
                        WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                        ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                      AND NOT EXISTS (
                        SELECT 1 FROM source_copy_exclusions excluded
                        WHERE excluded.source_id=asset.source_id
                          AND excluded.source_key=asset.source_key);
                    """;
                query.Parameters.AddWithValue("revision", options.RevisionId);
                query.Parameters.AddWithValue("maximum", MaximumBytes);
                await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
                if (await reader.ReadAsync(token))
                {
                    sourceId = reader.GetGuid(0);
                    sourceRoot = reader.GetString(1);
                    sourceKey = reader.GetString(2);
                    expectedSha = reader.GetString(3);
                    expectedBytes = reader.GetInt64(4);
                }
            }
            await transaction.CommitAsync(token);
        }

        if (sourceId == Guid.Empty || sourceRoot is null || sourceKey is null ||
            expectedSha is null || !IsApprovedDigest(expectedSha))
        {
            Write(output, true, VideoOneShotSourceProofResult.Fail(false, "revision-not-eligible"));
            return 1;
        }

        VideoOneShotSourceProofResult result;
        using CancellationTokenSource bounded =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(MaximumReadTime);
        try
        {
            result = await VerifyLocalFileAsync(
                sourceId, sourceRoot, sourceKey, expectedSha, expectedBytes,
                bounded.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            result = VideoOneShotSourceProofResult.Fail(null, "timeout");
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or
                     ArgumentException or System.Security.SecurityException)
        {
            // Never expose untrusted .Message/paths or Windows exceptions.
            result = VideoOneShotSourceProofResult.Fail(null, "source-unavailable");
        }
        Write(output, true, result);
        return result.Verified ? 0 : 1;
    }

    public static bool IsApprovedDigest(string digest) =>
        digest.Length == 64 &&
        digest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static async Task<VideoOneShotSourceProofResult> VerifyLocalFileAsync(
        Guid sourceId, string sourceRoot, string sourceKey,
        string expectedSha, long expectedBytes, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(sourceRoot) ||
            sourceRoot.StartsWith(@"\\", StringComparison.Ordinal) ||
            expectedBytes is < 1 or > MaximumBytes)
            return VideoOneShotSourceProofResult.Fail(false, "source-unavailable");

        var source = new OneDriveVideoAssetSource(
            SourceId.From(sourceId), sourceRoot);
        var reference = new SourceAssetReference(source.SourceId, sourceKey);
        if (await source.GetAvailabilityAsync(reference, token) != AssetAvailability.Local)
            return VideoOneShotSourceProofResult.Fail(false, "source-not-local");

        string path = source.GetLocalPath(reference);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            return VideoOneShotSourceProofResult.Fail(false, "source-unavailable");
        for (string? parent = Path.GetDirectoryName(path); parent is not null;
             parent = Path.GetDirectoryName(parent))
        {
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                return VideoOneShotSourceProofResult.Fail(false, "source-unavailable");
            if (parent == Path.GetPathRoot(parent)) break;
        }
        FileInfo file = new(path);
        if (file.Length != expectedBytes)
            return VideoOneShotSourceProofResult.Fail(false, "source-changed");
        DateTime before = file.LastWriteTimeUtc;
        long readBytes = 0;
        string actual;
        // All reads use the same FILE_FLAG_OPEN_NO_RECALL handle as normal
        // lease-scoped staging. The hash loop reads no more than 256 MiB
        // even if an untrusted source unexpectedly grows after stat.
        await using (FileStream stream = OneDriveVideoNoRecallReader.OpenRead(path))
        using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            byte[] buffer = new byte[65536];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int amount = await stream.ReadAsync(buffer, token);
                if (amount == 0) break;
                readBytes += amount;
                if (readBytes > expectedBytes)
                    return VideoOneShotSourceProofResult.Fail(true, "source-changed");
                hash.AppendData(buffer.AsSpan(0, amount));
            }
            actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        file.Refresh();
        if (!file.Exists || file.Length != expectedBytes ||
            file.LastWriteTimeUtc != before || readBytes != expectedBytes ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            await source.GetAvailabilityAsync(reference, token) != AssetAvailability.Local)
            return VideoOneShotSourceProofResult.Fail(true, "source-changed");

        if (!string.Equals(actual, expectedSha, StringComparison.Ordinal))
            return VideoOneShotSourceProofResult.Fail(true, "digest-mismatch", stable: true);

        return VideoOneShotSourceProofResult.Success();
    }

    public static void Write(
        TextWriter output, bool requested, VideoOneShotSourceProofResult? result)
    {
        ArgumentNullException.ThrowIfNull(output);
        string reason = result?.ReasonCode ?? "not-run";
        if (reason is not (
            "not-run" or "verified" or "revision-not-eligible" or
            "source-unavailable" or "source-not-local" or
            "source-changed" or "digest-mismatch" or "timeout"))
            throw new ArgumentException("Unrecognized source proof reason code.");
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-one-shot-source-proof-v1",
            dryRun = !requested,
            explicitLocalReadRequested = requested,
            status = reason,
            revisionDigestMatched = result?.Verified,
            originalBytesOpened = requested ? result?.OriginalOpened : false,
            sourceMetadataStable = result?.SourceMetadataStable,
            originalWriteAccessRequested = false,
            oneDriveHydrationRequested = false,
            jobsEnqueued = false,
            videoWorkersActivated = false,
            derivativesPublished = false,
            originalPostTrialIntegrityVerified = false,
            realVideoExecutionAuthorized = false
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
