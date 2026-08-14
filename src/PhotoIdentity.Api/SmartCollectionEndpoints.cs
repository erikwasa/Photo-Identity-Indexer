using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public sealed record SmartCollectionLocationRequest(
    double South,
    double West,
    double North,
    double East);

public sealed record SmartCollectionQueryRequest(
    string[]? People = null,
    string? PeopleMatch = null,
    string[]? Tags = null,
    string? TagMatch = null,
    SmartCollectionLocationRequest? Location = null,
    string? Taken = null,
    int Offset = 0,
    int Limit = 40);

public static class SmartCollectionEndpoints
{
    public static IEndpointRouteBuilder MapSmartCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/smart-collections/query", QueryAsync);
        return endpoints;
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

            return Results.Ok(new
            {
                items = page.Items.Select(photo => new
                {
                    revisionId = photo.RevisionId.ToString(),
                    assetId = photo.AssetId.ToString(),
                    thumbnailUrl = $"/api/collections/photos/{photo.RevisionId}/thumbnail",
                    previewUrl = $"/api/collections/photos/{photo.RevisionId}/preview",
                    originalUrl = $"/api/collections/photos/{photo.RevisionId}/original",
                    observedAtUtc = photo.ObservedAtUtc,
                    mediaType = photo.MediaType,
                    width = photo.Width,
                    height = photo.Height,
                    takenAtLocal = photo.TakenAtLocal,
                    latitude = photo.Latitude,
                    longitude = photo.Longitude,
                }).ToArray(),
                page.Offset,
                page.Limit,
                page.Total,
                filter = new
                {
                    people = page.Filter.People.Select(person => person.ToString()).ToArray(),
                    peopleMatch = page.Filter.PeopleMatch,
                    tags = page.Filter.Tags,
                    tagMatch = page.Filter.TagMatch,
                    location = page.Filter.Location,
                    taken = page.Filter.Taken is null
                        ? null
                        : new
                        {
                            from = page.Filter.Taken.From.ToString("yyyy-MM-dd"),
                            to = page.Filter.Taken.To.ToString("yyyy-MM-dd"),
                        },
                },
            });
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            FormatException or
            ArgumentOutOfRangeException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static SmartCollectionFilter ToFilter(SmartCollectionQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        PersonId[] people = (request.People ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(ParsePersonId)
            .Distinct()
            .ToArray();

        SmartCollectionGeoBounds? location = request.Location is null
            ? null
            : new SmartCollectionGeoBounds(
                request.Location.South,
                request.Location.West,
                request.Location.North,
                request.Location.East);
        SmartCollectionDateRange? taken = string.IsNullOrWhiteSpace(request.Taken)
            ? null
            : SmartCollectionDateRange.Parse(request.Taken);

        return new SmartCollectionFilter(
            people,
            request.PeopleMatch,
            request.Tags,
            request.TagMatch,
            location,
            taken);
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
