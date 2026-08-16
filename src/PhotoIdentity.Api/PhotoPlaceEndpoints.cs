using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public static class PhotoPlaceEndpoints
{
    private const string LocalMaintainerActor = "local-maintainer";

    public static IEndpointRouteBuilder MapPhotoPlaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/places",
            async (SqlitePhotoPlaceRepository repository, CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CataloguePlaceDefinition> places =
                    await repository.GetDefinitionsAsync(cancellationToken);
                return Results.Ok(places.Select(ToResponse).ToArray());
            });

        endpoints.MapGet(
            "/api/places/migration-conflicts",
            async (SqlitePhotoPlaceRepository repository, CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CataloguePlaceMigrationConflict> conflicts =
                    await repository.GetMigrationConflictsAsync(cancellationToken);
                return Results.Ok(conflicts.Select(ToResponse).ToArray());
            });

        endpoints.MapGet(
            "/api/collections/photos/{revisionId}/place",
            async (string revisionId, SqlitePhotoPlaceRepository repository, CancellationToken cancellationToken) =>
            {
                if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
                {
                    return Results.BadRequest(new PhotoPlaceErrorResponse("Invalid asset revision id."));
                }

                try
                {
                    return Results.Ok(ToResponse(
                        await repository.GetStateAsync(parsedRevisionId, cancellationToken)));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new PhotoPlaceErrorResponse(exception.Message));
                }
            });

        endpoints.MapPut(
            "/api/collections/photos/{revisionId}/place",
            async (string revisionId, PhotoPlaceMutationRequest request, SqlitePhotoPlaceRepository repository, CancellationToken cancellationToken) =>
            {
                if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
                {
                    return Results.BadRequest(new PhotoPlaceErrorResponse("Invalid asset revision id."));
                }

                try
                {
                    return Results.Ok(ToResponse(await repository.SetManualPlaceAsync(
                        parsedRevisionId,
                        request.Value,
                        LocalMaintainerActor,
                        cancellationToken)));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new PhotoPlaceErrorResponse(exception.Message));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new PhotoPlaceErrorResponse(exception.Message));
                }
            });

        endpoints.MapDelete(
            "/api/collections/photos/{revisionId}/place",
            async (string revisionId, SqlitePhotoPlaceRepository repository, CancellationToken cancellationToken) =>
            {
                if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
                {
                    return Results.BadRequest(new PhotoPlaceErrorResponse("Invalid asset revision id."));
                }

                try
                {
                    return Results.Ok(ToResponse(await repository.ClearManualPlaceAsync(
                        parsedRevisionId,
                        LocalMaintainerActor,
                        cancellationToken)));
                }
                catch (KeyNotFoundException exception)
                {
                    return Results.NotFound(new PhotoPlaceErrorResponse(exception.Message));
                }
            });

        return endpoints;
    }

    private static PhotoPlaceDefinitionResponse ToResponse(CataloguePlaceDefinition place) => new(
        place.TagId.ToString(CultureInfo.InvariantCulture),
        place.Name,
        place.Value,
        place.ParentTagId?.ToString(CultureInfo.InvariantCulture),
        place.ParentValue);

    private static PhotoPlaceStateResponse ToResponse(CataloguePhotoPlaceState state) => new(
        state.RevisionId.ToString(),
        state.Place is null
            ? null
            : new PhotoPlaceResponse(
                state.Place.TagId.ToString(CultureInfo.InvariantCulture),
                state.Place.Name,
                state.Place.Value,
                state.Place.SourceKind,
                state.Place.AssignedBy,
                state.Place.AssignedAtUtc),
        state.MigrationConflict is null ? null : ToResponse(state.MigrationConflict));

    private static PhotoPlaceMigrationConflictResponse ToResponse(CataloguePlaceMigrationConflict conflict) => new(
        conflict.RevisionId.ToString(),
        conflict.CandidateValues,
        conflict.DetectedAtUtc);

    private static bool TryParseRevisionId(string value, out AssetRevisionId revisionId)
    {
        if (Guid.TryParse(value, out Guid guid) && guid != Guid.Empty)
        {
            revisionId = AssetRevisionId.From(guid);
            return true;
        }

        revisionId = default;
        return false;
    }
}

public sealed record PhotoPlaceMutationRequest(string Value);

public sealed record PhotoPlaceDefinitionResponse(
    string Id,
    string Name,
    string Value,
    string? ParentId,
    string? ParentValue);

public sealed record PhotoPlaceResponse(
    string Id,
    string Name,
    string Value,
    string Source,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PhotoPlaceStateResponse(
    string RevisionId,
    PhotoPlaceResponse? Place,
    PhotoPlaceMigrationConflictResponse? MigrationConflict);

public sealed record PhotoPlaceMigrationConflictResponse(
    string RevisionId,
    IReadOnlyList<string> CandidateValues,
    DateTimeOffset DetectedAtUtc);

public sealed record PhotoPlaceErrorResponse(string Error);
