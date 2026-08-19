namespace PhotoIdentity.Api;

public static class PhotoPlaceEnrichmentEndpoints
{
    public static IEndpointRouteBuilder MapPhotoPlaceEnrichmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/place-enrichment/status",
            (
                GeoNamesReverseGeocodingConfiguration configuration,
                GeoNamesAutomaticEnrichmentConfiguration automatic,
                PhotoPlaceEnrichmentWorkerState workerState) =>
            {
                PhotoPlaceEnrichmentWorkerSnapshot worker = workerState.GetSnapshot();
                return Results.Ok(new PhotoPlaceEnrichmentStatusResponse(
                    Provider: "geonames",
                    Configured: configuration.IsConfigured,
                    ContractKey: configuration.ContractKey,
                    ServiceHost: configuration.BaseUri.Host,
                    Language: configuration.Language,
                    MinimumRequestIntervalMilliseconds: configuration.MinimumRequestIntervalMilliseconds,
                    AutomaticEnrichmentEnabled: automatic.Enabled && configuration.IsConfigured,
                    AutomaticMinimumRequestIntervalMilliseconds: automatic.MinimumRequestIntervalMilliseconds,
                    AutomaticIdlePollIntervalMilliseconds: automatic.IdlePollIntervalMilliseconds,
                    AutomaticState: worker.State,
                    AutomaticMessage: $"{worker.Message} Idle polling uses {automatic.IdlePollIntervalMilliseconds} ms when no immediate work is available.",
                    LastAutomaticActivityAtUtc: worker.LastActivityAtUtc,
                    NextAutomaticAttemptAtUtc: worker.NextAttemptAtUtc));
            });

        endpoints.MapPost(
            "/api/place-enrichment/geonames",
            ExecuteGeoNamesAsync);
        return endpoints;
    }

    private static async Task<IResult> ExecuteGeoNamesAsync(
        int? limit,
        bool? refresh,
        GeoNamesReverseGeocodingConfiguration configuration,
        PhotoPlaceEnrichmentService service,
        CancellationToken cancellationToken)
    {
        if (!configuration.IsConfigured)
        {
            return Results.Conflict(new PhotoPlaceEnrichmentErrorResponse(
                "GeoNames enrichment is disabled. Configure a private PhotoIdentity:GeoNames:Username and restart Photo Identity before invoking it."));
        }

        try
        {
            PhotoPlaceEnrichmentReport report = await service.ExecuteBatchAsync(
                limit ?? 10,
                refresh ?? false,
                cancellationToken);
            return Results.Ok(report);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new PhotoPlaceEnrichmentErrorResponse(exception.Message));
        }
    }
}

public sealed record PhotoPlaceEnrichmentStatusResponse(
    string Provider,
    bool Configured,
    string ContractKey,
    string ServiceHost,
    string Language,
    int MinimumRequestIntervalMilliseconds,
    bool AutomaticEnrichmentEnabled,
    int AutomaticMinimumRequestIntervalMilliseconds,
    int AutomaticIdlePollIntervalMilliseconds,
    string AutomaticState,
    string AutomaticMessage,
    DateTimeOffset? LastAutomaticActivityAtUtc,
    DateTimeOffset? NextAutomaticAttemptAtUtc);

public sealed record PhotoPlaceEnrichmentErrorResponse(string Error);
