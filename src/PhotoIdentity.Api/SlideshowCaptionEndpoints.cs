using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public static class SlideshowCaptionEndpoints
{
    public static IEndpointRouteBuilder MapSlideshowCaptionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/slideshows/captions/{revisionId}",
            GetAsync);
        endpoints.MapPost(
            "/api/slideshows/captions/{revisionId}",
            RequestAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        string revisionId,
        string? language,
        ISlideshowCaptionService captions,
        CancellationToken cancellationToken)
    {
        if (!TryParse(revisionId, language, out AssetRevisionId parsed, out string normalizedLanguage))
        {
            return Results.BadRequest(new
            {
                error = "Caption revision or language is invalid.",
            });
        }

        SlideshowCaptionResponse response = await captions.GetAsync(
            parsed,
            normalizedLanguage,
            cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> RequestAsync(
        string revisionId,
        SlideshowCaptionRequest request,
        ISlideshowCaptionService captions,
        CancellationToken cancellationToken)
    {
        if (!TryParse(revisionId, request.Language, out AssetRevisionId parsed, out string normalizedLanguage))
        {
            return Results.BadRequest(new
            {
                error = "Caption revision or language is invalid.",
            });
        }

        SlideshowCaptionResponse response = await captions.RequestAsync(
            parsed,
            normalizedLanguage,
            cancellationToken);
        return Results.Ok(response);
    }

    private static bool TryParse(
        string revisionId,
        string? language,
        out AssetRevisionId parsed,
        out string normalizedLanguage)
    {
        parsed = default;
        normalizedLanguage = string.Empty;

        if (!Guid.TryParse(revisionId, out Guid revisionGuid) ||
            revisionGuid == Guid.Empty ||
            !SlideshowCaptionLanguage.TryNormalize(language, out normalizedLanguage))
        {
            return false;
        }

        parsed = AssetRevisionId.From(revisionGuid);
        return true;
    }
}
