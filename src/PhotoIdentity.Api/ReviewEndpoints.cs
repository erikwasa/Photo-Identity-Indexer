using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review");

        group.MapGet("/faces", GetFacesAsync);
        group.MapGet("/faces/{id}", GetFaceAsync);
        group.MapGet("/faces/{id}/image", GetFaceImageAsync);
        group.MapGet("/people", GetPeopleAsync);
        group.MapPost("/people", CreatePersonAsync);
        group.MapPost("/faces/{id}/assign", AssignAsync);
        group.MapPost("/faces/{id}/reject", RejectAsync);
        group.MapPost("/faces/{id}/undo", UndoAsync);

        return endpoints;
    }

    private static async Task<IResult> GetFacesAsync(
        SqliteReviewRepository repository,
        int offset = 0,
        int limit = 40,
        string state = CatalogueReviewStates.Unreviewed,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CatalogueReviewFacePage page = await repository.GetFacesAsync(
                offset,
                limit,
                state,
                cancellationToken);
            return Results.Ok(new ReviewFacePageResponse(
                page.Items.Select(ToResponse).ToArray(),
                page.Offset,
                page.Limit,
                page.Total));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> GetFaceAsync(
        string id,
        SqliteReviewRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        CatalogueReviewFace? face = await repository.GetFaceAsync(faceOccurrenceId, cancellationToken);
        if (face is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<CatalogueReviewAction> actions = await repository.GetActionsAsync(
            faceOccurrenceId,
            cancellationToken);
        return Results.Ok(new ReviewFaceDetailsResponse(
            ToResponse(face),
            face.MediaType,
            face.PhotoWidth,
            face.PhotoHeight,
            face.RevisionHash.ToString()[..12],
            actions.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> GetFaceImageAsync(
        string id,
        SqliteReviewRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        CatalogueReviewFace? face = await repository.GetFaceAsync(faceOccurrenceId, cancellationToken);
        if (face?.CropStoragePath is not string path || !File.Exists(path))
        {
            return Results.NotFound();
        }

        string contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png",
        };
        return Results.File(path, contentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> GetPeopleAsync(
        SqliteReviewRepository repository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogueReviewPerson> people = await repository.GetPeopleAsync(cancellationToken);
        return Results.Ok(people.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> CreatePersonAsync(
        CreatePersonRequest request,
        SqliteReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            CatalogueReviewPerson person = await repository.CreatePersonAsync(
                request.DisplayName,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Created($"/api/review/people/{person.Id}", ToResponse(person));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> AssignAsync(
        string id,
        AssignFaceRequest request,
        SqliteReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId) ||
            !TryPersonId(request.PersonId, out PersonId personId))
        {
            return BadRequest("The face occurrence or person identifier is invalid.");
        }

        try
        {
            CatalogueReviewAction action = await repository.AssignAsync(
                faceOccurrenceId,
                personId,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return Results.Ok(ToResponse(action));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> RejectAsync(
        string id,
        ReviewFaceActionRequest request,
        SqliteReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        try
        {
            CatalogueReviewAction action = await repository.RejectAsync(
                faceOccurrenceId,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return Results.Ok(ToResponse(action));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> UndoAsync(
        string id,
        ReviewFaceActionRequest request,
        SqliteReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        try
        {
            CatalogueReviewAction? action = await repository.UndoLatestAsync(
                faceOccurrenceId,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return action is null ? Results.NotFound() : Results.Ok(ToResponse(action));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static ReviewFaceResponse ToResponse(CatalogueReviewFace face) => new(
        face.Id.ToString(),
        $"/api/review/faces/{face.Id}/image",
        face.PhotoName,
        face.Ordinal,
        face.Confidence,
        face.State,
        face.Person is null ? null : ToResponse(face.Person),
        face.CreatedAtUtc);

    private static ReviewPersonResponse ToResponse(CatalogueReviewPerson person) =>
        new(person.Id.ToString(), person.DisplayName);

    private static ReviewActionResponse ToResponse(CatalogueReviewAction action) => new(
        action.Id,
        action.Kind,
        action.PersonId is PersonId personId && action.PersonDisplayName is string displayName
            ? new ReviewPersonResponse(personId.ToString(), displayName)
            : null,
        action.Actor,
        action.Note,
        action.CreatedAtUtc,
        action.ReversedAtUtc is not null,
        action.ReversesActionId);

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

    private static bool TryFaceOccurrenceId(string value, out FaceOccurrenceId id)
    {
        id = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = FaceOccurrenceId.From(parsed);
        return true;
    }

    private static bool TryPersonId(string value, out PersonId id)
    {
        id = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = PersonId.From(parsed);
        return true;
    }
}
