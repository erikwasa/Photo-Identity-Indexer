using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public static class PhotoTagEndpoints
{
    private const string LocalMaintainerActor = "local-maintainer";
    private const string ReservedPlacesError = "The Places hierarchy is reserved for first-class location data. Use the place editor instead of ordinary tags.";

    public static IEndpointRouteBuilder MapPhotoTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/tags",
            async (SqlitePhotoTagRepository repository, CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CataloguePhotoTagDefinition> tags =
                    await repository.GetCanonicalTagsAsync(cancellationToken);
                return Results.Ok(tags
                    .Where(tag => !PhotoPlacePath.IsReservedNormalizedTagValue(tag.NormalizedValue))
                    .Select(ToResponse)
                    .ToArray());
            });

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
                    return Results.Ok(ToResponse(NonPlaceTags(tags)));
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

                if (IsReservedPlaceValue(request.Value))
                {
                    return Results.BadRequest(new PhotoTagErrorResponse(ReservedPlacesError));
                }

                try
                {
                    IReadOnlyList<CatalogueManualPhotoTag> tags = await repository.AddManualTagAsync(
                        parsedRevisionId,
                        request.Value,
                        LocalMaintainerActor,
                        cancellationToken);
                    return Results.Ok(ToResponse(NonPlaceTags(tags)));
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

                if (IsReservedPlaceValue(name))
                {
                    return Results.BadRequest(new PhotoTagErrorResponse(ReservedPlacesError));
                }

                try
                {
                    IReadOnlyList<CatalogueManualPhotoTag> tags = await repository.RemoveManualTagAsync(
                        parsedRevisionId,
                        name,
                        LocalMaintainerActor,
                        cancellationToken);
                    return Results.Ok(ToResponse(NonPlaceTags(tags)));
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

    private static IReadOnlyList<CatalogueManualPhotoTag> NonPlaceTags(
        IReadOnlyList<CatalogueManualPhotoTag> tags) =>
        tags.Where(tag => !PhotoPlacePath.IsReservedNormalizedTagValue(tag.NormalizedValue)).ToArray();

    private static bool IsReservedPlaceValue(string value)
    {
        try
        {
            return PhotoPlacePath.IsReservedTagPath(PhotoTagPath.Parse(value));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static PhotoTagResponse[] ToResponse(IReadOnlyList<CatalogueManualPhotoTag> tags) =>
        tags.Select(tag => new PhotoTagResponse(
                tag.TagId.ToString(CultureInfo.InvariantCulture),
                tag.Value,
                tag.Value,
                tag.ParentTagId?.ToString(CultureInfo.InvariantCulture),
                tag.ParentValue,
                tag.Color,
                "manual",
                tag.AssignedBy,
                tag.AssignedAtUtc))
            .ToArray();

    private static PhotoTagDefinitionResponse ToResponse(CataloguePhotoTagDefinition tag) => new(
        tag.TagId.ToString(CultureInfo.InvariantCulture),
        tag.Name,
        tag.Value,
        tag.ParentTagId?.ToString(CultureInfo.InvariantCulture),
        tag.ParentValue,
        tag.Color);

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

public sealed record PhotoTagMutationRequest(string Value);

public sealed record PhotoTagDefinitionResponse(
    string Id,
    string Name,
    string Value,
    string? ParentId,
    string? ParentValue,
    string? Color);

public sealed record PhotoTagResponse(
    string Id,
    string Name,
    string Value,
    string? ParentId,
    string? ParentValue,
    string? Color,
    string Source,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PhotoTagErrorResponse(string Error);
