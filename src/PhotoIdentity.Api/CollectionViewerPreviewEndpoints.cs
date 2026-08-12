using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

public static class CollectionViewerPreviewEndpoints
{
    // The viewer fallback is transient and is never registered as a durable proxy profile.
    // These settings preserve the review-sized rendering used by WI-0054 while allowing an
    // already-local verified original to be viewed even when durable proxy generation is not configured.
    private static readonly ReviewProxyProfile TransientViewerProfile = new(
        "viewer-preview-transient-v1",
        maximumLongEdge: 1600,
        jpegQuality: 78);

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

        ReviewProxyProfile renderProfile =
            proxyConfiguration.TryResolve(out _, out ReviewProxyProfile? configuredProfile, out _) &&
            configuredProfile is not null
                ? configuredProfile
                : TransientViewerProfile;

        await using FileStream stream = original.Stream;
        using MemoryStream source = new();
        await stream.CopyToAsync(source, cancellationToken);
        EncodedReviewProxy preview = renderer.Render(source.ToArray(), renderProfile, cancellationToken);
        return Results.File(preview.Content, preview.ContentType);
    }
}
