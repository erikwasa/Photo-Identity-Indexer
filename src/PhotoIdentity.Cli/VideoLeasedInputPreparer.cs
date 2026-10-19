using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// Explicit-call worker preparation; not registered as a hosted service or
/// public command. Stages only a current, locally available and hash-verified
/// video revision from its PostgreSQL-registered source. Tool invocation
/// receives the staged private copy, never an original OneDrive path.
/// </summary>
public sealed class VideoLeasedInputPreparer(PostgresCatalogueDatabase database)
{
    public async Task<VideoStagedLocalCopy> PrepareAsync(
        VideoProcessingJobLease lease, string scratchRoot,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var safety = new PostgresVideoLeaseSafetyProbe(database);
        VideoExecutionPermit initial = await safety.CheckAsync(lease, token);
        if (!initial.Allowed)
            throw new InvalidOperationException("Video input staging denied by lease safety policy.");

        VideoLeasedLocalSource? location =
            await new PostgresVideoLocalSourceLocator(database).ResolveAsync(lease, token);
        if (location is null)
            throw new InvalidOperationException("Video source is no longer authorized.");
        if (!Path.IsPathFullyQualified(location.RootLocator))
            throw new InvalidOperationException("Registered video source root is not absolute.");

        OneDriveVideoAssetSource source =
            new(SourceId.From(location.SourceId), location.RootLocator);
        OneDriveVideoLocalStager stage = new(source);
        return await stage.StageAsync(
            new SourceAssetReference(source.SourceId, location.SourceKey),
            location.SizeBytes, location.Sha256, scratchRoot,
            async checkToken => (await safety.CheckAsync(lease, checkToken)).Allowed,
            token);
    }
}
