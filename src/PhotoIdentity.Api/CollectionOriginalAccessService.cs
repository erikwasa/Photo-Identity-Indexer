using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

public sealed record CollectionOriginalAccessSnapshot(
    AssetRevisionId RevisionId,
    string State,
    bool ManagedHydration,
    bool IsPinned,
    bool CanRequestHydration,
    bool CanView,
    bool CanRelease,
    string? Message);

public sealed record VerifiedCollectionOriginal(
    FileStream Stream,
    string ContentType);

public sealed class CollectionOriginalAccessService
{
    public const string ReadyState = "ready";
    public const string OnlineOnlyState = "online-only";
    public const string DownloadingState = "downloading";
    public const string ReleasingState = "releasing";
    public const string HashMismatchState = "hash-mismatch";
    public const string UnavailableState = "unavailable";
    public const string ErrorState = "error";

    private readonly SqliteLocalBatchRepository _catalogue;
    private readonly SqliteArchiveHydrationRepository _hydrations;
    private readonly IOneDriveFilesOnDemandPlatform _platform;
    private readonly ArchiveHydrationCapacityService _capacity;
    private readonly TimeProvider _timeProvider;
    private readonly StringComparison _pathComparison;

    public CollectionOriginalAccessService(
        SqliteLocalBatchRepository catalogue,
        SqliteArchiveHydrationRepository hydrations,
        IOneDriveFilesOnDemandPlatform platform,
        ArchiveHydrationCapacityService capacity,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(hydrations);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _catalogue = catalogue;
        _hydrations = hydrations;
        _platform = platform;
        _capacity = capacity;
        _timeProvider = timeProvider;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public async Task<CollectionOriginalAccessSnapshot?> GetStatusAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        ResolvedOriginal? resolved = await ResolveAsync(revisionId, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        ArchiveManagedHydrationRecord? ownership = await _hydrations.GetAsync(revisionId, cancellationToken);
        OneDriveFilesOnDemandState platformState = _platform.GetState(resolved.Path);

        if (ownership is { IsActive: true, IsReleaseRequested: true } &&
            platformState.Availability == AssetAvailability.OnlineOnly)
        {
            await _hydrations.MarkReleasedAsync(
                revisionId,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            ownership = await _hydrations.GetAsync(revisionId, cancellationToken);
        }

        bool managed = ownership is { IsActive: true };
        bool releasing = ownership is { IsReleaseRequested: true };
        return platformState.Availability switch
        {
            AssetAvailability.Local when releasing => Snapshot(
                revisionId, ReleasingState, managed, platformState.IsPinned,
                canHydrate: false, canView: false, canRelease: true,
                "Photo Identity requested release and is waiting for OneDrive to make the original online-only."),
            AssetAvailability.Local => await LocalSnapshotAsync(
                resolved, managed, platformState.IsPinned, cancellationToken),
            AssetAvailability.OnlineOnly => Snapshot(
                revisionId, OnlineOnlyState, managed, platformState.IsPinned,
                canHydrate: true, canView: false, canRelease: false,
                managed
                    ? "The managed original is online-only. Hydration can be requested again subject to the storage policy."
                    : "The original is online-only. Normal browsing can continue from its review proxy."),
            AssetAvailability.Downloading when releasing => Snapshot(
                revisionId, ReleasingState, managed, platformState.IsPinned,
                canHydrate: false, canView: false, canRelease: true,
                "Photo Identity requested release and is waiting for OneDrive."),
            AssetAvailability.Downloading => Snapshot(
                revisionId, DownloadingState, managed, platformState.IsPinned,
                canHydrate: false, canView: false, canRelease: managed,
                managed
                    ? "Photo Identity requested hydration and is waiting for the original to become local."
                    : "The original is already being made local outside Photo Identity; it will not be claimed or automatically released."),
            AssetAvailability.Unavailable => Snapshot(
                revisionId, UnavailableState, managed, platformState.IsPinned,
                false, false, managed,
                "The authoritative original is unavailable at its catalogued location."),
            AssetAvailability.Error => Snapshot(
                revisionId, ErrorState, managed, platformState.IsPinned,
                false, false, managed,
                "OneDrive availability could not be determined."),
            _ => Snapshot(
                revisionId, ErrorState, managed, platformState.IsPinned,
                false, false, managed,
                "The authoritative original has an unsupported availability state."),
        };
    }

    public async Task<CollectionOriginalAccessSnapshot?> RequestHydrationAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        ResolvedOriginal? resolved = await ResolveAsync(revisionId, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        ArchiveManagedHydrationRecord? ownership = await _hydrations.GetAsync(revisionId, cancellationToken);
        if (ownership is { IsActive: true, IsReleaseRequested: true })
        {
            throw new InvalidOperationException(
                "The original is currently being released. Wait for it to become online-only before hydrating it again.");
        }

        OneDriveFilesOnDemandState state = _platform.GetState(resolved.Path);
        if (state.Availability == AssetAvailability.OnlineOnly)
        {
            ArchiveHydrationAdmission admission = await _capacity.ExecuteHydrationAdmissionAsync(
                resolved.Revision,
                async () =>
                {
                    // Claim ownership only after Windows accepts our explicit pin request. A crash
                    // between these operations leaks local storage rather than risking release of
                    // content Photo Identity did not hydrate.
                    await _platform.RequestHydrationAsync(resolved.Path, cancellationToken);
                    if (ownership is not { IsActive: true })
                    {
                        await _hydrations.ClaimAsync(
                            revisionId,
                            _timeProvider.GetUtcNow(),
                            cancellationToken);
                    }
                    else
                    {
                        await _capacity.TouchAsync(revisionId, cancellationToken);
                    }
                },
                cancellationToken);
            if (!admission.Allowed)
            {
                throw new InvalidOperationException(admission.Message ?? "Managed hydration is blocked by the configured storage policy.");
            }
        }
        else if (state.Availability == AssetAvailability.Unavailable)
        {
            throw new FileNotFoundException("The authoritative original is unavailable.");
        }
        else if (state.Availability == AssetAvailability.Error)
        {
            throw new IOException("OneDrive availability could not be determined.");
        }

        // Local or already-downloading originals are deliberately not claimed. That preserves
        // content which was local or user-pinned before Photo Identity became involved.
        return await GetStatusAsync(revisionId, cancellationToken);
    }

    public async Task<CollectionOriginalAccessSnapshot?> RequestReleaseAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        ResolvedOriginal? resolved = await ResolveAsync(revisionId, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        ArchiveManagedHydrationRecord? ownership = await _hydrations.GetAsync(revisionId, cancellationToken);
        if (ownership is not { IsActive: true })
        {
            throw new InvalidOperationException(
                "The original was not hydrated by Photo Identity and cannot be released automatically.");
        }

        OneDriveFilesOnDemandState state = _platform.GetState(resolved.Path);
        if (state.Availability == AssetAvailability.OnlineOnly)
        {
            await _hydrations.MarkReleasedAsync(
                revisionId,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            return await GetStatusAsync(revisionId, cancellationToken);
        }

        if (state.Availability == AssetAvailability.Unavailable)
        {
            throw new FileNotFoundException("The authoritative original is unavailable.");
        }

        if (state.Availability == AssetAvailability.Error)
        {
            throw new IOException("OneDrive availability could not be determined.");
        }

        if (!ownership.IsReleaseRequested)
        {
            await _platform.RequestOnlineOnlyAsync(resolved.Path, cancellationToken);
            await _hydrations.MarkReleaseRequestedAsync(
                revisionId,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }

        return await GetStatusAsync(revisionId, cancellationToken);
    }

    public async Task<VerifiedCollectionOriginal?> OpenVerifiedAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        ResolvedOriginal? resolved = await ResolveAsync(revisionId, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        ArchiveManagedHydrationRecord? ownership = await _hydrations.GetAsync(revisionId, cancellationToken);
        if (ownership is { IsActive: true, IsReleaseRequested: true })
        {
            return null;
        }

        OneDriveFilesOnDemandState state = _platform.GetState(resolved.Path);
        if (state.Availability != AssetAvailability.Local)
        {
            return null;
        }

        bool managed = ownership is { IsActive: true };
        VerifiedCollectionOriginal? verified = await _capacity.RunLargeReadAsync(
            managed,
            () => OpenVerifiedCoreAsync(resolved, cancellationToken),
            cancellationToken);
        if (verified is not null && managed)
        {
            await _capacity.TouchAsync(revisionId, cancellationToken);
        }

        return verified;
    }

    private static async Task<VerifiedCollectionOriginal?> OpenVerifiedCoreAsync(
        ResolvedOriginal resolved,
        CancellationToken cancellationToken)
    {
        FileStream stream = new(
            resolved.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length != resolved.Revision.SizeBytes)
            {
                await stream.DisposeAsync();
                return null;
            }

            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            Sha256Digest actual = new(Convert.ToHexString(hash).ToLowerInvariant());
            if (actual != resolved.Revision.ContentHash)
            {
                await stream.DisposeAsync();
                return null;
            }

            stream.Position = 0;
            return new VerifiedCollectionOriginal(
                stream,
                resolved.Revision.MediaType ?? "application/octet-stream");
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task<CollectionOriginalAccessSnapshot> LocalSnapshotAsync(
        ResolvedOriginal resolved,
        bool managed,
        bool isPinned,
        CancellationToken cancellationToken)
    {
        bool matches = await _capacity.RunLargeReadAsync(
            managed,
            () => VerifyContentAsync(resolved, cancellationToken),
            cancellationToken);
        return matches
            ? Snapshot(
                resolved.Revision.RevisionId,
                ReadyState,
                managed,
                isPinned,
                canHydrate: false,
                canView: true,
                canRelease: managed,
                managed
                    ? "The original is local, revision-verified and owned by Photo Identity."
                    : "The original is local and revision-verified. Photo Identity will not release it automatically.")
            : Snapshot(
                resolved.Revision.RevisionId,
                HashMismatchState,
                managed,
                isPinned,
                canHydrate: false,
                canView: false,
                canRelease: managed,
                "The local bytes do not match the immutable catalogue revision, so the original will not be served.");
    }

    private static async Task<bool> VerifyContentAsync(
        ResolvedOriginal resolved,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(
                resolved.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != resolved.Revision.SizeBytes)
            {
                return false;
            }

            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant()) ==
                resolved.Revision.ContentHash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<ResolvedOriginal?> ResolveAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        CatalogueProcessingAssetRevision? revision = await _catalogue.GetAssetRevisionAsync(
            revisionId,
            cancellationToken);
        if (revision is null ||
            !string.Equals(revision.SourceKind, "local-folder", StringComparison.Ordinal))
        {
            return null;
        }

        string root = Path.GetFullPath(revision.RootLocator);
        string relativePath = revision.SourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, _pathComparison))
        {
            return null;
        }

        // File symlinks are not valid authoritative originals. Cloud Files placeholders may carry
        // the ReparsePoint attribute while still reporting no link target, so they remain allowed.
        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null)
        {
            return null;
        }

        return new ResolvedOriginal(revision, path);
    }

    private static CollectionOriginalAccessSnapshot Snapshot(
        AssetRevisionId revisionId,
        string state,
        bool managed,
        bool pinned,
        bool canHydrate,
        bool canView,
        bool canRelease,
        string? message) =>
        new(revisionId, state, managed, pinned, canHydrate, canView, canRelease, message);

    private sealed record ResolvedOriginal(
        CatalogueProcessingAssetRevision Revision,
        string Path);
}
