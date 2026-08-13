using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public static class PhotoTagEndpoints
{
    private const string LocalMaintainerActor = "local-maintainer";

    public static IEndpointRouteBuilder MapPhotoTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/collections/photos/{revisionId}/tags",
            async (string revisionId, SqlitePhotoTagRepository repository, CancellationToken cancellationToken) =>
            {
                if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
                {
                    return Results.BadRequest(new PhotoTagErrorResponse("Invalid asset revision id."));
                }

                try
                {
                    IReadOnlyList<CatalogueManualPhotoTag> tags =
                        await repository.GetManualTagsAsync(parsedRevisionId, cancellationToken);
                    return Results.Ok(ToResponse(tags));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new PhotoTagErrorResponse(exception.Message));
                }
            });

        endpoints.MapPost(
            "/api/collections/photos/{revisionId}/tags",
            async (string revisionId, PhotoTagMutationRequest request, SqlitePhotoTagRepository repository, CancellationToken cancellationToken) =>
            {
                if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
                {
                    return Results.BadRequest(new PhotoTagErrorResponse("Invalid asset revision id."));
                }

                try
                {
                    IReadOnlyList<CatalogueManualPhotoTag> tags = await repository.AddManualTagAsync(
                        parsedRevisionId,
                        request.Name,
                        LocalMaintainerActor,
                        cancellationToken);
                    return Results.Ok(ToResponse(tags));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new PhotoTagErrorResponse(exception.Message));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new PhotoTagErrorResponse(exception.Message));
                }
            });

        endpoints.MapDelete(
            "/api/collections/photos/{revisionId}/tags",
            async (string revisionId, string name, SqlitePhotoTagRepository repository, CancellationToken cancellationToken) =>
            {
                if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
                {
                    return Results.BadRequest(new PhotoTagErrorResponse("Invalid asset revision id."));
                }

                try
                {
                    IReadOnlyList<CatalogueManualPhotoTag> tags = await repository.RemoveManualTagAsync(
                        parsedRevisionId,
                        name,
                        LocalMaintainerActor,
                        cancellationToken);
                    return Results.Ok(ToResponse(tags));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new PhotoTagErrorResponse(exception.Message));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new PhotoTagErrorResponse(exception.Message));
                }
            });

        return endpoints;
    }

    private static PhotoTagResponse[] ToResponse(IReadOnlyList<CatalogueManualPhotoTag> tags) =>
        tags.Select(tag => new PhotoTagResponse(
                tag.DisplayName,
                "manual",
                tag.AssignedBy,
                tag.AssignedAtUtc))
            .ToArray();

    private static bool TryParseRevisionId(string value, out AssetRevisionId revisionId)
    {
        if (Guid.TryParse(value, out Guid guid))
        {
            revisionId = AssetRevisionId.From(guid);
            return true;
        }

        revisionId = default;
        return false;
    }
}

public sealed record PhotoTagMutationRequest(string Name);

public sealed record PhotoTagResponse(
    string Name,
    string Source,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PhotoTagErrorResponse(string Error);
