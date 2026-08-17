using PhotoIdentity.Core.Places;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public sealed record PhotoPlaceEnrichmentReport(
    int Candidates,
    int ProviderRequests,
    int CachedResults,
    int Assigned,
    int UnchangedAutomatic,
    int SkippedManual,
    int SkippedConflict,
    int Deferred,
    int Failed,
    bool StoppedEarly);

/// <summary>
/// Applies reverse-geocoded places from persisted GPS only. The service never resolves source paths,
/// opens photos or asks OneDrive to hydrate content.
/// </summary>
public sealed class PhotoPlaceEnrichmentService
{
    private const string AutomaticActor = "automatic-place-enrichment";

    private readonly IReverseGeocoder _geocoder;
    private readonly SqlitePhotoPlaceEnrichmentRepository _enrichment;
    private readonly SqliteAutomaticPhotoPlaceRepository _automaticPlaces;

    public PhotoPlaceEnrichmentService(
        IReverseGeocoder geocoder,
        SqlitePhotoPlaceEnrichmentRepository enrichment,
        SqliteAutomaticPhotoPlaceRepository automaticPlaces)
    {
        ArgumentNullException.ThrowIfNull(geocoder);
        ArgumentNullException.ThrowIfNull(enrichment);
        ArgumentNullException.ThrowIfNull(automaticPlaces);
        _geocoder = geocoder;
        _enrichment = enrichment;
        _automaticPlaces = automaticPlaces;
    }

    public async Task<PhotoPlaceEnrichmentReport> ExecuteBatchAsync(
        int limit = 10,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CataloguePlaceEnrichmentCandidate> candidates =
            await _enrichment.GetCandidatesAsync(
                _geocoder.ProviderName,
                _geocoder.ContractKey,
                limit,
                refresh,
                cancellationToken);

        int providerRequests = 0;
        int cached = 0;
        int assigned = 0;
        int unchangedAutomatic = 0;
        int skippedManual = 0;
        int skippedConflict = 0;
        int deferred = 0;
        int failed = 0;
        bool stoppedEarly = false;

        foreach (CataloguePlaceEnrichmentCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CatalogueAutomaticPlaceEligibility eligibility =
                await _automaticPlaces.GetEligibilityAsync(candidate.RevisionId, cancellationToken);
            if (eligibility.BlockedByManual)
            {
                skippedManual++;
                continue;
            }

            if (eligibility.BlockedByConflict)
            {
                skippedConflict++;
                continue;
            }

            ReverseGeocodePlace? resolvedPlace = null;
            if (!refresh)
            {
                CatalogueReverseGeocodeCacheEntry? cachedEntry = await _enrichment.GetCachedAsync(
                    _geocoder.ProviderName,
                    _geocoder.ContractKey,
                    candidate.Latitude,
                    candidate.Longitude,
                    cancellationToken);
                if (cachedEntry is not null)
                {
                    cached++;
                    resolvedPlace = new ReverseGeocodePlace(
                        PhotoPlacePath.Parse(cachedEntry.PlaceValue),
                        cachedEntry.ProviderResultId,
                        cachedEntry.CountryCode);
                }
            }

            if (resolvedPlace is null)
            {
                providerRequests++;
                ReverseGeocodeResponse response = await _geocoder.ReverseGeocodeAsync(
                    new ReverseGeocodeQuery(candidate.Latitude, candidate.Longitude),
                    cancellationToken);

                if (response.Status == ReverseGeocodeStatus.Success && response.Place is not null)
                {
                    resolvedPlace = response.Place;
                    await _enrichment.SaveCacheAsync(
                        _geocoder.ProviderName,
                        _geocoder.ContractKey,
                        candidate,
                        resolvedPlace.Place.DisplayValue,
                        resolvedPlace.ProviderResultId,
                        resolvedPlace.CountryCode,
                        cancellationToken);
                }
                else if (response.Status == ReverseGeocodeStatus.Deferred)
                {
                    deferred++;
                    await _enrichment.MarkDeferredAsync(
                        _geocoder.ProviderName,
                        _geocoder.ContractKey,
                        candidate,
                        response.ErrorCode,
                        response.ErrorMessage,
                        cancellationToken);
                    if (response.StopBatch)
                    {
                        stoppedEarly = true;
                        break;
                    }
                    continue;
                }
                else
                {
                    failed++;
                    await _enrichment.MarkFailedAsync(
                        _geocoder.ProviderName,
                        _geocoder.ContractKey,
                        candidate,
                        response.ErrorCode,
                        response.ErrorMessage,
                        cancellationToken);
                    if (response.StopBatch)
                    {
                        stoppedEarly = true;
                        break;
                    }
                    continue;
                }
            }

            CatalogueAutomaticPlaceWriteResult write = await _automaticPlaces.TrySetAsync(
                candidate.RevisionId,
                resolvedPlace.Place.DisplayValue,
                _geocoder.ProviderName,
                AutomaticActor,
                cancellationToken);
            if (write.BlockedByManual)
            {
                skippedManual++;
                continue;
            }

            if (write.BlockedByConflict)
            {
                skippedConflict++;
                continue;
            }

            if (write.Applied)
            {
                assigned++;
            }
            else
            {
                unchangedAutomatic++;
            }

            await _enrichment.MarkSucceededAsync(
                _geocoder.ProviderName,
                _geocoder.ContractKey,
                candidate,
                resolvedPlace.Place.DisplayValue,
                resolvedPlace.ProviderResultId,
                resolvedPlace.CountryCode,
                cancellationToken);
        }

        return new PhotoPlaceEnrichmentReport(
            candidates.Count,
            providerRequests,
            cached,
            assigned,
            unchangedAutomatic,
            skippedManual,
            skippedConflict,
            deferred,
            failed,
            stoppedEarly);
    }
}
