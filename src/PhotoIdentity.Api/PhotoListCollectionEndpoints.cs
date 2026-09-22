using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record PhotoListCollectionRequest(
    string Name,
    string[]? RevisionIds = null);

public sealed record PhotoListCollectionResponse(
    string Id,
    string Name,
    string[] RevisionIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public static class PhotoListCollectionEndpoints
{
    public static IEndpointRouteBuilder MapPhotoListCollectionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/photo-list-collections", CreateAsync);
        endpoints.MapGet("/api/photo-list-collections", ListAsync);
        endpoints.MapGet("/api/photo-list-collections/{id:guid}", GetAsync);
        endpoints.MapPut("/api/photo-list-collections/{id:guid}", UpdateAsync);
        endpoints.MapDelete("/api/photo-list-collections/{id:guid}", DeleteAsync);
        endpoints.MapPost(
            "/api/photo-list-collections/{id:guid}/slideshow-snapshot",
            CreateSlideshowSnapshotAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        PhotoListCollectionRequest request,
        IPhotoListCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            PhotoListCollectionDefinition definition = await repository.CreateAsync(
                request.Name,
                ParseRevisionIds(request.RevisionIds),
                cancellationToken);
            return Results.Created(
                $"/api/photo-list-collections/{definition.Id}",
                ToResponse(definition));
        }
        catch (PhotoListCollectionNameConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (PhotoListCollectionRevisionUnavailableException exception)
        {
            return Results.BadRequest(new
            {
                error = exception.Message,
                revisionIds = exception.RevisionIds.Select(item => item.ToString()).ToArray(),
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ListAsync(
        IPhotoListCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PhotoListCollectionDefinition> definitions =
            await repository.ListAsync(cancellationToken);
        return Results.Ok(definitions.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IPhotoListCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        PhotoListCollectionDefinition? definition =
            await repository.GetAsync(collectionId, cancellationToken);
        return definition is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(definition));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        PhotoListCollectionRequest request,
        IPhotoListCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        try
        {
            PhotoListCollectionDefinition? definition = await repository.UpdateAsync(
                collectionId,
                request.Name,
                ParseRevisionIds(request.RevisionIds),
                cancellationToken);
            return definition is null
                ? Results.NotFound()
                : Results.Ok(ToResponse(definition));
        }
        catch (PhotoListCollectionNameConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (PhotoListCollectionRevisionUnavailableException exception)
        {
            return Results.BadRequest(new
            {
                error = exception.Message,
                revisionIds = exception.RevisionIds.Select(item => item.ToString()).ToArray(),
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IPhotoListCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        return await repository.DeleteAsync(collectionId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> CreateSlideshowSnapshotAsync(
        Guid id,
        IPhotoListCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out PhotoListCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        PhotoListCollectionSlideshowSnapshot? snapshot =
            await repository.CreateSlideshowSnapshotAsync(
                collectionId,
                cancellationToken);
        if (snapshot is null)
        {
            return Results.NotFound();
        }

        SmartCollectionSlideshowSnapshotItemResponse[] items = snapshot.RevisionIds
            .Select(
                revisionId =>
                    new SmartCollectionSlideshowSnapshotItemResponse(
                        revisionId.ToString()))
            .ToArray();

        return Results.Ok(new SmartCollectionSlideshowSnapshotResponse(
            snapshot.CollectionId.ToString(),
            snapshot.CollectionName,
            snapshot.CreatedAtUtc,
            items,
            items.Length));
    }

    private static PhotoListCollectionResponse ToResponse(
        PhotoListCollectionDefinition definition) =>
        new(
            definition.Id.ToString(),
            definition.Name,
            definition.RevisionIds.Select(item => item.ToString()).ToArray(),
            definition.CreatedAtUtc,
            definition.UpdatedAtUtc);

    private static AssetRevisionId[] ParseRevisionIds(string[]? values)
    {
        List<AssetRevisionId> revisionIds = [];
        foreach (string value in values ?? [])
        {
            if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException(
                    $"Revision identifier '{value}' is not a valid non-empty GUID.",
                    nameof(values));
            }

            revisionIds.Add(AssetRevisionId.From(parsed));
        }

        return PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds);
    }

    private static bool TryGetId(
        Guid value,
        out PhotoListCollectionId id,
        out IResult? error)
    {
        if (value == Guid.Empty)
        {
            id = default;
            error = Results.BadRequest(new
            {
                error = "Photo-list collection identifier cannot be empty.",
            });
            return false;
        }

        id = PhotoListCollectionId.From(value);
        error = null;
        return true;
    }
}
