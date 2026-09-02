using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Selects analyzed current revisions whose required post-analysis derivative is still missing.
/// </summary>
public interface IArchivePostAnalysisRepository
{
    Task<AssetRevisionId?> GetNextMissingProxyRevisionAsync(
        SourceId sourceId,
        Sha256Digest analysisProfileHash,
        string proxyProfileId,
        CancellationToken cancellationToken = default);
}
