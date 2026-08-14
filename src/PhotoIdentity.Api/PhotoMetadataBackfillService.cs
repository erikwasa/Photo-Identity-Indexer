using System.Security.Cryptography;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

public sealed record PhotoMetadataBackfillReport(
    int Candidates,
    int Persisted,
    int DeferredNonLocal,
    int DeferredChanged,
    int DeferredUnavailable);

/// <summary>
/// Reads capture metadata only from source files that are already local and still match the
/// immutable revision fingerprint. The service never requests Files On-Demand hydration.
/// </summary>
public sealed class PhotoMetadataBackfillService
{
    private readonly SqliteAssetCatalogueRepository _catalogue;
    private readonly SqlitePhotoMetadataBackfillRepository _backfill;
    private readonly IOneDriveFilesOnDemandPlatform _filesOnDemand;
    private readonly IPhotoMetadataReader _metadataReader;
    private readonly TimeProvider _timeProvider;

    public PhotoMetadataBackfillService(
        SqliteAssetCatalogueRepository catalogue,
        SqlitePhotoMetadataBackfillRepository backfill,
        IOneDriveFilesOnDemandPlatform filesOnDemand,
        IPhotoMetadataReader metadataReader,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(backfill);
        ArgumentNullException.ThrowIfNull(filesOnDemand);
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _catalogue = catalogue;
        _backfill = backfill;
        _filesOnDemand = filesOnDemand;
        _metadataReader = metadataReader;
        _timeProvider = timeProvider;
    }

    public async Task<PhotoMetadataBackfillReport> ExecuteBatchAsync(
        int limit = 250,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PhotoMetadataBackfillCandidate> candidates =
            await _backfill.GetCandidatesAsync(limit, offset, cancellationToken);

        int persisted = 0;
        int deferredNonLocal = 0;
        int deferredChanged = 0;
        int deferredUnavailable = 0;

        foreach (PhotoMetadataBackfillCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path;
            try
            {
                path = ResolveSourcePath(candidate.RootLocator, candidate.SourceKey);
            }
            catch (ArgumentException)
            {
                deferredUnavailable++;
                continue;
            }

            OneDriveFilesOnDemandState state = _filesOnDemand.GetState(path);
            if (state.Availability != AssetAvailability.Local)
            {
                if (state.Availability is AssetAvailability.OnlineOnly or AssetAvailability.Downloading)
                {
                    deferredNonLocal++;
                }
                else
                {
                    deferredUnavailable++;
                }

                continue;
            }

            try
            {
                FileInfo file = new(path);
                if (!file.Exists || file.Length != candidate.SizeBytes)
                {
                    deferredChanged++;
                    continue;
                }

                await using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                using SHA256 sha256 = SHA256.Create();
                byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
                string actualHash = Convert.ToHexString(hash).ToLowerInvariant();
                if (!string.Equals(actualHash, candidate.ContentHash.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    deferredChanged++;
                    continue;
                }

                stream.Position = 0;
                PhotoCaptureMetadata metadata = await _metadataReader.ReadAsync(
                    stream,
                    candidate.MediaType,
                    cancellationToken);
                await _catalogue.SavePhotoMetadataAsync(
                    candidate.RevisionId,
                    metadata,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                persisted++;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
            {
                deferredUnavailable++;
            }
        }

        return new PhotoMetadataBackfillReport(
            candidates.Count,
            persisted,
            deferredNonLocal,
            deferredChanged,
            deferredUnavailable);
    }

    private static string ResolveSourcePath(string rootLocator, string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);

        string root = Path.GetFullPath(rootLocator);
        string platformKey = sourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(root, platformKey));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.Equals(root, comparison) && !resolved.StartsWith(rootPrefix, comparison))
        {
            throw new ArgumentException("The source item must remain inside the configured root.", nameof(sourceKey));
        }

        return resolved;
    }
}

public sealed class PhotoMetadataBackfillHostedService : BackgroundService
{
    private const int BatchSize = 250;
    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private readonly PhotoMetadataBackfillService _service;
    private readonly ILogger<PhotoMetadataBackfillHostedService> _logger;
    private int _offset;

    public PhotoMetadataBackfillHostedService(
        PhotoMetadataBackfillService service,
        ILogger<PhotoMetadataBackfillHostedService> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = RetryInterval;
            try
            {
                PhotoMetadataBackfillReport report = await _service.ExecuteBatchAsync(
                    BatchSize,
                    _offset,
                    stoppingToken);

                if (report.Persisted > 0)
                {
                    _offset = 0;
                    delay = ScanDelay;
                    _logger.LogInformation(
                        "Photo metadata backfill persisted {Persisted} of {Candidates} candidates; {DeferredNonLocal} non-local revisions were left untouched.",
                        report.Persisted,
                        report.Candidates,
                        report.DeferredNonLocal);
                }
                else if (report.Candidates > 0)
                {
                    _offset += report.Candidates;
                    delay = ScanDelay;
                }
                else
                {
                    _offset = 0;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Photo metadata backfill batch failed; it will be retried later.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }
}
