using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos", GetPhotosAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPhotosAsync(
        SqliteCollectionQueryRepository repository,
        string? people = null,
        string match = CatalogueCollectionMatchModes.All,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        double? minimumConfidence = null,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default)
    {
        if (!TryPeople(people, out PersonId[] personIds))
        {
            return Results.BadRequest(new
            {
                error = "Supply one or more comma-separated, non-empty person identifiers in the 'people' query parameter.",
            });
        }

        try
        {
            CatalogueCollectionPhotoPage page = await repository.QueryConfirmedPhotosAsync(
                personIds,
                match,
                fromUtc,
                toUtc,
                minimumConfidence,
                offset,
                limit,
                cancellationToken);
            return Results.Ok(new CollectionPhotoPageResponse(
                page.Items.Select(ToResponse).ToArray(),
                page.Offset,
                page.Limit,
                page.Total,
                new CollectionQueryResponse(
                    personIds.Select(value => value.ToString()).ToArray(),
                    page.MatchMode,
                    ConfirmedOnly: true,
                    fromUtc?.ToUniversalTime(),
                    toUtc?.ToUniversalTime(),
                    minimumConfidence)));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static CollectionPhotoResponse ToResponse(CatalogueCollectionPhoto photo) => new(
        photo.RevisionId.ToString(),
        photo.AssetId.ToString(),
        photo.ObservedAtUtc,
        photo.MediaType,
        photo.Width,
        photo.Height,
        photo.People.Select(person => new CollectionPersonMatchResponse(
            person.PersonId.ToString(),
            person.DisplayName,
            person.ConfirmedFaceCount)).ToArray());

    private static bool TryPeople(string? value, out PersonId[] personIds)
    {
        personIds = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        List<PersonId> parsed = [];
        foreach (string segment in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Guid.TryParse(segment, out Guid personId) || personId == Guid.Empty)
            {
                return false;
            }

            parsed.Add(PersonId.From(personId));
        }

        personIds = parsed.Distinct().ToArray();
        return personIds.Length > 0;
    }
}
