using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

public sealed record ArchiveSourceVerificationAdvanceResult(
    bool HadPendingSource,
    bool WaitingForLocalContent,
    bool VerificationCompleted,
    AssetRevisionId? RevisionId,
    AssetRevisionId? PreviousRevisionId,
    bool RevisionChanged,
    bool NewRevision,
    bool ManagedHydrationTransferred);

/// <summary>
/// Resolves unverified or metadata-divergent archive sources before analysis. Placeholder metadata
/// can enqueue work but never becomes immutable identity: local bytes are SHA-256 hashed before a
/// revision is established/reselected. Photo-Identity-owned pre-revision hydration is transferred
/// to the resulting revision so analysis/proxy generation can finish before release.
/// </summary>
public sealed class ArchiveSourceVerificationService
{
    private readonly SqliteArchiveSourceObservationRepository _observations;
    private readonly SqliteArchiveSourceHydrationRepository _sourceHydrations;
    private readonly SqliteArchiveAvailabilityRepository _availability;
    private readonly ArchiveHydrationCapacityService _capacity;
    private readonly IOneDriveFilesOnDemandPlatform _platform;
    private readonly TimeProvider _timeProvider;
    private readonly StringComparison _pathComparison;

    public ArchiveSourceVerificationService(
        SqliteArchiveSourceObservationRepository observations,
        SqliteArchiveSourceHydrationRepository sourceHydrations,
        SqliteArchiveAvailabilityRepository availability,
        ArchiveHydrationCapacityService capacity,
        IOneDriveFilesOnDemandPlatform platform,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(sourceHydrations);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _observations = observations;
        _sourceHydrations = sourceHydrations;
        _availability = availability;
        _capacity = capacity;
        _platform = platform;
        _timeProvider = timeProvider;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public async Task<ArchiveSourceVerificationAdvanceResult> AdvanceAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        ArchiveSourceObservation? source = await _observations.GetNextPendingAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return new ArchiveSourceVerificationAdvanceResult(
                false, false, false, null, null, false, false, false);
        }

        string path = ResolvePath(source);
        ArchiveManagedSourceHydrationRecord? ownership = await _sourceHydrations.GetAsync(
            source.AssetId,
            cancellationToken);
        OneDriveFilesOnDemandState state = _platform.GetState(path);

        if (ownership is { IsActive: true, IsReleaseRequested: true } &&
            state.Availability == AssetAvailability.OnlineOnly)
        {
            await _sourceHydrations.MarkReleasedAsync(
                source.AssetId,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            ownership = await _sourceHydrations.GetAsync(source.AssetId, cancellationToken);
        }

        switch (state.Availability)
        {
            case AssetAvailability.OnlineOnly:
                if (ownership is { IsActive: true, IsReleaseRequested: true })
                {
                    await _availability.RecordAsync(
                        source.AssetId,
                        AssetAvailability.OnlineOnly,
                        _timeProvider.GetUtcNow(),
                        cancellationToken);
                    return Waiting();
                }

                ArchiveHydrationAdmission admission = await _capacity.ExecuteSourceHydrationAdmissionAsync(
                    source,
                    async () =>
                    {
                        await _platform.RequestHydrationAsync(path, cancellationToken);
                        if (ownership is not { IsActive: true })
                        {
                            await _sourceHydrations.ClaimAsync(
                                source.AssetId,
                                _timeProvider.GetUtcNow(),
                                cancellationToken);
                        }
                        else
                        {
                            await _capacity.TouchSourceAsync(source.AssetId, cancellationToken);
                        }
                    },
                    cancellationToken);
                if (!admission.Allowed)
                {
                    throw new InvalidOperationException(
                        admission.Message ?? "Source verification hydration is blocked by the configured storage policy.");
                }

                OneDriveFilesOnDemandState requestedState = _platform.GetState(path);
                await _availability.RecordAsync(
                    source.AssetId,
                    requestedState.Availability,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                return Waiting();

            case AssetAvailability.Downloading:
                await _availability.RecordAsync(
                    source.AssetId,
                    AssetAvailability.Downloading,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                return Waiting();

            case AssetAvailability.Unavailable:
                throw new FileNotFoundException(
                    "An archive source requiring content verification is unavailable at its catalogued location.");

            case AssetAvailability.Error:
                throw new IOException(
                    "OneDrive availability could not be determined for an archive source requiring content verification.");

            case AssetAvailability.Local:
                break;

            default:
                throw new InvalidOperationException("Unsupported archive source availability state.");
        }

        bool managed = ownership is { IsActive: true, IsReleaseRequested: false };
        VerifiedSourceContent verified = await _capacity.RunLargeReadAsync(
            managed,
            () => HashLocalSourceAsync(path, source.MediaType, cancellationToken),
            cancellationToken);
        ArchiveSourceVerificationWriteResult persisted = await _observations.RecordVerifiedContentAsync(
            source.AssetId,
            verified.ContentHash,
            verified.SizeBytes,
            verified.LastWriteTimeUtc,
            verified.MediaType,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        bool transferred = managed && await _sourceHydrations.TransferToRevisionAsync(
            source.AssetId,
            persisted.RevisionId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        AssetRevisionId? previousRevisionId = source.VerifiedRevisionId;
        bool revisionChanged = previousRevisionId is AssetRevisionId previous &&
            previous != persisted.RevisionId;
        return new ArchiveSourceVerificationAdvanceResult(
            true,
            false,
            true,
            persisted.RevisionId,
            previousRevisionId,
            revisionChanged,
            persisted.NewRevision,
            transferred);
    }

    private string ResolvePath(ArchiveSourceObservation source)
    {
        string root = Path.GetFullPath(source.RootLocator);
        string relative = source.SourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, _pathComparison))
        {
            throw new InvalidOperationException("Archive source verification path escapes the configured source root.");
        }

        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidOperationException("File symlinks are not valid authoritative archive sources.");
        }

        return path;
    }

    private static async Task<VerifiedSourceContent> HashLocalSourceAsync(
        string path,
        string mediaType,
        CancellationToken cancellationToken)
    {
        DateTimeOffset beforeWrite = File.GetLastWriteTimeUtc(path);
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long size = stream.Length;
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        DateTimeOffset afterWrite = File.GetLastWriteTimeUtc(path);
        FileInfo after = new(path);
        if (beforeWrite != afterWrite || after.Length != size)
        {
            throw new IOException(
                "The authoritative archive source changed while it was being verified. Synchronize and retry.");
        }

        return new VerifiedSourceContent(
            size,
            afterWrite,
            mediaType,
            new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant()));
    }

    private static ArchiveSourceVerificationAdvanceResult Waiting() =>
        new(true, true, false, null, null, false, false, false);

    private sealed record VerifiedSourceContent(
        long SizeBytes,
        DateTimeOffset LastWriteTimeUtc,
        string MediaType,
        Sha256Digest ContentHash);
}
