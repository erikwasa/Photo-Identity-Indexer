using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record ArchiveSourceCatalogueScanDiagnostics(
    int MetadataReuseCount,
    TimeSpan BaselineReadElapsed,
    int HashedFileCount,
    long HashedBytes,
    TimeSpan HashingElapsed,
    int ObservationWriteCount,
    TimeSpan ObservationPersistenceElapsed,
    TimeSpan MissingReconciliationElapsed,
    TimeSpan TotalElapsed);

public sealed record ArchiveSourceCatalogueScanSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    int SupportedFileCount,
    int LocalFileCount,
    int OnlineOnlyFileCount,
    int DownloadingFileCount,
    int UnavailableFileCount,
    int AvailabilityErrorCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int VerifiedSourceCount,
    int NeedsSourceVerificationCount,
    int UnverifiedSourceCount,
    int MarkedDeletedCount,
    ArchiveSourceCatalogueScanDiagnostics Diagnostics);

/// <summary>
/// Catalogues permanent-archive presence and availability without opening OneDrive placeholders.
/// Lightweight size/last-write observations are retained for every item. A previously verified
/// local file reuses its immutable revision when size, last-write timestamp and media type still
/// match the verified baseline; otherwise local content is SHA-256 verified before persistence.
/// </summary>
public sealed class SqliteArchiveSourceCatalogueScanner
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteArchiveSourceObservationRepository _observations;
    private readonly SqliteArchiveSourceScanBatchRepository _scanBatch;

    public SqliteArchiveSourceCatalogueScanner(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _observations = new SqliteArchiveSourceObservationRepository(database);
        _scanBatch = new SqliteArchiveSourceScanBatchRepository(database);
    }

    public async Task<ArchiveSourceCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        CatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogueSource);
        ArgumentNullException.ThrowIfNull(options);

        Stopwatch totalStopwatch = Stopwatch.StartNew();
        await _observations.EnsureSchemaAsync(cancellationToken);
        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();

        Stopwatch baselineStopwatch = Stopwatch.StartNew();
        IReadOnlyDictionary<string, ArchiveSourceScanBaseline> baselines = await _scanBatch.GetBaselinesAsync(
            catalogueSource.Id,
            options.RelativeRoot,
            cancellationToken);
        baselineStopwatch.Stop();

        int supported = 0;
        int local = 0;
        int onlineOnly = 0;
        int downloading = 0;
        int unavailable = 0;
        int availabilityErrors = 0;
        int newRevisions = 0;
        int unchanged = 0;
        int verified = 0;
        int needsVerification = 0;
        int unverified = 0;
        int metadataReuse = 0;
        int hashedFiles = 0;
        long hashedBytes = 0;
        TimeSpan hashingElapsed = TimeSpan.Zero;
        List<ArchiveSourceScanWrite> writes = [];

        await foreach (SourceAsset sourceAsset in source.EnumerateAsync(options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceAsset.Reference.SourceId != catalogueSource.Id)
            {
                throw new InvalidOperationException(
                    "The source returned an asset owned by a different source identifier.");
            }

            supported++;
            switch (sourceAsset.Availability)
            {
                case AssetAvailability.Local:
                    local++;
                    break;
                case AssetAvailability.OnlineOnly:
                    onlineOnly++;
                    break;
                case AssetAvailability.Downloading:
                    downloading++;
                    break;
                case AssetAvailability.Unavailable:
                    unavailable++;
                    break;
                case AssetAvailability.Error:
                    availabilityErrors++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(sourceAsset.Availability),
                        sourceAsset.Availability,
                        "Unsupported archive availability state.");
            }

            Sha256Digest? contentHash = null;
            bool reuseVerifiedRevision = sourceAsset.Availability == AssetAvailability.Local &&
                baselines.TryGetValue(sourceAsset.Reference.ItemKey, out ArchiveSourceScanBaseline? baseline) &&
                baseline.CanReuseVerifiedRevision(sourceAsset);

            if (sourceAsset.Availability == AssetAvailability.Local && !reuseVerifiedRevision)
            {
                Stopwatch hashStopwatch = Stopwatch.StartNew();
                await using Stream content = await source.OpenContentAsync(
                    sourceAsset.Reference,
                    cancellationToken);
                byte[] hash = await SHA256.HashDataAsync(content, cancellationToken);
                hashStopwatch.Stop();
                hashingElapsed += hashStopwatch.Elapsed;
                hashedFiles++;
                hashedBytes += sourceAsset.SizeBytes;
                contentHash = new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
            }
            else if (reuseVerifiedRevision)
            {
                metadataReuse++;
            }

            writes.Add(new ArchiveSourceScanWrite(sourceAsset, contentHash));
        }

        Stopwatch persistenceStopwatch = Stopwatch.StartNew();
        IReadOnlyList<ArchiveSourceObservationWriteResult> results = await _scanBatch.RecordBatchAsync(
            catalogueSource,
            writes,
            baselines,
            scannedAt,
            cancellationToken);
        persistenceStopwatch.Stop();

        for (int index = 0; index < results.Count; index++)
        {
            ArchiveSourceObservationWriteResult result = results[index];
            ArchiveSourceScanWrite write = writes[index];
            if (write.SourceAsset.Availability == AssetAvailability.Local)
            {
                if (result.NewRevision)
                {
                    newRevisions++;
                }
                else
                {
                    unchanged++;
                }
            }

            switch (result.VerificationState)
            {
                case ArchiveSourceVerificationState.Verified:
                    verified++;
                    break;
                case ArchiveSourceVerificationState.NeedsSourceVerification:
                    needsVerification++;
                    break;
                case ArchiveSourceVerificationState.Unverified:
                    unverified++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        Stopwatch missingStopwatch = Stopwatch.StartNew();
        int deleted = await MarkMissingAssetsAsync(
            catalogueSource.Id,
            options.RelativeRoot,
            scannedAt,
            cancellationToken);
        missingStopwatch.Stop();
        totalStopwatch.Stop();

        ArchiveSourceCatalogueScanDiagnostics diagnostics = new(
            metadataReuse,
            baselineStopwatch.Elapsed,
            hashedFiles,
            hashedBytes,
            hashingElapsed,
            writes.Count,
            persistenceStopwatch.Elapsed,
            missingStopwatch.Elapsed,
            totalStopwatch.Elapsed);

        return new ArchiveSourceCatalogueScanSummary(
            catalogueSource.Id,
            scannedAt,
            supported,
            local,
            onlineOnly,
            downloading,
            unavailable,
            availabilityErrors,
            newRevisions,
            unchanged,
            verified,
            needsVerification,
            unverified,
            deleted,
            diagnostics);
    }

    private async Task<int> MarkMissingAssetsAsync(
        SourceId sourceId,
        string? relativeRoot,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken)
    {
        string scope = ArchiveCoverage.NormalizeRelativeFolder(relativeRoot ?? string.Empty);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = scope.Length == 0
            ? """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, $scanned_at_utc)
              WHERE source_id = $source_id
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> $scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """
            : """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, $scanned_at_utc)
              WHERE source_id = $source_id
                AND substr(source_key, 1, length($scope_prefix)) = $scope_prefix
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> $scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$scanned_at_utc", Format(scannedAtUtc));
        if (scope.Length > 0)
        {
            command.Parameters.AddWithValue("$scope_prefix", scope + "/");
        }

        int updated = await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        return updated;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
