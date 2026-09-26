using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Api;

public interface ISourceCopyPurgeFileSystem
{
    void DeleteFile(string path);
    void DeleteDirectory(string path);
    bool FileExists(string path);
    bool DirectoryExists(string path);
}

public sealed class SourceCopyPurgeFileSystem : ISourceCopyPurgeFileSystem
{
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
}

public sealed record SourceCopyPurgeCycleResult(int Attempted, int Completed, int Failed);

/// <summary>
/// Executes the privacy purge in the only safe order: durable exclusion, durable artifact manifest,
/// filesystem deletion and verification, catalogue deletion, then manifest retirement. A crash at
/// any intermediate point leaves the exclusion active and the next cycle can repeat the work.
/// Scheduling belongs to the archive advancement loop so there is a single lifecycle coordinator.
/// </summary>
public sealed class SourceCopyPurgeService
{
    private static readonly TimeSpan FailedRetryDelay = TimeSpan.FromSeconds(30);

    private readonly ISourceCopyExclusionRepository _exclusions;
    private readonly ISourceCopyPurgeRepository _purges;
    private readonly SourceCopyPurgeRoots _roots;
    private readonly ISourceCopyPurgeFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;

    public SourceCopyPurgeService(
        ISourceCopyExclusionRepository exclusions,
        ISourceCopyPurgeRepository purges,
        SourceCopyPurgeRoots roots,
        ISourceCopyPurgeFileSystem fileSystem,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        ArgumentNullException.ThrowIfNull(purges);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _exclusions = exclusions;
        _purges = purges;
        _roots = roots;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
    }

    public async Task<SourceCopyPurgeCycleResult> RunEligibleAsync(
        int maximumAttempts = 4,
        CancellationToken cancellationToken = default)
    {
        if (maximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<SourceCopyExclusionState> all = await _exclusions.ListAsync(
            cancellationToken: cancellationToken);
        SourceCopyExclusionState[] eligible = all
            .Where(state => IsEligible(state, now))
            .OrderBy(state => state.PurgeUpdatedAtUtc)
            .ThenBy(state => state.SourceId.ToString(), StringComparer.Ordinal)
            .ThenBy(state => state.SourceKey, StringComparer.Ordinal)
            .Take(maximumAttempts)
            .ToArray();

        int completed = 0;
        int failed = 0;
        foreach (SourceCopyExclusionState state in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = await PurgeAsync(state.SourceId, state.SourceKey, cancellationToken);
            if (succeeded)
            {
                completed++;
            }
            else
            {
                failed++;
            }
        }

        return new SourceCopyPurgeCycleResult(eligible.Length, completed, failed);
    }

    public async Task<bool> PurgeAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        SourceCopyExclusionState? state = await _exclusions.GetAsync(sourceId, sourceKey, cancellationToken);
        if (state is null)
        {
            throw new InvalidOperationException("Source-copy exclusion does not exist.");
        }
        if (state.PurgeState == SourceCopyPurgeStates.Completed)
        {
            return true;
        }

        try
        {
            DateTimeOffset attemptStarted = _timeProvider.GetUtcNow();
            await _exclusions.SetPurgeStateAsync(
                sourceId,
                sourceKey,
                SourceCopyPurgeStates.Attempting,
                errorCode: null,
                attemptStarted,
                cancellationToken);

            SourceCopyPurgeManifest manifest = await _purges.PrepareManifestAsync(
                sourceId,
                sourceKey,
                _roots,
                attemptStarted,
                cancellationToken);

            foreach (SourceCopyPurgeArtifact artifact in manifest.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteAndVerify(artifact);
            }

            await _purges.PurgeCatalogueAsync(sourceId, sourceKey, cancellationToken);

            foreach (SourceCopyPurgeArtifact artifact in manifest.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifyMissing(artifact);
            }

            await _purges.ClearManifestAsync(sourceId, sourceKey, cancellationToken);
            await _exclusions.SetPurgeStateAsync(
                sourceId,
                sourceKey,
                SourceCopyPurgeStates.Completed,
                errorCode: null,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave the durable state as attempting. A later process/cycle safely retries it.
            throw;
        }
        catch (Exception exception)
        {
            await _exclusions.SetPurgeStateAsync(
                sourceId,
                sourceKey,
                SourceCopyPurgeStates.Failed,
                ErrorCode(exception),
                _timeProvider.GetUtcNow(),
                CancellationToken.None);
            return false;
        }
    }

    private void DeleteAndVerify(SourceCopyPurgeArtifact artifact)
    {
        if (artifact.IsDirectory)
        {
            _fileSystem.DeleteDirectory(artifact.AbsolutePath);
        }
        else
        {
            _fileSystem.DeleteFile(artifact.AbsolutePath);
        }
        VerifyMissing(artifact);
    }

    private void VerifyMissing(SourceCopyPurgeArtifact artifact)
    {
        bool stillExists = artifact.IsDirectory
            ? _fileSystem.DirectoryExists(artifact.AbsolutePath)
            : _fileSystem.FileExists(artifact.AbsolutePath);
        if (stillExists)
        {
            throw new IOException("A known purge artifact remains after deletion was requested.");
        }
    }

    private static bool IsEligible(SourceCopyExclusionState state, DateTimeOffset now) =>
        state.PurgeState is SourceCopyPurgeStates.Pending or SourceCopyPurgeStates.Attempting ||
        (state.PurgeState == SourceCopyPurgeStates.Failed &&
         state.PurgeUpdatedAtUtc <= now - FailedRetryDelay);

    private static string ErrorCode(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "artifact-access-denied",
        IOException => "artifact-delete-failed",
        InvalidOperationException => "purge-preparation-failed",
        _ => "purge-failed",
    };
}
