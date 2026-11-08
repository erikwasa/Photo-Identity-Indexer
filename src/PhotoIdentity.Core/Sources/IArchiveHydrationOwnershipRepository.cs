using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

public sealed record ArchiveManagedHydrationState(
    AssetRevisionId AssetRevisionId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReleaseRequestedAtUtc,
    DateTimeOffset? ReleasedAtUtc)
{
    public bool IsActive => ReleasedAtUtc is null;
    public bool IsReleaseRequested =>
        IsActive && ReleaseRequestedAtUtc is not null;
}

public sealed record ArchiveManagedHydrationLeaseState(
    AssetRevisionId AssetRevisionId,
    AssetId AssetId,
    long SizeBytes,
    string RootLocator,
    string SourceKey,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset LastNeededAtUtc,
    DateTimeOffset? ReleaseRequestedAtUtc,
    bool IsVideo = false)
{
    public bool IsReleaseRequested =>
        ReleaseRequestedAtUtc is not null;
}

public sealed record ArchiveManagedSourceHydrationState(
    AssetId AssetId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReleaseRequestedAtUtc,
    DateTimeOffset? ReleasedAtUtc)
{
    public bool IsActive => ReleasedAtUtc is null;
    public bool IsReleaseRequested =>
        IsActive && ReleaseRequestedAtUtc is not null;
}

public sealed record ArchiveManagedSourceHydrationLeaseState(
    AssetId AssetId,
    long SizeBytes,
    string RootLocator,
    string SourceKey,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset LastNeededAtUtc,
    DateTimeOffset? ReleaseRequestedAtUtc,
    bool IsVideo = false)
{
    public bool IsReleaseRequested =>
        ReleaseRequestedAtUtc is not null;
}

/// <summary>
/// Tracks only revision hydration explicitly initiated by Photo Identity.
/// </summary>
public interface IArchiveHydrationRepository
{
    Task<ArchiveManagedHydrationState?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveManagedHydrationLeaseState>> GetActiveLeasesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded read of durable, previously requested video release receipts.
    /// Fallback keeps test/alternate persistence implementations compatible.
    /// PostgreSQL overrides this with a database-side LIMIT so operator
    /// Archive advances do not scan every managed photo hydration.
    /// This must not issue any OneDrive operation.
    /// </summary>
    async Task<IReadOnlyList<ArchiveManagedHydrationLeaseState>>
        GetPendingVideoReleasesAsync(
            int maximum,
            CancellationToken cancellationToken = default)
    {
        if (maximum is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        return (await GetActiveLeasesAsync(cancellationToken))
            .Where(item => item.IsVideo && item.IsReleaseRequested)
            .Take(maximum)
            .ToArray();
    }

    Task<ArchiveManagedHydrationState> ClaimAsync(
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    Task TouchAsync(
        AssetRevisionId revisionId,
        DateTimeOffset neededAtUtc,
        CancellationToken cancellationToken = default);

    Task<ArchiveManagedHydrationState> MarkReleaseRequestedAsync(
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return up to the requested limit of current, unexcluded app-managed
    /// videos no longer needed by queued/running derivative work.
    /// </summary>
    Task<IReadOnlyList<ArchiveManagedHydrationLeaseState>>
        GetIdleVideoHydrationsAsync(
            int maximum, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArchiveManagedHydrationLeaseState>>([]);

    /// <summary>
    /// Gate an app-owned release under the same source row lock used when
    /// new video jobs are enqueued. Once granted, subsequent enqueue must
    /// reject an outstanding release until the original is rehydrated.
    /// </summary>
    Task<bool> TryRequestIdleVideoReleaseAsync(
        AssetRevisionId revisionId, DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Check that a previously requested video release is still authorized
    /// and has no queued/running derivative dependencies before retry.
    /// </summary>
    Task<bool> CanRetryVideoReleaseAsync(
        AssetRevisionId revisionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Atomically confirm an already requested video release, but never
    /// close a lease whose release request was cleared or superseded.
    /// Implementations without atomic support fail closed.
    /// </summary>
    Task<bool> TryMarkVideoReleaseConfirmedAsync(
        AssetRevisionId revisionId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    Task MarkReleasedAsync(
        AssetRevisionId revisionId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tracks temporary Photo-Identity hydration ownership before immutable revision identity is known.
/// </summary>
public interface IArchiveSourceHydrationRepository
{
    Task<ArchiveManagedSourceHydrationState?> GetAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveManagedSourceHydrationLeaseState>> GetActiveLeasesAsync(
        CancellationToken cancellationToken = default);

    Task<ArchiveManagedSourceHydrationState> ClaimAsync(
        AssetId assetId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    Task TouchAsync(
        AssetId assetId,
        DateTimeOffset neededAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkReleaseRequestedAsync(
        AssetId assetId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkReleasedAsync(
        AssetId assetId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TransferToRevisionAsync(
        AssetId assetId,
        AssetRevisionId revisionId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Atomically transfers active hydration ownership back to source identity before re-verification.
/// </summary>
public interface IArchiveHydrationIdentityTransferRepository
{
    Task<bool> MoveActiveRevisionLeaseToSourceAsync(
        AssetId assetId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> MoveRevisionLeaseToSourceAsync(
        AssetRevisionId revisionId,
        AssetId assetId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default);
}
