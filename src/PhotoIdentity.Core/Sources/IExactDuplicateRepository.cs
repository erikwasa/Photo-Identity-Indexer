using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Sources;

public sealed record ExactDuplicateSourceCopy(
    SourceId SourceId,
    AssetId AssetId,
    AssetRevisionId RevisionId,
    string SourceKey,
    bool IsMissing);

public sealed record ExactDuplicateGroup(
    Sha256Digest ContentHash,
    IReadOnlyList<ExactDuplicateSourceCopy> Copies);

/// <summary>
/// Reads exact duplicate groups from authoritative, verified current source-copy revisions.
/// Duplicate membership never merges assets or revisions and remains independent from source presence.
/// </summary>
public interface IExactDuplicateRepository
{
    Task<IReadOnlyList<ExactDuplicateGroup>> GetGroupsAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default);
}
