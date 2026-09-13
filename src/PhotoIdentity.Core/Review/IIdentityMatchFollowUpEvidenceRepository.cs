using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

/// <summary>
/// Finds identity-evidence changes that should schedule a later regeneration. Automatic
/// assignments from the matcher are intentionally excluded so follow-up scheduling cannot
/// recurse solely from its own automatic decisions.
/// </summary>
public interface IIdentityMatchFollowUpEvidenceRepository
{
    Task<DateTimeOffset?> GetLatestQualifyingChangeAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentityMatchEvidenceVersion afterVersion,
        CancellationToken cancellationToken = default);
}
