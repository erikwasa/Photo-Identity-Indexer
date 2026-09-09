using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

/// <summary>Marks a source for re-verification, returning active revision hydration ownership to source identity first.</summary>
public interface IArchiveSourceVerificationStateRepository
{
    Task MarkNeedsVerificationAsync(AssetId assetId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default);
}
