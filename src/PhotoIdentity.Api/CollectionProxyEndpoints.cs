using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public static class CollectionProxyEndpoints
{
    public static IEndpointRouteBuilder MapCollectionProxyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos/{revisionId}/proxy", GetProxyAsync);
        return endpoints;
    }

    private static async Task<IResult> GetProxyAsync(
        string revisionId,
        CollectionReviewProxyFileResolver proxyResolver,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid value) || value == Guid.Empty)
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        CollectionPhotoFile? file = await proxyResolver.ResolveAsync(
            AssetRevisionId.From(value),
            cancellationToken);
        return file is null
            ? Results.NotFound(new { error = "A durable review proxy is not available for this revision yet." })
            : Results.File(file.Path, file.ContentType, enableRangeProcessing: true);
    }
}
