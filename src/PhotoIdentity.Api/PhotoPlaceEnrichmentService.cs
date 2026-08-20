using PhotoIdentity.Core.Places;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public sealed record PhotoPlaceEnrichmentIssue(
    string RevisionId,
    string Outcome,
    string? ProviderCode,
    string Message);

public sealed record PhotoPlaceEnrichmentReport(
    int Candidates,
    int ProviderRequests,
    int CachedResults,
    int Assigned,
    int UnchangedAutomatic,
    int SkippedManual,
    int SkippedConflict,
    int NoResult,
    int Deferred,
    int Failed,
    bool StoppedEarly,
    string? StopReasonCode = null,
    string? StopReasonMessage = null,
    IReadOnlyList<PhotoPlaceEnrichmentIssue>? Issues = null);

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
    private readonly SemaphoreSlim _executionGate = new(1, 1);

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
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteBatchCoreAsync(limit, refresh, cancellationToken);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task<PhotoPlaceEnrichmentReport> ExecuteBatchCoreAsync(
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
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
        int noResult = 0;
        int deferred = 0;
        int failed = 0;
        bool stoppedEarly = false;
        string? stopReasonCode = null;
        string? stopReasonMessage = null;
        List<PhotoPlaceEnrichmentIssue> issues = [];

        foreach (CataloguePlaceEnrichmentCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CatalogueAutomaticPlaceEligibility eligibility =
                await _automaticPlaces.GetEligibilityAsync(candidate.RevisionId, cancellationToken);
            if (eligibility.BlockedByManual)
            {
                skippedManual++;
                await MarkSkippedAsync(candidate, "manual-place", "Manual place history takes precedence over automatic enrichment.", cancellationToken);
                continue;
            }

            if (eligibility.BlockedByConflict)
            {
                skippedConflict++;
                await MarkSkippedAsync(candidate, "migration-conflict", "The legacy place conflict requires explicit maintainer resolution.", cancellationToken);
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
                ReverseGeocodeResponse response = await _geocoder.ReverseGeocodeAsync(
                    new ReverseGeocodeQuery(candidate.Latitude, candidate.Longitude),
                    cancellationToken);
                providerRequests += response.ProviderRequestCount;

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
                else if (response.Status == ReverseGeocodeStatus.NoResult)
                {
                    noResult++;
                    string message = "GeoNames found no nearby populated place for this photo's persisted GPS coordinates.";
                    await _enrichment.MarkSkippedAsync(
                        _geocoder.ProviderName,
                        _geocoder.ContractKey,
                        candidate,
                        response.ErrorCode ?? "no-result",
                        response.ErrorMessage ?? message,
                        cancellationToken);
                    issues.Add(new PhotoPlaceEnrichmentIssue(
                        candidate.RevisionId.ToString(),
                        "no-result",
                        response.ErrorCode,
                        message));
                    continue;
                }
                else if (response.Status == ReverseGeocodeStatus.Deferred)
                {
                    deferred++;
                    string message = BuildOperatorReason(response);
                    await _enrichment.MarkDeferredAsync(
                        _geocoder.ProviderName,
                        _geocoder.ContractKey,
                        candidate,
                        response.ErrorCode,
                        response.ErrorMessage,
                        cancellationToken);
                    issues.Add(new PhotoPlaceEnrichmentIssue(
                        candidate.RevisionId.ToString(),
                        "deferred",
                        response.ErrorCode,
                        message));
                    if (response.StopBatch)
                    {
                        stoppedEarly = true;
                        stopReasonCode = response.ErrorCode;
                        stopReasonMessage = message;
                        break;
                    }
                    continue;
                }
                else
                {
                    failed++;
                    string message = BuildOperatorReason(response);
                    await _enrichment.MarkFailedAsync(
                        _geocoder.ProviderName,
                        _geocoder.ContractKey,
                        candidate,
                        response.ErrorCode,
                        response.ErrorMessage,
                        cancellationToken);
                    issues.Add(new PhotoPlaceEnrichmentIssue(
                        candidate.RevisionId.ToString(),
                        "failed",
                        response.ErrorCode,
                        message));
                    if (response.StopBatch)
                    {
                        stoppedEarly = true;
                        stopReasonCode = response.ErrorCode;
                        stopReasonMessage = message;
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
                await MarkSkippedAsync(candidate, "manual-place", "Manual place history took precedence before the automatic write.", cancellationToken);
                continue;
            }

            if (write.BlockedByConflict)
            {
                skippedConflict++;
                await MarkSkippedAsync(candidate, "migration-conflict", "A legacy place conflict appeared before the automatic write.", cancellationToken);
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
            noResult,
            deferred,
            failed,
            stoppedEarly,
            stopReasonCode,
            stopReasonMessage,
            issues);
    }

    private static string BuildOperatorReason(ReverseGeocodeResponse response) => response.ErrorCode switch
    {
        "10" => "GeoNames authorization failed. Confirm the configured username is correct and enable Free Web Services on the GeoNames account page after confirming the account email.",
        "18" => "GeoNames reports that the daily web-service credit limit has been exceeded. Retry after the provider limit resets.",
        "19" => "GeoNames reports that the hourly web-service credit limit has been exceeded. Retry after the provider limit resets.",
        "20" => "GeoNames reports that the weekly web-service credit limit has been exceeded. Retry after the provider limit resets.",
        "13" or "22" => "GeoNames is temporarily unavailable. Retry the enrichment later.",
        "transport" => "Photo Identity could not complete the HTTPS request to GeoNames. Check network access and retry.",
        "14" or "21" or "23" or "24" or "27" =>
            $"GeoNames rejected the reverse-geocoding request (provider code {response.ErrorCode}). This indicates a request or service-contract problem rather than a photo-data problem.",
        _ when response.ErrorCode?.StartsWith("http-", StringComparison.Ordinal) == true =>
            $"GeoNames rejected the HTTPS request ({response.ErrorCode}). Check provider availability and configuration before retrying.",
        _ => "GeoNames could not process the reverse-geocoding request. The failed attempt remains retryable.",
    };

    private Task MarkSkippedAsync(
        CataloguePlaceEnrichmentCandidate candidate,
        string reasonCode,
        string reasonMessage,
        CancellationToken cancellationToken) =>
        _enrichment.MarkSkippedAsync(
            _geocoder.ProviderName,
            _geocoder.ContractKey,
            candidate,
            reasonCode,
            reasonMessage,
            cancellationToken);
}
