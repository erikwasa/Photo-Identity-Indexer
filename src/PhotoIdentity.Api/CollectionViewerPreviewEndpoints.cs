using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

public static class CollectionViewerPreviewEndpoints
{
    public static IEndpointRouteBuilder MapCollectionViewerPreviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos/{revisionId}/viewer-preview", GetViewerPreviewAsync);
        return endpoints;
    }

    private static async Task<IResult> GetViewerPreviewAsync(
        string revisionId,
        CollectionReviewProxyFileResolver proxyResolver,
        CollectionOriginalAccessService originalAccess,
        ReviewProxyGenerationConfiguration proxyConfiguration,
        OpenCvReviewProxyRenderer renderer,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid value) || value == Guid.Empty)
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        AssetRevisionId parsedRevisionId = AssetRevisionId.From(value);
        CollectionPhotoFile? proxy = await proxyResolver.ResolveAsync(parsedRevisionId, cancellationToken);
        if (proxy is not null)
        {
            return Results.File(proxy.Path, proxy.ContentType, enableRangeProcessing: true);
        }

        if (!proxyConfiguration.TryResolve(out _, out ReviewProxyProfile? profile, out string? configurationMessage) ||
            profile is null)
        {
            return Results.Problem(
                configurationMessage ?? "Review-preview rendering is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        VerifiedCollectionOriginal? original = await originalAccess.OpenVerifiedAsync(
            parsedRevisionId,
            cancellationToken);
        if (original is null)
        {
            return Results.NotFound(new
            {
                error = "No durable review proxy exists yet and the authoritative original is not already local and revision-verified. Normal viewing will not hydrate it implicitly.",
            });
        }

        await using FileStream stream = original.Stream;
        using MemoryStream source = new();
        await stream.CopyToAsync(source, cancellationToken);
        EncodedReviewProxy preview = renderer.Render(source.ToArray(), profile, cancellationToken);
        return Results.File(preview.Content, preview.ContentType);
    }
}
