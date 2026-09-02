using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Places;

public sealed record PhotoPlaceEnrichmentCandidate(
    AssetRevisionId RevisionId,
    double Latitude,
    double Longitude);

public sealed record ReverseGeocodeCacheEntry(
    string PlaceValue,
    string? ProviderResultId,
    string? CountryCode,
    DateTimeOffset ResolvedAtUtc);

/// <summary>
/// Durable operational state for reverse-geocoding workers.
/// Authoritative Places assignment remains a separate domain.
/// </summary>
public interface IPhotoPlaceEnrichmentStateRepository
{
    Task<IReadOnlyList<PhotoPlaceEnrichmentCandidate>> GetCandidatesAsync(
        string provider,
        string contractKey,
        int limit,
        bool refresh,
        CancellationToken cancellationToken = default);

    Task<ReverseGeocodeCacheEntry?> GetCachedAsync(
        string provider,
        string contractKey,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);

    Task SaveCacheAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string placeValue,
        string? providerResultId,
        string? countryCode,
        CancellationToken cancellationToken = default);

    Task MarkSucceededAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string placeValue,
        string? providerResultId,
        string? countryCode,
        CancellationToken cancellationToken = default);

    Task MarkSkippedAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string reasonCode,
        string reasonMessage,
        CancellationToken cancellationToken = default);

    Task MarkDeferredAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        string provider,
        string contractKey,
        PhotoPlaceEnrichmentCandidate candidate,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default);
}
