using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class PersonMaintenanceEndpoints
{
    private const int RepresentativeImageSize = 360;

    public static IEndpointRouteBuilder MapPersonMaintenanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review/people");
        group.MapGet("/maintenance", GetPeopleAsync);
        group.MapGet("/maintenance/history", GetHistoryAsync);
        group.MapGet("/{id}/representative-face", GetRepresentativeFaceAsync);
        group.MapPut("/{id}/favorite", SetFavoriteAsync);
        group.MapPut("/{id}/smart-collection-visibility", SetSmartCollectionVisibilityAsync);
        group.MapPut("/{id}/featured-face", SetFeaturedFaceAsync);
        group.MapPost("/{id}/rename", RenameAsync);
        group.MapPost("/{id}/merge", MergeAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPeopleAsync(
        SqlitePersonMaintenanceRepository repository,
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CataloguePersonMaintenancePerson> people =
            await repository.GetPeopleAsync(cancellationToken);
        IReadOnlySet<PersonId> favorites = await new SqliteFavoritePeopleRepository(database)
            .GetFavoritePersonIdsAsync(cancellationToken);
        IReadOnlySet<PersonId> hiddenPeople = await new SqlitePersonSmartCollectionVisibilityRepository(database)
            .GetHiddenPersonIdsAsync(cancellationToken);
        return Results.Ok(people
            .OrderByDescending(person => favorites.Contains(person.Id))
            .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Id.ToString(), StringComparer.Ordinal)
            .Select(person => ToResponse(
                person,
                favorites.Contains(person.Id),
                hiddenPeople.Contains(person.Id)))
            .ToArray());
    }

    private static async Task<IResult> GetHistoryAsync(
        SqlitePersonMaintenanceRepository repository,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<CataloguePersonMaintenanceAction> actions =
                await repository.GetHistoryAsync(limit, cancellationToken);
            return Results.Ok(actions.Select(ToResponse).ToArray());
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> GetRepresentativeFaceAsync(
        string id,
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            CataloguePersonRepresentativeFace? representative =
                await new SqlitePersonFeaturedFaceRepository(database).ResolveAsync(
                    personId,
                    cancellationToken);
            return Results.Ok(ToResponse(personId, representative));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> SetFavoriteAsync(
        string id,
        SetPersonFavoriteRequest request,
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            await new SqliteFavoritePeopleRepository(database).SetFavoriteAsync(
                personId,
                request.IsFavorite,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> SetSmartCollectionVisibilityAsync(
        string id,
        SetPersonSmartCollectionVisibilityRequest request,
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            await new SqlitePersonSmartCollectionVisibilityRepository(database).SetHiddenAsync(
                personId,
                request.HiddenFromSmartCollections,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> SetFeaturedFaceAsync(
        string id,
        SetPersonFeaturedFaceRequest request,
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        SqlitePersonFeaturedFaceRepository repository = new(database);
        try
        {
            if (string.IsNullOrWhiteSpace(request.FaceId))
            {
                await repository.ClearFeaturedFaceAsync(personId, cancellationToken);
            }
            else
            {
                if (!TryFaceOccurrenceId(request.FaceId, out FaceOccurrenceId faceId))
                {
                    return BadRequest("The face occurrence identifier is invalid.");
                }

                await repository.SetFeaturedFaceAsync(
                    personId,
                    faceId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
            }

            CataloguePersonRepresentativeFace? representative = await repository.ResolveAsync(
                personId,
                cancellationToken);
            return Results.Ok(ToResponse(personId, representative));
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

    private static async Task<IResult> RenameAsync(
        string id,
        RenamePersonRequest request,
        SqlitePersonMaintenanceRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            CataloguePersonMaintenanceAction action = await repository.RenameAsync(
                personId,
                request.DisplayName,
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
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> MergeAsync(
        string id,
        MergePersonRequest request,
        SqlitePersonMaintenanceRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId sourcePersonId) ||
            !TryPersonId(request.TargetPersonId, out PersonId targetPersonId))
        {
            return BadRequest("The source or target person identifier is invalid.");
        }

        try
        {
            CataloguePersonMaintenanceAction action = await repository.MergeAsync(
                sourcePersonId,
                targetPersonId,
                request.ConfirmIrreversible,
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
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static PersonMaintenancePersonResponse ToResponse(
        CataloguePersonMaintenancePerson person,
        bool isFavorite,
        bool hiddenFromSmartCollections) => new(
            person.Id.ToString(),
            person.DisplayName,
            person.LabelCount,
            person.SuggestionCount,
            isFavorite,
            hiddenFromSmartCollections);

    private static PersonRepresentativeFaceResponse ToResponse(
        PersonId personId,
        CataloguePersonRepresentativeFace? representative) => new(
            personId.ToString(),
            representative?.FaceId.ToString(),
            representative is null
                ? null
                : $"/api/review/faces/{representative.FaceId}/image?size={RepresentativeImageSize}",
            representative?.IsExplicit ?? false);

    private static PersonMaintenanceActionResponse ToResponse(
        CataloguePersonMaintenanceAction action) => new(
            action.Id,
            action.Kind,
            action.PersonId.ToString(),
            action.PreviousDisplayName,
            action.TargetPersonId?.ToString(),
            action.NewDisplayName,
            action.Actor,
            action.Note,
            action.CreatedAtUtc,
            action.Reversible);

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

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });
}
