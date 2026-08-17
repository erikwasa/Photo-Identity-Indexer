using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public static class CollectionViewerPreviewEndpoints
{
    public static IEndpointRouteBuilder MapCollectionViewerPreviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos/{revisionId}/viewer-preview", GetViewerPreviewAsync);
        endpoints.MapGet("/api/collections/photos/{revisionId}/viewer-proxy", GetViewerProxyAsync);
        return endpoints;
    }

    private static async Task<IResult> GetViewerPreviewAsync(
        string revisionId,
        CollectionReviewProxyFileResolver proxyResolver,
        CollectionOriginalAccessService originalAccess,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return InvalidRevision();
        }

        // Photo Details should prefer a verified local original only when the browser can render
        // its media type directly. OpenVerifiedAsync never hydrates an online-only source implicitly.
        VerifiedCollectionOriginal? original = await originalAccess.OpenVerifiedAsync(
            parsedRevisionId,
            cancellationToken);
        bool unsupportedLocalOriginal = original is not null &&
            !BrowserImageContentTypes.CanRender(original.ContentType);
        if (original is not null && !unsupportedLocalOriginal)
        {
            return Results.File(original.Stream, original.ContentType, enableRangeProcessing: true);
        }

        if (original is not null)
        {
            await original.Stream.DisposeAsync();
        }

        CollectionPhotoFile? proxy = await proxyResolver.ResolveAsync(parsedRevisionId, cancellationToken);
        if (proxy is not null)
        {
            return Results.File(proxy.Path, proxy.ContentType, enableRangeProcessing: true);
        }

        return Results.NotFound(new
        {
            error = unsupportedLocalOriginal
                ? "The authoritative original is local and revision-verified, but its format is not directly browser-renderable and no durable review proxy exists yet."
                : "No durable review proxy exists yet and the authoritative original is not already local and revision-verified. Normal viewing will not hydrate it implicitly.",
        });
    }

    private static async Task<IResult> GetViewerProxyAsync(
        string revisionId,
        CollectionReviewProxyFileResolver proxyResolver,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return InvalidRevision();
        }

        CollectionPhotoFile? proxy = await proxyResolver.ResolveAsync(parsedRevisionId, cancellationToken);
        return proxy is null
            ? Results.NotFound(new { error = "No durable review proxy exists for this photo." })
            : Results.File(proxy.Path, proxy.ContentType, enableRangeProcessing: true);
    }

    private static bool TryRevisionId(string revisionId, out AssetRevisionId parsedRevisionId)
    {
        parsedRevisionId = default;
        if (!Guid.TryParse(revisionId, out Guid value) || value == Guid.Empty)
        {
            return false;
        }

        parsedRevisionId = AssetRevisionId.From(value);
        return true;
    }

    private static IResult InvalidRevision() => Results.BadRequest(new
    {
        error = "The asset revision identifier is invalid.",
    });
}
