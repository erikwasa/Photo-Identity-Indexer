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
    DateTimeOffset? ReleaseRequestedAtUtc)
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
    DateTimeOffset? ReleaseRequestedAtUtc)
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
