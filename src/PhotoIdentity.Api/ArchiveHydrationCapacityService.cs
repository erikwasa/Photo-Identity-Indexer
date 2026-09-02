using PhotoIdentity.Core.Catalogue;
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

public sealed record ArchiveHydrationSetAdmission(
    bool Allowed,
    bool WaitingForRelease,
    string? Message,
    long RequiredAdditionalBytes,
    long AvailableManagedCapacity,
    long EvictionBytesRequested,
    int MaximumConcurrentOperations);

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
/// Serializes all Photo-Identity-managed archive hydration admission. Both revision-level original
/// access and pre-revision source verification share one budget/gate, so first-time online-only
/// files cannot bypass the same reserve, byte and concurrency limits.
/// </summary>
public sealed class ArchiveHydrationCapacityService
{
    private readonly IArchiveHydrationRepository _hydrations;
    private readonly IArchiveSourceHydrationRepository _sourceHydrations;
    private readonly IArchiveCoverageRepository _coverage;
    private readonly IArchiveStorageAccountingRepository _storage;
    private readonly IArchiveAvailabilityRepository _availability;
    private readonly IOneDriveFilesOnDemandPlatform _platform;
    private readonly IArchiveStorageProbe _probe;
    private readonly ArchiveHydrationPolicyConfiguration _configuration;
    private readonly ReviewProxyServingConfiguration _proxyConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly SlideshowOriginalLeaseRegistry _slideshowLeases;
    private readonly SemaphoreSlim _admissionGate = new(1, 1);
    private readonly SemaphoreSlim? _largeOperationGate;

