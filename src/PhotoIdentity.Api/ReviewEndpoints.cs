using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review");

        group.MapGet("/faces", GetFacesAsync);
        group.MapGet("/filters", GetFiltersAsync);
        group.MapGet("/faces/{id}", GetFaceAsync);
        group.MapGet("/faces/{id}/image", GetFaceImageAsync);
        group.MapGet("/people", GetPeopleAsync);
        group.MapPost("/people", CreatePersonAsync);
        group.MapPost("/faces/{id}/assign", AssignAsync);
        group.MapPost("/faces/{id}/unknown", MarkUnknownAsync);
        group.MapPost("/faces/{id}/reject", RejectAsync);
        group.MapPost("/faces/{id}/undo", UndoAsync);

        return endpoints;
    }

    private static async Task<IResult> GetFacesAsync(
        SqliteReviewFilterRepository repository,
        int offset = 0,
        int limit = 40,
        string state = CatalogueReviewStates.Unreviewed,
        string? processingRunId = null,
        string? modelId = null,
        string? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        if (!TryProcessingRunId(processingRunId, out ProcessingRunId? parsedRunId) ||
            !TryModelRevision(modelId, modelHash, out ModelId? parsedModelId, out Sha256Digest? parsedModelHash))
        {
            return BadRequest("The processing run or model revision filter is invalid.");
        }

        try
        {
            CatalogueReviewFacePage page = await repository.GetFacesAsync(
                offset,
                limit,
                state,
                parsedRunId,
                parsedModelId,
                parsedModelHash,
                sort,
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

    private static async Task<IResult> GetFiltersAsync(
        SqliteReviewFilterRepository repository,
        CancellationToken cancellationToken)
    {
        CatalogueReviewFilterOptions options = await repository.GetOptionsAsync(cancellationToken);
        return Results.Ok(new ReviewFilterOptionsResponse(
            options.ProcessingRuns.Select(run => new ReviewProcessingRunFilterResponse(
                run.Id.ToString(),
                run.Status,
                run.StartedAtUtc,
                run.CompletedAtUtc,
                run.FaceCount)).ToArray(),
            options.ModelRevisions.Select(model => new ReviewModelRevisionFilterResponse(
                model.ModelId.ToString(),
                model.ModelHash.ToString(),
                model.GeneratedAtUtc,
                model.FaceCount)).ToArray()));
    }

    private static async Task<IResult> GetFaceAsync(
        string id,
        SqliteReviewRepository repository,
        SqliteReviewFilterRepository filterRepository,
        string state = "all",
        string? processingRunId = null,
        string? modelId = null,
        string? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        if (!TryProcessingRunId(processingRunId, out ProcessingRunId? parsedRunId) ||
            !TryModelRevision(modelId, modelHash, out ModelId? parsedModelId, out Sha256Digest? parsedModelHash))
        {
            return BadRequest("The processing run or model revision filter is invalid.");
        }

        CatalogueReviewFace? face = await repository.GetFaceAsync(faceOccurrenceId, cancellationToken);
        if (face is null)
        {
            return Results.NotFound();
        }

        try
        {
            IReadOnlyList<CatalogueReviewAction> actions = await repository.GetActionsAsync(
                faceOccurrenceId,
                cancellationToken);
            CatalogueReviewFaceNavigation? navigation = await filterRepository.GetNavigationAsync(
                faceOccurrenceId,
                state,
                parsedRunId,
                parsedModelId,
                parsedModelHash,
                sort,
                cancellationToken);
            return Results.Ok(new ReviewFaceDetailsResponse(
                ToResponse(face),
                face.MediaType,
                face.PhotoWidth,
                face.PhotoHeight,
                face.RevisionHash.ToString()[..12],
                actions.Select(ToResponse).ToArray(),
                navigation is null
                    ? null
                    : new ReviewFaceNavigationResponse(
                        navigation.PreviousFaceId?.ToString(),
                        navigation.NextFaceId?.ToString(),
                        navigation.Position,
                        navigation.Total,
                        navigation.Sort)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> GetFaceImageAsync(
        string id,
        SqliteReviewRepository repository,
        ReviewCropFileResolver cropFileResolver,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        CatalogueReviewFace? face = await repository.GetFaceAsync(faceOccurrenceId, cancellationToken);
        if (face?.CropStoragePath is not string storagePath)
        {
            return Results.NotFound();
        }

        string? path = await cropFileResolver.ResolveAsync(storagePath, cancellationToken);
        if (path is null)
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
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogueReviewPerson> people = await repository.GetPeopleAsync(cancellationToken);
        IReadOnlySet<PersonId> favorites = await new SqliteFavoritePeopleRepository(database)
            .GetFavoritePersonIdsAsync(cancellationToken);
        return Results.Ok(people
            .OrderByDescending(person => favorites.Contains(person.Id))
            .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Id.ToString(), StringComparer.Ordinal)
            .Select(person => ToResponse(person, favorites.Contains(person.Id)))
            .ToArray());
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

    private static Task<IResult> MarkUnknownAsync(
        string id,
        ReviewFaceActionRequest request,
        SqliteReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        RecordPersonlessDecisionAsync(
            id,
            request,
            repository.MarkUnknownAsync,
            timeProvider,
            cancellationToken);

    private static Task<IResult> RejectAsync(
        string id,
        ReviewFaceActionRequest request,
        SqliteReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        RecordPersonlessDecisionAsync(
            id,
            request,
            repository.RejectAsync,
            timeProvider,
            cancellationToken);

    private static async Task<IResult> RecordPersonlessDecisionAsync(
        string id,
        ReviewFaceActionRequest request,
        Func<FaceOccurrenceId, string, DateTimeOffset, string?, CancellationToken, Task<CatalogueReviewAction>> action,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        try
        {
            CatalogueReviewAction result = await action(
                faceOccurrenceId,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return Results.Ok(ToResponse(result));
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

    private static ReviewPersonResponse ToResponse(CatalogueReviewPerson person, bool isFavorite = false) =>
        new(person.Id.ToString(), person.DisplayName, isFavorite);

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

    private static bool TryProcessingRunId(string? value, out ProcessingRunId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = ProcessingRunId.From(parsed);
        return true;
    }

    private static bool TryModelRevision(
        string? modelId,
        string? modelHash,
        out ModelId? parsedModelId,
        out Sha256Digest? parsedModelHash)
    {
        parsedModelId = null;
        parsedModelHash = null;
        if (string.IsNullOrWhiteSpace(modelId) && string.IsNullOrWhiteSpace(modelHash))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(modelHash))
        {
            return false;
        }

        try
        {
            parsedModelId = new ModelId(modelId);
            parsedModelHash = new Sha256Digest(modelHash);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
