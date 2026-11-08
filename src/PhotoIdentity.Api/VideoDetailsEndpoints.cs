using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Api;

/// <summary>
/// Video-only opt-in details. No route reads original bytes or triggers
/// video processing. Explicit location lookup is the only network action.
/// </summary>
public static class VideoDetailsEndpoints
{
    private const string Actor = "local-maintainer";

    public static IEndpointRouteBuilder MapVideoDetailsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/videos/{revisionId}/details", GetAsync);
        endpoints.MapPut("/api/videos/{revisionId}/capture-time", SetCaptureAsync);
        endpoints.MapDelete("/api/videos/{revisionId}/capture-time", ClearCaptureAsync);
        endpoints.MapPost("/api/videos/{revisionId}/people", AddPersonAsync);
        endpoints.MapDelete("/api/videos/{revisionId}/people/{personId}", RemovePersonAsync);
        endpoints.MapPut("/api/videos/{revisionId}/place", SetPlaceAsync);
        endpoints.MapDelete("/api/videos/{revisionId}/place", ClearPlaceAsync);
        endpoints.MapPost("/api/videos/{revisionId}/resolve-place", ResolvePlaceAsync);
        return endpoints;
    }

    private static bool Enabled(IConfiguration config) =>
        string.Equals(config["PhotoIdentity:VideoLibraryEnabled"], "true",
            StringComparison.OrdinalIgnoreCase);

    private static bool TryRevision(string id, out AssetRevisionId revision)
    {
        if (Guid.TryParse(id, out Guid parsed) && parsed != Guid.Empty)
        {
            revision = AssetRevisionId.From(parsed);
            return true;
        }
        revision = default;
        return false;
    }

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static IResult Invalid() => Results.BadRequest(new { error = "Invalid identifier or value." });

    private static async Task<IResult> GetAsync(
        string revisionId, PostgresVideoDetailsRepository repository,
        IConfiguration settings, HttpContext context, CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!Enabled(settings)) return Results.NotFound();
        if (!TryRevision(revisionId, out AssetRevisionId id)) return Invalid();
        VideoDetailsRecord? details = await repository.GetAsync(id, cancellationToken);
        return details is null ? Results.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> ApplyAsync(
        string revisionId, PostgresVideoDetailsRepository repository,
        IConfiguration settings, HttpContext context, CancellationToken cancellationToken,
        Func<AssetRevisionId, Task> mutate)
    {
        NoStore(context);
        if (!Enabled(settings)) return Results.NotFound();
        if (!TryRevision(revisionId, out AssetRevisionId id)) return Invalid();
        try
        {
            await mutate(id);
            VideoDetailsRecord? updated = await repository.GetAsync(id, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "The current included video or person was not found." });
        }
        catch (ArgumentException)
        {
            return Invalid();
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error = "The video or person changed, or the operation is not permitted." });
        }
    }

    public sealed record CaptureTimeRequest(string Value);
    public sealed record VideoPersonRequest(string PersonId);
    public sealed record VideoPlaceRequest(string Value);

    private static Task<IResult> SetCaptureAsync(
        string revisionId, CaptureTimeRequest request,
        PostgresVideoDetailsRepository repository, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        // Require an explicit UTC designator or +/-HH:MM offset. A local
        // wall-clock string without zone must never silently become UTC.
        string value = request.Value?.Trim() ?? "";
        if (!value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) &&
            !System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:\d{2}$"))
            return Task.FromResult(Invalid());
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset parsed))
            return Task.FromResult(Invalid());
        return ApplyAsync(revisionId, repository, settings, context, token,
            id => repository.SetCaptureDateAsync(id, parsed, Actor, token));
    }

    private static Task<IResult> ClearCaptureAsync(
        string revisionId, PostgresVideoDetailsRepository repository,
        IConfiguration settings, HttpContext context, CancellationToken token) =>
        ApplyAsync(revisionId, repository, settings, context, token,
            id => repository.SetCaptureDateAsync(id, null, Actor, token));

    private static Task<IResult> AddPersonAsync(
        string revisionId, VideoPersonRequest request,
        PostgresVideoDetailsRepository repository, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        if (!Guid.TryParse(request.PersonId, out Guid person) || person == Guid.Empty)
            return Task.FromResult(Invalid());
        return ApplyAsync(revisionId, repository, settings, context, token,
            id => repository.SetPersonAsync(id, person, true, Actor, token));
    }

    private static Task<IResult> RemovePersonAsync(
        string revisionId, string personId,
        PostgresVideoDetailsRepository repository, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        if (!Guid.TryParse(personId, out Guid person) || person == Guid.Empty)
            return Task.FromResult(Invalid());
        return ApplyAsync(revisionId, repository, settings, context, token,
            id => repository.SetPersonAsync(id, person, false, Actor, token));
    }

    private static Task<IResult> SetPlaceAsync(
        string revisionId, VideoPlaceRequest request,
        PostgresVideoDetailsRepository repository, IConfiguration settings,
        HttpContext context, CancellationToken token) =>
        ApplyAsync(revisionId, repository, settings, context, token,
            id => repository.SetPlaceAsync(id, request.Value, "manual", Actor,
                cancellationToken: token));

    private static Task<IResult> ClearPlaceAsync(
        string revisionId, PostgresVideoDetailsRepository repository,
        IConfiguration settings, HttpContext context, CancellationToken token) =>
        ApplyAsync(revisionId, repository, settings, context, token,
            id => repository.SetPlaceAsync(id, null, "manual", Actor,
                cancellationToken: token));

    private static async Task<IResult> ResolvePlaceAsync(
        string revisionId, PostgresVideoDetailsRepository repository,
        IReverseGeocoder geocoder, IPhotoPlaceEnrichmentStateRepository cache,
        GeoNamesReverseGeocodingConfiguration geoNames,
        IConfiguration settings, HttpContext context, CancellationToken token)
    {
        NoStore(context);
        if (!Enabled(settings)) return Results.NotFound();
        if (!TryRevision(revisionId, out AssetRevisionId id)) return Invalid();
        VideoDetailsRecord? details = await repository.GetAsync(id, token);
        if (details is null) return Results.NotFound();
        if (details.ManualPlaceOverride)
            return Results.Conflict(new { error = "A manual location takes precedence. Use the manual Place editor." });
        if (details.Latitude is not double latitude || details.Longitude is not double longitude ||
            (latitude == 0 && longitude == 0))
            return Results.Conflict(new { error = "No valid embedded GPS coordinates are available." });
        if (!geoNames.IsConfigured)
            return Results.Conflict(new { error = "Configure the existing private GeoNames account before looking up Places." });

        try
        {
            ReverseGeocodeCacheEntry? cached = await cache.GetCachedAsync(
                geocoder.ProviderName, geocoder.ContractKey, latitude, longitude, token);
            ReverseGeocodePlace? place = cached is null ? null :
                new ReverseGeocodePlace(PhotoPlacePath.Parse(cached.PlaceValue),
                    cached.ProviderResultId, cached.CountryCode);
            if (place is null)
            {
                ReverseGeocodeResponse result = await geocoder.ReverseGeocodeAsync(
                    new ReverseGeocodeQuery(latitude, longitude), token);
                if (result.Status != ReverseGeocodeStatus.Success || result.Place is null)
                    return Results.Conflict(new { error = "A Place could not be resolved from the embedded GPS. Try again later or set one manually." });
                place = result.Place;
                // Reuse existing photo cache reads, but do not publish new
                // video-derived coordinates to a global cache: source exclusion
                // could occur while the external request is in flight.
            }
            // Recheck after I/O, including any manual Place edits while the
            // external request was in flight. The repository rechecks again
            // under the source lock before applying the automatic action.
            VideoDetailsRecord? latest = await repository.GetAsync(id, token);
            if (latest is null || latest.ManualPlaceOverride ||
                latest.Latitude != latitude || latest.Longitude != longitude)
                return Results.Conflict(new { error = "The video metadata or location changed; no automatic Place was applied." });
            await repository.SetPlaceAsync(id, place.Place.DisplayValue,
                "automatic", "geonames-video-lookup", geocoder.ProviderName, token,
                expectedLatitude: latitude, expectedLongitude: longitude);
            VideoDetailsRecord? updated = await repository.GetAsync(id, token);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException)
        {
            return Invalid();
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error = "The video changed before the location lookup could be applied." });
        }
    }
}