    public ArchiveHydrationCapacityService(
        IArchiveHydrationRepository hydrations,
        IArchiveSourceHydrationRepository sourceHydrations,
        IArchiveCoverageRepository coverage,
        IArchiveStorageAccountingRepository storage,
        IArchiveAvailabilityRepository availability,
        IOneDriveFilesOnDemandPlatform platform,
        IArchiveStorageProbe probe,
        ArchiveHydrationPolicyConfiguration configuration,
        ReviewProxyServingConfiguration proxyConfiguration,
        TimeProvider timeProvider,
        SlideshowOriginalLeaseRegistry? slideshowLeases = null)
    {
        ArgumentNullException.ThrowIfNull(hydrations);
        ArgumentNullException.ThrowIfNull(sourceHydrations);
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(proxyConfiguration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _hydrations = hydrations;
        _sourceHydrations = sourceHydrations;
        _coverage = coverage;
        _storage = storage;
        _availability = availability;
        _platform = platform;
        _probe = probe;
        _configuration = configuration;
        _proxyConfiguration = proxyConfiguration;
        _timeProvider = timeProvider;
        _slideshowLeases = slideshowLeases ?? new SlideshowOriginalLeaseRegistry(timeProvider);
        if (configuration.TryGetPolicy(out ArchiveHydrationPolicy? policy, out _))
        {
            _largeOperationGate = new SemaphoreSlim(policy!.MaximumConcurrentOperations, policy.MaximumConcurrentOperations);
        }
    }

    public Task<ArchiveHydrationAdmission> ExecuteHydrationAdmissionAsync(
        IAssetRevisionStorageDescriptor revision,
        Func<Task> acceptedAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return ExecuteAdmissionAsync(
            revision.SizeBytes,
            revision.RootLocator,
            RevisionKey(revision.RevisionId),
            acceptedAction,
            cancellationToken);
    }

    public Task<ArchiveHydrationAdmission> ExecuteSourceHydrationAdmissionAsync(
        ArchiveSourceObservation source,
        Func<Task> acceptedAction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ExecuteAdmissionAsync(
            source.ObservedSizeBytes,
            source.RootLocator,
            SourceKey(source.AssetId),
            acceptedAction,
            cancellationToken);
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

    public Task TouchSourceAsync(AssetId assetId, CancellationToken cancellationToken = default) =>
        _sourceHydrations.TouchAsync(assetId, _timeProvider.GetUtcNow(), cancellationToken);

    public async Task<ArchiveHydrationSetAdmission> PreflightHydrationSetAsync(
        IReadOnlyCollection<IAssetRevisionStorageDescriptor> revisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisions);

        if (!_configuration.TryGetPolicy(out ArchiveHydrationPolicy? policy, out string? policyMessage) ||
            policy is null)
        {
            return new ArchiveHydrationSetAdmission(
                false,
                false,
                policyMessage,
                0L,
                0L,
                0L,
                0);
        }

        IAssetRevisionStorageDescriptor[] requested = revisions
            .DistinctBy(revision => revision.RevisionId)
            .ToArray();
        if (requested.Length == 0)
        {
            return new ArchiveHydrationSetAdmission(
                true,
                false,
                null,
                0L,
                policy.MaximumManagedHydrationBytes,
                0L,
                policy.MaximumConcurrentOperations);
        }

        await _admissionGate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<ObservedManagedLease> observed = await ObserveActiveLeasesAsync(cancellationToken);
            long reservedBytes = observed
                .Where(item => item.State.Availability is AssetAvailability.Local or AssetAvailability.Downloading)
                .Sum(item => item.Lease.SizeBytes);

            long additionalBytes = 0L;
            string? storageRoot = null;
            foreach (IAssetRevisionStorageDescriptor revision in requested)
            {
                string? path = TryResolvePath(revision.RootLocator, revision.SourceKey);
                OneDriveFilesOnDemandState state = path is null
                    ? new OneDriveFilesOnDemandState(AssetAvailability.Unavailable, false, false)
                    : _platform.GetState(path);

                if (state.Availability == AssetAvailability.OnlineOnly)
                {
                    additionalBytes = checked(additionalBytes + revision.SizeBytes);
                    storageRoot ??= revision.RootLocator;
                }
            }

            long availableManagedCapacity = Math.Max(
                0L,
                policy.MaximumManagedHydrationBytes - reservedBytes);
            if (additionalBytes == 0)
            {
                return new ArchiveHydrationSetAdmission(
                    true,
                    false,
                    null,
                    0L,
                    availableManagedCapacity,
                    0L,
                    policy.MaximumConcurrentOperations);
            }

            storageRoot ??= requested[0].RootLocator;
            long availableFreeBytes = _probe.GetAvailableFreeSpaceBytes(storageRoot);
            long bytesToReclaimForBudget = Math.Max(
                0L,
                checked(reservedBytes + additionalBytes - policy.MaximumManagedHydrationBytes));
            long freeCapacityAboveReserve = Math.Max(
                0L,
                availableFreeBytes - policy.MinimumFreeSpaceReserveBytes);
            long bytesToReclaimForReserve = Math.Max(
                0L,
                checked(additionalBytes - freeCapacityAboveReserve));
            long bytesToReclaim = Math.Max(bytesToReclaimForBudget, bytesToReclaimForReserve);

            if (bytesToReclaim == 0)
            {
                return new ArchiveHydrationSetAdmission(
                    true,
                    false,
                    null,
                    additionalBytes,
                    availableManagedCapacity,
                    0L,
                    policy.MaximumConcurrentOperations);
            }

            long pendingReleaseBytes = observed
                .Where(item =>
                    item.Lease.IsReleaseRequested &&
                    item.State.Availability is AssetAvailability.Local or AssetAvailability.Downloading)
                .Sum(item => item.Lease.SizeBytes);
            long newlyNeeded = Math.Max(0L, bytesToReclaim - pendingReleaseBytes);
            long newlyRequested = newlyNeeded == 0
                ? 0L
                : await RequestLeastRecentlyNeededReleaseAsync(
                    observed,
                    excludedKey: null,
                    newlyNeeded,
                    cancellationToken);

            long expectedReclaim = checked(pendingReleaseBytes + newlyRequested);
            if (expectedReclaim >= bytesToReclaim)
            {
                return new ArchiveHydrationSetAdmission(
                    false,
                    true,
                    "Storage is being freed for best-quality slideshow preparation. Preparation will continue after OneDrive reports the requested releases online-only.",
                    additionalBytes,
                    availableManagedCapacity,
                    newlyRequested,
                    policy.MaximumConcurrentOperations);
            }

            return new ArchiveHydrationSetAdmission(
                false,
                false,
                "Best-quality slideshow cannot prepare all originals under the current storage policy.",
                additionalBytes,
                availableManagedCapacity,
                newlyRequested,
                policy.MaximumConcurrentOperations);
        }
        finally
        {
            _admissionGate.Release();
        }
    }

    public async Task<ArchiveStorageSnapshot> GetStorageSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        ArchiveCoverageState? coverage =
            await _coverage.GetAsync(cancellationToken);
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
            : await _storage.GetCurrentLogicalSourceBytesAsync(
                coverage.Source.SourceId,
                cancellationToken);
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

