using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public sealed record ReviewIdentityMatchModelRevision(
    ModelId ModelId,
    Sha256Digest ModelHash,
    int FaceCount);

/// <summary>
/// Lists exact embedding-model revisions that can participate in identity-match regeneration.
/// </summary>
public interface IIdentityMatchRegenerationModelRepository
{
    Task<IReadOnlyList<ReviewIdentityMatchModelRevision>> ListAsync(
        CancellationToken cancellationToken = default);
}
