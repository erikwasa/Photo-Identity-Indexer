using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

public sealed record ArchiveHydrationPolicyConfiguration(
    long? MinimumFreeSpaceReserveBytes,
    long? MaximumManagedHydrationBytes,
    int? MaximumConcurrentOperations)
{
    public bool TryGetPolicy(out ArchiveHydrationPolicy? policy, out string? message)
    {
        policy = null;
        if (MinimumFreeSpaceReserveBytes is null ||
            MaximumManagedHydrationBytes is null ||
            MaximumConcurrentOperations is null)
        {
            message = "Managed archive hydration is disabled until minimum free-space reserve, maximum managed bytes and maximum concurrent operations are all configured.";
            return false;
        }

        if (MinimumFreeSpaceReserveBytes < 0 ||
            MaximumManagedHydrationBytes <= 0 ||
            MaximumConcurrentOperations <= 0)
        {
            message = "Archive hydration limits are invalid: reserve must be non-negative and managed bytes/concurrency must be positive.";
            return false;
        }

        policy = new ArchiveHydrationPolicy(
            MinimumFreeSpaceReserveBytes.Value,
            MaximumManagedHydrationBytes.Value,
            MaximumConcurrentOperations.Value);
        message = null;
        return true;
    }
}

public sealed record ArchiveHydrationPolicy(
    long MinimumFreeSpaceReserveBytes,
    long MaximumManagedHydrationBytes,
    int MaximumConcurrentOperations);

public sealed record ArchiveStorageSnapshot(
    bool ArchiveConfigured,
    bool PolicyConfigured,
    string? PolicyMessage,
    long? MinimumFreeSpaceReserveBytes,
    long? MaximumManagedHydrationBytes,
    int? MaximumConcurrentOperations,
    long LogicalSourceBytes,
    long? AvailableFreeBytes,
    long ManagedHydratedBytes,
    long ManagedDownloadingBytes,
    long ManagedReleasingBytes,
    long ManagedReservedBytes,
    int ActiveManagedOriginals,
    int HydrationsInProgress,
    long ReviewProxyBytes,
    string? ReviewProxyProfileId);

public sealed record ArchiveHydrationAdmission(
    bool Allowed,
    string? Message,
    long EvictionBytesRequested);

public interface IArchiveStorageProbe
{
    long GetAvailableFreeSpaceBytes(string path);
}

public sealed class DriveArchiveStorageProbe : IArchiveStorageProbe
{
    public long GetAvailableFreeSpaceBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new IOException("The archive storage volume could not be resolved.");
        }

        return new DriveInfo(root).AvailableFreeSpace;
    }
}