    private async Task<ArchiveHydrationAdmission> ExecuteAdmissionAsync(
        long requestedSizeBytes,
        string rootLocator,
        string excludedKey,
        Func<Task> acceptedAction,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestedSizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
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
            long availableFreeBytes = _probe.GetAvailableFreeSpaceBytes(rootLocator);
            bool exceedsManagedBudget = reservedBytes > policy.MaximumManagedHydrationBytes - requestedSizeBytes;
            bool crossesFreeReserve = availableFreeBytes < requestedSizeBytes ||
                availableFreeBytes - requestedSizeBytes < policy.MinimumFreeSpaceReserveBytes;

            if (exceedsManagedBudget || crossesFreeReserve)
            {
                long bytesToReclaimForBudget = exceedsManagedBudget
                    ? checked(reservedBytes + requestedSizeBytes - policy.MaximumManagedHydrationBytes)
                    : 0L;
                long bytesToReclaimForReserve = crossesFreeReserve
                    ? checked(policy.MinimumFreeSpaceReserveBytes + requestedSizeBytes - availableFreeBytes)
                    : 0L;
                long requested = await RequestLeastRecentlyNeededReleaseAsync(
                    observed,
                    excludedKey,
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

    private async Task<IReadOnlyList<ObservedManagedLease>> ObserveActiveLeasesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ArchiveManagedHydrationLeaseState> revisionLeases = await _hydrations.GetActiveLeasesAsync(cancellationToken);
        IReadOnlyList<ArchiveManagedSourceHydrationLeaseState> sourceLeases = await _sourceHydrations.GetActiveLeasesAsync(cancellationToken);
        List<ObservedManagedLease> observed = [];

        foreach (ArchiveManagedHydrationLeaseState lease in revisionLeases)
        {
            ManagedLease normalized = new(
                RevisionKey(lease.AssetRevisionId),
                lease.SizeBytes,
                lease.RootLocator,
                lease.SourceKey,
                lease.LastNeededAtUtc,
                lease.IsReleaseRequested,
                lease.AssetRevisionId,
                lease.AssetId);
            if (await ObserveLeaseAsync(normalized, cancellationToken) is ObservedManagedLease value)
            {
                observed.Add(value);
            }
        }

        foreach (ArchiveManagedSourceHydrationLeaseState lease in sourceLeases)
        {
            ManagedLease normalized = new(
                SourceKey(lease.AssetId),
                lease.SizeBytes,
                lease.RootLocator,
                lease.SourceKey,
                lease.LastNeededAtUtc,
                lease.IsReleaseRequested,
                null,
                lease.AssetId);
            if (await ObserveLeaseAsync(normalized, cancellationToken) is ObservedManagedLease value)
            {
                observed.Add(value);
            }
        }

        return observed;
    }

    private async Task<ObservedManagedLease?> ObserveLeaseAsync(
        ManagedLease lease,
        CancellationToken cancellationToken)
    {
        string? path = TryResolvePath(lease.RootLocator, lease.SourceKey);
        OneDriveFilesOnDemandState state = path is null
            ? new OneDriveFilesOnDemandState(AssetAvailability.Unavailable, false, false)
            : _platform.GetState(path);
        if (lease.IsReleaseRequested && state.Availability == AssetAvailability.OnlineOnly)
        {
            DateTimeOffset observedAt = _timeProvider.GetUtcNow();

            // Persist observed availability before closing the managed lease. If the process stops
            // between the two writes, the still-active release can be observed and reconciled again.
            await _availability.RecordAsync(
                lease.AssetId,
                AssetAvailability.OnlineOnly,
                observedAt,
                cancellationToken);

            if (lease.RevisionId is AssetRevisionId revisionId)
            {
                await _hydrations.MarkReleasedAsync(revisionId, observedAt, cancellationToken);
            }
            else if (lease.SourceAssetId is AssetId sourceAssetId)
            {
                await _sourceHydrations.MarkReleasedAsync(sourceAssetId, observedAt, cancellationToken);
            }

            return null;
        }

        return new ObservedManagedLease(lease, path, state);
    }

    private async Task<long> RequestLeastRecentlyNeededReleaseAsync(
        IReadOnlyList<ObservedManagedLease> observed,
        string? excludedKey,
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
                (excludedKey is null ||
                    !string.Equals(item.Lease.Key, excludedKey, StringComparison.Ordinal)) &&
                !_slideshowLeases.IsProtected(item.Lease.RevisionId, item.Lease.AssetId) &&
                !item.Lease.IsReleaseRequested &&
                item.State.Availability == AssetAvailability.Local &&
                item.Path is not null)
            .OrderBy(item => item.Lease.LastNeededAtUtc)
            .ThenBy(item => item.Lease.Key, StringComparer.Ordinal))
        {
            await _platform.RequestOnlineOnlyAsync(candidate.Path!, cancellationToken);
            if (candidate.Lease.RevisionId is AssetRevisionId revisionId)
            {
                await _hydrations.MarkReleaseRequestedAsync(
                    revisionId,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            else if (candidate.Lease.SourceAssetId is AssetId assetId)
            {
                await _sourceHydrations.MarkReleaseRequestedAsync(
                    assetId,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }

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

    private static string RevisionKey(AssetRevisionId revisionId) => $"revision:{revisionId}";
    private static string SourceKey(AssetId assetId) => $"source:{assetId}";

    private sealed record ManagedLease(
        string Key,
        long SizeBytes,
        string RootLocator,
        string SourceKey,
        DateTimeOffset LastNeededAtUtc,
        bool IsReleaseRequested,
        AssetRevisionId? RevisionId,
        AssetId AssetId)
    {
        public AssetId? SourceAssetId => RevisionId is null ? AssetId : null;
    }

    private sealed record ObservedManagedLease(
        ManagedLease Lease,
        string? Path,
        OneDriveFilesOnDemandState State);
}
