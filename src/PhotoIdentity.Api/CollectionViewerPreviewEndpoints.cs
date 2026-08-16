using PhotoIdentity.Core.Identifiers;

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
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid value) || value == Guid.Empty)
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        AssetRevisionId parsedRevisionId = AssetRevisionId.From(value);

        // Photo Details should always prefer the authoritative original when it is already local and
        // revision-verified. OpenVerifiedAsync never hydrates an online-only source implicitly.
        VerifiedCollectionOriginal? original = await originalAccess.OpenVerifiedAsync(
            parsedRevisionId,
            cancellationToken);
        if (original is not null)
        {
            return Results.File(original.Stream, original.ContentType, enableRangeProcessing: true);
        }

        CollectionPhotoFile? proxy = await proxyResolver.ResolveAsync(parsedRevisionId, cancellationToken);
        if (proxy is not null)
        {
            return Results.File(proxy.Path, proxy.ContentType, enableRangeProcessing: true);
        }

        return Results.NotFound(new
        {
            error = "No durable review proxy exists yet and the authoritative original is not already local and revision-verified. Normal viewing will not hydrate it implicitly.",
        });
    }
}