/// <summary>
/// Serializes managed-hydration admission, reserves whole logical source sizes, observes active
/// managed leases and requests LRU release when capacity is exhausted. OneDrive release is
/// asynchronous, so requested eviction is never counted as free capacity until it is observed.
/// </summary>
public sealed class ArchiveHydrationCapacityService
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteArchiveHydrationRepository _hydrations;
    private readonly SqliteArchiveStorageRepository _storage;
    private readonly IOneDriveFilesOnDemandPlatform _platform;
    private readonly IArchiveStorageProbe _probe;
    private readonly ArchiveHydrationPolicyConfiguration _configuration;
    private readonly ReviewProxyServingConfiguration _proxyConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _admissionGate = new(1, 1);
    private readonly SemaphoreSlim? _largeOperationGate;

    public ArchiveHydrationCapacityService(
        SqliteCatalogueDatabase database,
        SqliteArchiveHydrationRepository hydrations,
        SqliteArchiveStorageRepository storage,
        IOneDriveFilesOnDemandPlatform platform,
        IArchiveStorageProbe probe,
        ArchiveHydrationPolicyConfiguration configuration,
        ReviewProxyServingConfiguration proxyConfiguration,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(hydrations);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(proxyConfiguration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _hydrations = hydrations;
        _storage = storage;
        _platform = platform;
        _probe = probe;
        _configuration = configuration;
        _proxyConfiguration = proxyConfiguration;
        _timeProvider = timeProvider;
        if (configuration.TryGetPolicy(out ArchiveHydrationPolicy? policy, out _))
        {
            _largeOperationGate = new SemaphoreSlim(policy!.MaximumConcurrentOperations, policy.MaximumConcurrentOperations);
        }
    }

    public async Task<ArchiveHydrationAdmission> ExecuteHydrationAdmissionAsync(
        CatalogueProcessingAssetRevision revision,
        Func<Task> acceptedAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(acceptedAction);

        if (!_configuration.TryGetPolicy(out ArchiveHydrationPolicy? policy, out string? policyMessage) || policy is null)
        {
            return new ArchiveHydrationAdmission(false, policyMessage, 0L);
        }

        await _admissionGate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<ObservedManagedLease> observed = await ObserveActiveLeasesAsync(cancellationToken);
            int downloading = observed.Count(item =>
                !item.Lease.IsReleaseRequested && item.State.Availability == AssetAvailability.Downloading);
            if (downloading >= policy.MaximumConcurrentOperations)
            {
                return new ArchiveHydrationAdmission(
                    false,
                    $"Managed hydration is at its concurrency limit ({policy.MaximumConcurrentOperations}). Retry after an active hydration completes.",
                    0L);
            }

            long reservedBytes = observed
                .Where(item => item.State.Availability is AssetAvailability.Local or AssetAvailability.Downloading)
                .Sum(item => item.Lease.SizeBytes);
            long availableFreeBytes = _probe.GetAvailableFreeSpaceBytes(revision.RootLocator);
            bool exceedsManagedBudget = reservedBytes > policy.MaximumManagedHydrationBytes - revision.SizeBytes;
            bool crossesFreeReserve = availableFreeBytes < revision.SizeBytes ||
                availableFreeBytes - revision.SizeBytes < policy.MinimumFreeSpaceReserveBytes;

            if (exceedsManagedBudget || crossesFreeReserve)
            {
                long bytesToReclaimForBudget = exceedsManagedBudget
                    ? checked(reservedBytes + revision.SizeBytes - policy.MaximumManagedHydrationBytes)
                    : 0L;
                long bytesToReclaimForReserve = crossesFreeReserve
                    ? checked(policy.MinimumFreeSpaceReserveBytes + revision.SizeBytes - availableFreeBytes)
                    : 0L;
                long requested = await RequestLeastRecentlyNeededReleaseAsync(
                    observed,
                    revision.RevisionId,
                    Math.Max(bytesToReclaimForBudget, bytesToReclaimForReserve),
                    cancellationToken);
                string reason = exceedsManagedBudget && crossesFreeReserve
                    ? "managed byte budget and free-space reserve"
                    : exceedsManagedBudget ? "managed byte budget" : "free-space reserve";
                string suffix = requested > 0
                    ? $" Release was requested for {requested} managed byte(s); retry after OneDrive reports them online-only."
                    : " No releasable Photo-Identity-owned originals are currently available.";
                return new ArchiveHydrationAdmission(
                    false,
                    $"Hydration would violate the configured {reason}.{suffix}",
                    requested);
            }

            await acceptedAction();
            return new ArchiveHydrationAdmission(true, null, 0L);
        }
        finally
        {
            _admissionGate.Release();
        }
    }

    public async Task<T> RunLargeReadAsync<T>(
        bool managed,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!managed || _largeOperationGate is null)
        {
            return await operation();
        }

        await _largeOperationGate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            _largeOperationGate.Release();
        }
    }

    public Task TouchAsync(AssetRevisionId revisionId, CancellationToken cancellationToken = default) =>
        _hydrations.TouchAsync(revisionId, _timeProvider.GetUtcNow(), cancellationToken);

    public async Task<ArchiveStorageSnapshot> GetStorageSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        ArchiveCoverageConfiguration? coverage = await new SqliteArchiveCoverageRepository(_database)
            .GetAsync(cancellationToken);
        IReadOnlyList<ObservedManagedLease> observed = await ObserveActiveLeasesAsync(cancellationToken);
        bool policyConfigured = _configuration.TryGetPolicy(out ArchiveHydrationPolicy? policy, out string? policyMessage);

        long hydratedBytes = observed
            .Where(item => item.State.Availability == AssetAvailability.Local)
            .Sum(item => item.Lease.SizeBytes);
        long downloadingBytes = observed
            .Where(item => item.State.Availability == AssetAvailability.Downloading)
            .Sum(item => item.Lease.SizeBytes);
        long releasingBytes = observed
            .Where(item => item.Lease.IsReleaseRequested &&
                item.State.Availability is AssetAvailability.Local or AssetAvailability.Downloading)
            .Sum(item => item.Lease.SizeBytes);
        long reservedBytes = observed
            .Where(item => item.State.Availability is AssetAvailability.Local or AssetAvailability.Downloading)
            .Sum(item => item.Lease.SizeBytes);
        int hydrationCount = observed.Count(item =>
            !item.Lease.IsReleaseRequested && item.State.Availability == AssetAvailability.Downloading);

        long logicalBytes = coverage is null
            ? 0L
            : await _storage.GetCurrentLogicalSourceBytesAsync(coverage.Source.Id, cancellationToken);
        long proxyBytes = await _storage.GetReviewProxyBytesAsync(
            _proxyConfiguration.ProfileId,
            cancellationToken);
        long? availableFree = coverage is null
            ? null
            : _probe.GetAvailableFreeSpaceBytes(coverage.Source.RootLocator);

        return new ArchiveStorageSnapshot(
            coverage is not null,
            policyConfigured,
            policyMessage,
            policy?.MinimumFreeSpaceReserveBytes,
            policy?.MaximumManagedHydrationBytes,
            policy?.MaximumConcurrentOperations,
            logicalBytes,
            availableFree,
            hydratedBytes,
            downloadingBytes,
            releasingBytes,
            reservedBytes,
            observed.Count,
            hydrationCount,
            proxyBytes,
            _proxyConfiguration.ProfileId);
    }

    private async Task<IReadOnlyList<ObservedManagedLease>> ObserveActiveLeasesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ArchiveManagedHydrationLease> leases = await _hydrations.GetActiveLeasesAsync(cancellationToken);
        List<ObservedManagedLease> observed = [];
        foreach (ArchiveManagedHydrationLease lease in leases)
        {
            string? path = TryResolvePath(lease.RootLocator, lease.SourceKey);
            OneDriveFilesOnDemandState state = path is null
                ? new OneDriveFilesOnDemandState(AssetAvailability.Unavailable, false, false)
                : _platform.GetState(path);
            if (lease.IsReleaseRequested && state.Availability == AssetAvailability.OnlineOnly)
            {
                await _hydrations.MarkReleasedAsync(lease.AssetRevisionId, _timeProvider.GetUtcNow(), cancellationToken);
                continue;
            }

            observed.Add(new ObservedManagedLease(lease, path, state));
        }

        return observed;
    }

    private async Task<long> RequestLeastRecentlyNeededReleaseAsync(
        IReadOnlyList<ObservedManagedLease> observed,
        AssetRevisionId excludedRevisionId,
        long bytesToReclaim,
        CancellationToken cancellationToken)
    {
        if (bytesToReclaim <= 0)
        {
            return 0L;
        }

        long requested = 0L;
        foreach (ObservedManagedLease candidate in observed
            .Where(item =>
                item.Lease.AssetRevisionId != excludedRevisionId &&
                !item.Lease.IsReleaseRequested &&
                item.State.Availability == AssetAvailability.Local &&
                item.Path is not null)
            .OrderBy(item => item.Lease.LastNeededAtUtc)
            .ThenBy(item => item.Lease.AssetRevisionId.ToString(), StringComparer.Ordinal))
        {
            await _platform.RequestOnlineOnlyAsync(candidate.Path!, cancellationToken);
            await _hydrations.MarkReleaseRequestedAsync(
                candidate.Lease.AssetRevisionId,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            requested = checked(requested + candidate.Lease.SizeBytes);
            if (requested >= bytesToReclaim)
            {
                break;
            }
        }

        return requested;
    }

    private static string? TryResolvePath(string rootLocator, string sourceKey)
    {
        try
        {
            string root = Path.GetFullPath(rootLocator);
            string relative = sourceKey
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return path.StartsWith(prefix, comparison) ? path : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private sealed record ObservedManagedLease(
        ArchiveManagedHydrationLease Lease,
        string? Path,
        OneDriveFilesOnDemandState State);
}
