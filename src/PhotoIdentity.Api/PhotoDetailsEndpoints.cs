using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class PhotoDetailsEndpoints
{
    public static IEndpointRouteBuilder MapPhotoDetailsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos/{revisionId}/details", GetPhotoDetailsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPhotoDetailsAsync(
        string revisionId,
        SqlitePhotoDetailsRepository repository,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid value) || value == Guid.Empty)
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        AssetRevisionId parsedRevisionId = AssetRevisionId.From(value);
        CataloguePhotoDetails? details = await repository.GetAsync(parsedRevisionId, cancellationToken);
        if (details is null)
        {
            return Results.NotFound();
        }

        string fileName = FileNameOnly(details.SourceKey);
        return Results.Ok(new PhotoDetailsResponse(
            details.RevisionId.ToString(),
            fileName,
            details.People.Select(person => new PhotoDetailsPersonResponse(
                person.PersonId.ToString(),
                person.DisplayName,
                person.ConfirmedFaceCount,
                person.ManualPresence)).ToArray()));
    }

    private static string FileNameOnly(string sourceKey)
    {
        string normalized = sourceKey.Replace('\\', '/').TrimEnd('/');
        int separator = normalized.LastIndexOf('/');
        string fileName = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return string.IsNullOrWhiteSpace(fileName) ? "Unknown" : fileName;
    }
}
