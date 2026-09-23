using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
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
        group.MapGet("/{id}/family", GetFamilyAsync);
        group.MapPut("/{id}/birth", SetBirthAsync);
        group.MapPost("/{id}/relationships", AddRelationshipAsync);
        group.MapDelete("/{id}/relationships/{relationshipId:long}", DeleteRelationshipAsync);
        group.MapPut("/{id}/favorite", SetFavoriteAsync);
        group.MapPut("/{id}/smart-collection-visibility", SetSmartCollectionVisibilityAsync);
        group.MapPut("/{id}/featured-face", SetFeaturedFaceAsync);
        group.MapPost("/{id}/rename", RenameAsync);
        group.MapPost("/{id}/merge", MergeAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPeopleAsync(
        IPersonMaintenanceRepository repository,
        IPersonPhotoCountRepository photoCountsRepository,
        IFavoritePeopleRepository favoritePeopleRepository,
        IPersonSmartCollectionVisibilityRepository visibilityRepository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PersonMaintenancePerson> people =
            await repository.GetPeopleAsync(cancellationToken);
        IReadOnlyDictionary<PersonId, int> photoCounts =
            await photoCountsRepository.GetActivePhotoCountsAsync(cancellationToken);
        IReadOnlySet<PersonId> favorites =
            await favoritePeopleRepository.GetFavoritePersonIdsAsync(cancellationToken);
        IReadOnlySet<PersonId> hiddenPeople =
            await visibilityRepository.GetHiddenPersonIdsAsync(cancellationToken);
        return Results.Ok(people
            .OrderByDescending(person => favorites.Contains(person.Id))
            .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Id.ToString(), StringComparer.Ordinal)
            .Select(person => ToResponse(
                person,
                photoCounts.TryGetValue(person.Id, out int photoCount) ? photoCount : 0,
                favorites.Contains(person.Id),
                hiddenPeople.Contains(person.Id)))
            .ToArray());
    }

    private static async Task<IResult> GetHistoryAsync(
        IPersonMaintenanceRepository repository,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<PersonMaintenanceAction> actions =
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
        IPersonFeaturedFaceRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            CataloguePersonRepresentativeFace? representative =
                await repository.ResolveAsync(personId, cancellationToken);
            return Results.Ok(ToResponse(personId, representative));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> GetFamilyAsync(
        string id,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        if (!TryFamilyRepository(services, out PostgresPersonFamilyMetadataRepository? repository, out IResult? unsupported))
        {
            return unsupported!;
        }

        try
        {
            PersonFamilyMetadata metadata = await repository!.GetAsync(personId, cancellationToken);
            return Results.Ok(ToResponse(metadata));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> SetBirthAsync(
        string id,
        SetPersonBirthDateRequest request,
        IServiceProvider services,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        if (!TryFamilyRepository(services, out PostgresPersonFamilyMetadataRepository? repository, out IResult? unsupported))
        {
            return unsupported!;
        }

        try
        {
            PersonBirthDate? birthDate;
            if (request.Year is null)
            {
                if (request.Month is not null || request.Day is not null || !string.IsNullOrWhiteSpace(request.Precision))
                {
                    return BadRequest("Clearing a birth date requires year, month, day and precision to be empty.");
                }

                birthDate = null;
            }
            else
            {
                birthDate = new PersonBirthDate(
                    request.Year.Value,
                    request.Month,
                    request.Day,
                    request.Precision ?? string.Empty);
            }

            await repository!.SetBirthDateAsync(
                personId,
                birthDate,
                request.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Ok(ToResponse(await repository.GetAsync(personId, cancellationToken)));
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

    private static async Task<IResult> AddRelationshipAsync(
        string id,
        AddPersonFamilyRelationshipRequest request,
        IServiceProvider services,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId) ||
            !TryPersonId(request.RelatedPersonId, out PersonId relatedPersonId))
        {
            return BadRequest("The person or related-person identifier is invalid.");
        }

        if (!TryFamilyRepository(services, out PostgresPersonFamilyMetadataRepository? repository, out IResult? unsupported))
        {
            return unsupported!;
        }

        try
        {
            PersonFamilyRelationship relationship = await repository!.AddRelationshipAsync(
                personId,
                relatedPersonId,
                request.Kind,
                request.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Results.Ok(ToResponse(relationship));
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

    private static async Task<IResult> DeleteRelationshipAsync(
        string id,
        long relationshipId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        if (!TryFamilyRepository(services, out PostgresPersonFamilyMetadataRepository? repository, out IResult? unsupported))
        {
            return unsupported!;
        }

        try
        {
            return await repository!.DeleteRelationshipAsync(personId, relationshipId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
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

    private static async Task<IResult> SetFavoriteAsync(
        string id,
        SetPersonFavoriteRequest request,
        IFavoritePeopleRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            await repository.SetFavoriteAsync(
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
        IPersonSmartCollectionVisibilityRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            await repository.SetHiddenAsync(
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
        IPersonFeaturedFaceRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

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
        IPersonMaintenanceRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryPersonId(id, out PersonId personId))
        {
            return BadRequest("The person identifier is invalid.");
        }

        try
        {
            PersonMaintenanceAction action = await repository.RenameAsync(
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
        IPersonMaintenanceRepository repository,
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
            PersonMaintenanceAction action = await repository.MergeAsync(
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

    private static bool TryFamilyRepository(
        IServiceProvider services,
        out PostgresPersonFamilyMetadataRepository? repository,
        out IResult? error)
    {
        PostgresCatalogueDatabase? database = services.GetService<PostgresCatalogueDatabase>();
        if (database is null)
        {
            repository = null;
            error = Results.Problem(
                statusCode: StatusCodes.Status501NotImplemented,
                detail: "Person family metadata is available on the PostgreSQL catalogue only.");
            return false;
        }

        repository = new PostgresPersonFamilyMetadataRepository(database);
        error = null;
        return true;
    }

    private static PersonMaintenancePersonResponse ToResponse(
        PersonMaintenancePerson person,
        int photoCount,
        bool isFavorite,
        bool hiddenFromSmartCollections) => new(
            person.Id.ToString(),
            person.DisplayName,
            photoCount,
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
        PersonMaintenanceAction action) => new(
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

    private static PersonFamilyMetadataResponse ToResponse(PersonFamilyMetadata metadata) => new(
        metadata.PersonId.ToString(),
        metadata.BirthDate is null
            ? null
            : new PersonBirthDateResponse(
                metadata.BirthDate.Year,
                metadata.BirthDate.Month,
                metadata.BirthDate.Day,
                metadata.BirthDate.Precision),
        metadata.Relationships.Select(ToResponse).ToArray());

    private static PersonFamilyRelationshipResponse ToResponse(PersonFamilyRelationship relationship) => new(
        relationship.Id,
        relationship.RelatedPersonId.ToString(),
        relationship.RelatedPersonDisplayName,
        relationship.Kind,
        relationship.Actor,
        relationship.CreatedAtUtc);

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
