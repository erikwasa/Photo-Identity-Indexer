using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public sealed record SmartCollectionLocationRequest(
    double? South = null,
    double? West = null,
    double? North = null,
    double? East = null,
    string? Place = null);

public sealed record SmartCollectionQueryRequest(
    string[]? People = null,
    string? PeopleMatch = null,
    string[]? Tags = null,
    string? TagMatch = null,
    SmartCollectionLocationRequest? Location = null,
    string? Taken = null,
    int Offset = 0,
    int Limit = 40);

public sealed record SmartCollectionDefinitionRequest(
    string Name,
    string[]? People = null,
    string? PeopleMatch = null,
    string[]? Tags = null,
    string? TagMatch = null,
    SmartCollectionLocationRequest? Location = null,
    string? Taken = null);

public sealed record SmartCollectionDateRangeResponse(
    string From,
    string To);

public sealed record SmartCollectionFilterResponse(
    string[] People,
    string PeopleMatch,
    string[] Tags,
    string TagMatch,
    SmartCollectionLocationRequest? Location,
    SmartCollectionDateRangeResponse? Taken);

public sealed record SmartCollectionDefinitionResponse(
    string Id,
    string Name,
    SmartCollectionFilterResponse Filter,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SmartCollectionPhotoResponse(
    string RevisionId,
    string AssetId,
    string ThumbnailUrl,
    string PreviewUrl,
    string OriginalUrl,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    DateTime? TakenAtLocal,
    double? Latitude,
    double? Longitude);

public sealed record SmartCollectionPageResponse(
    SmartCollectionPhotoResponse[] Items,
    int Offset,
    int Limit,
    int Total,
    SmartCollectionFilterResponse Filter,
    string? CollectionId = null,
    string? CollectionName = null);

public static class SmartCollectionEndpoints
{
    public static IEndpointRouteBuilder MapSmartCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/smart-collections/query", QueryAsync);
        endpoints.MapPost("/api/smart-collections", CreateAsync);
        endpoints.MapGet("/api/smart-collections", ListAsync);
        endpoints.MapGet("/api/smart-collections/{id:guid}", GetAsync);
        endpoints.MapPut("/api/smart-collections/{id:guid}", UpdateAsync);
        endpoints.MapDelete("/api/smart-collections/{id:guid}", DeleteAsync);
        endpoints.MapGet("/api/smart-collections/{id:guid}/query", QuerySavedAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        SmartCollectionDefinitionRequest request,
        SqliteSmartCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            SmartCollectionDefinition definition = await repository.CreateAsync(
                request.Name,
                ToFilter(request),
                cancellationToken);
            return Results.Created(
                $"/api/smart-collections/{definition.Id}",
                ToDefinitionResponse(definition));
        }
        catch (SmartCollectionNameConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ListAsync(
        SqliteSmartCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SmartCollectionDefinition> definitions =
            await repository.ListAsync(cancellationToken);
        return Results.Ok(definitions.Select(ToDefinitionResponse).ToArray());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        SqliteSmartCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        SmartCollectionDefinition? definition = await repository.GetAsync(collectionId, cancellationToken);
        return definition is null
            ? Results.NotFound()
            : Results.Ok(ToDefinitionResponse(definition));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        SmartCollectionDefinitionRequest request,
        SqliteSmartCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        try
        {
            SmartCollectionDefinition? existing = await repository.GetAsync(collectionId, cancellationToken);
            if (existing is null)
            {
                return Results.NotFound();
            }

            string? fallbackLocationPlace = request.Location?.Place is null
                ? existing.Filter.LocationPlace
                : null;
            SmartCollectionDefinition? definition = await repository.UpdateAsync(
                collectionId,
                request.Name,
                ToFilter(request, fallbackLocationPlace),
                cancellationToken);
            return definition is null
                ? Results.NotFound()
                : Results.Ok(ToDefinitionResponse(definition));
        }
        catch (SmartCollectionNameConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        SqliteSmartCollectionRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        return await repository.DeleteAsync(collectionId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> QuerySavedAsync(
        Guid id,
        int? offset,
        int? limit,
        SqliteSmartCollectionRepository definitions,
        SqliteSmartCollectionQueryRepository query,
        CancellationToken cancellationToken)
    {
        if (!TryGetId(id, out SmartCollectionId collectionId, out IResult? error))
        {
            return error!;
        }

        SmartCollectionDefinition? definition = await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        try
        {
            SmartCollectionPhotoPage page = await query.QueryAsync(
                definition.Filter,
                offset ?? 0,
                limit ?? 40,
                cancellationToken);
            return Results.Ok(ToPageResponse(page, definition));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> QueryAsync(
        SmartCollectionQueryRequest request,
        SqliteSmartCollectionQueryRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            SmartCollectionFilter filter = ToFilter(request);
            SmartCollectionPhotoPage page = await repository.QueryAsync(
                filter,
                request.Offset,
                request.Limit,
                cancellationToken);
            return Results.Ok(ToPageResponse(page));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static SmartCollectionFilter ToFilter(SmartCollectionQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ToFilter(
            request.People,
            request.PeopleMatch,
            request.Tags,
            request.TagMatch,
            request.Location,
            request.Taken,
            fallbackLocationPlace: null);
    }

    private static SmartCollectionFilter ToFilter(
        SmartCollectionDefinitionRequest request,
        string? fallbackLocationPlace = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ToFilter(
            request.People,
            request.PeopleMatch,
            request.Tags,
            request.TagMatch,
            request.Location,
            request.Taken,
            fallbackLocationPlace);
    }

    private static SmartCollectionFilter ToFilter(
        string[]? people,
        string? peopleMatch,
        string[]? tags,
        string? tagMatch,
        SmartCollectionLocationRequest? location,
        string? taken,
        string? fallbackLocationPlace)
    {
        ValidateGenericTags(tags);

        PersonId[] parsedPeople = (people ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(ParsePersonId)
            .Distinct()
            .ToArray();

        SmartCollectionGeoBounds? parsedLocation = ParseBounds(location);
        SmartCollectionDateRange? parsedTaken = string.IsNullOrWhiteSpace(taken)
            ? null
            : SmartCollectionDateRange.Parse(taken);
        string? locationPlace = location?.Place is null
            ? fallbackLocationPlace
            : location.Place;

        return new SmartCollectionFilter(
            parsedPeople,
            peopleMatch,
            tags,
            tagMatch,
            parsedLocation,
            parsedTaken,
            locationPlace);
    }

    private static void ValidateGenericTags(string[]? tags)
    {
        foreach (string value in tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            PhotoTagPath path = PhotoTagPath.Parse(value);
            if (PhotoPlacePath.IsReservedTagPath(path))
            {
                throw new ArgumentException(
                    $"The reserved '{PhotoPlacePath.RootDisplayName}' hierarchy belongs to Location, not Tags.",
                    nameof(tags));
            }
        }
    }

    private static SmartCollectionGeoBounds? ParseBounds(SmartCollectionLocationRequest? location)
    {
        if (location is null)
        {
            return null;
        }

        bool any = location.South.HasValue || location.West.HasValue ||
            location.North.HasValue || location.East.HasValue;
        bool all = location.South.HasValue && location.West.HasValue &&
            location.North.HasValue && location.East.HasValue;
        if (any && !all)
        {
            throw new ArgumentException(
                "Location GPS bounds require south, west, north and east together.",
                nameof(location));
        }

        return all
            ? new SmartCollectionGeoBounds(
                location.South!.Value,
                location.West!.Value,
                location.North!.Value,
                location.East!.Value)
            : null;
    }

    private static SmartCollectionDefinitionResponse ToDefinitionResponse(
        SmartCollectionDefinition definition) => new(
        definition.Id.ToString(),
        definition.Name,
        ToFilterResponse(definition.Filter),
        definition.CreatedAtUtc,
        definition.UpdatedAtUtc);

    private static SmartCollectionPageResponse ToPageResponse(
        SmartCollectionPhotoPage page,
        SmartCollectionDefinition? definition = null) => new(
        page.Items.Select(photo => new SmartCollectionPhotoResponse(
            photo.RevisionId.ToString(),
            photo.AssetId.ToString(),
            $"/api/collections/photos/{photo.RevisionId}/thumbnail",
            $"/api/collections/photos/{photo.RevisionId}/preview",
            $"/api/collections/photos/{photo.RevisionId}/original",
            photo.ObservedAtUtc,
            photo.MediaType,
            photo.Width,
            photo.Height,
            photo.TakenAtLocal,
            photo.Latitude,
            photo.Longitude)).ToArray(),
        page.Offset,
        page.Limit,
        page.Total,
        ToFilterResponse(page.Filter),
        definition?.Id.ToString(),
        definition?.Name);

    private static SmartCollectionFilterResponse ToFilterResponse(SmartCollectionFilter filter) => new(
        filter.People.Select(person => person.ToString()).ToArray(),
        filter.PeopleMatch,
        filter.Tags.ToArray(),
        filter.TagMatch,
        filter.Location is null && filter.LocationPlace is null
            ? null
            : new SmartCollectionLocationRequest(
                filter.Location?.South,
                filter.Location?.West,
                filter.Location?.North,
                filter.Location?.East,
                filter.LocationPlace is null
                    ? null
                    : PhotoPlacePath.Parse(filter.LocationPlace).DisplayValue),
        filter.Taken is null
            ? null
            : new SmartCollectionDateRangeResponse(
                filter.Taken.From.ToString("yyyy-MM-dd"),
                filter.Taken.To.ToString("yyyy-MM-dd")));

    private static bool TryGetId(
        Guid id,
        out SmartCollectionId collectionId,
        out IResult? error)
    {
        if (id == Guid.Empty)
        {
            collectionId = default;
            error = Results.BadRequest(new { error = "Smart collection identifier cannot be empty." });
            return false;
        }

        collectionId = SmartCollectionId.From(id);
        error = null;
        return true;
    }

    private static PersonId ParsePersonId(string value)
    {
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException($"Person identifier '{value}' is invalid.", nameof(value));
        }

        return PersonId.From(parsed);
    }
}
