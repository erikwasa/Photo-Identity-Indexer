using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Persists the most recently observed availability for an archive asset independently of immutable revisions.
/// </summary>
public interface IArchiveAvailabilityRepository
{
    Task RecordAsync(
        AssetId assetId,
        AssetAvailability availability,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken = default);
}
