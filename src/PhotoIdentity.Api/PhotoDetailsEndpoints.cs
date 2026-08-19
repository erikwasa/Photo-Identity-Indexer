using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class PhotoDetailsEndpoints
{
    private const string LocalMaintainerActor = "local-maintainer";

    public static IEndpointRouteBuilder MapPhotoDetailsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos/{revisionId}/details", GetPhotoDetailsAsync);
        endpoints.MapPost("/api/collections/photos/{revisionId}/people", AddManualPersonAsync);
        endpoints.MapDelete("/api/collections/photos/{revisionId}/people/{personId}", RemoveManualPersonAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPhotoDetailsAsync(
        string revisionId,
        SqlitePhotoDetailsRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new PhotoPersonErrorResponse("The asset revision identifier is invalid."));
        }

        CataloguePhotoDetails? details = await repository.GetAsync(parsedRevisionId, cancellationToken);
        if (details is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToResponse(details));
    }

    private static async Task<IResult> AddManualPersonAsync(
        string revisionId,
        PhotoPersonMutationRequest request,
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider,
        SqlitePhotoDetailsRepository detailsRepository,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new PhotoPersonErrorResponse("The asset revision identifier is invalid."));
        }

        if (!TryParsePersonId(request.PersonId, out PersonId personId))
        {
            return Results.BadRequest(new PhotoPersonErrorResponse("The person identifier is invalid."));
        }

        SqlitePhotoPersonRepository repository = new(database, timeProvider);
        try
        {
            await repository.AddManualPersonAsync(
                parsedRevisionId,
                personId,
                LocalMaintainerActor,
                cancellationToken);
            CataloguePhotoDetails details = await detailsRepository.GetAsync(parsedRevisionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Asset revision '{parsedRevisionId}' was not found.");
            return Results.Ok(ToResponse(details));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new PhotoPersonErrorResponse(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new PhotoPersonErrorResponse(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new PhotoPersonErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> RemoveManualPersonAsync(
        string revisionId,
        string personId,
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider,
        SqlitePhotoDetailsRepository detailsRepository,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new PhotoPersonErrorResponse("The asset revision identifier is invalid."));
        }

        if (!TryParsePersonId(personId, out PersonId parsedPersonId))
        {
            return Results.BadRequest(new PhotoPersonErrorResponse("The person identifier is invalid."));
        }

        SqlitePhotoPersonRepository repository = new(database, timeProvider);
        try
        {
            await repository.RemoveManualPersonAsync(
                parsedRevisionId,
                parsedPersonId,
                LocalMaintainerActor,
                cancellationToken);
            CataloguePhotoDetails details = await detailsRepository.GetAsync(parsedRevisionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Asset revision '{parsedRevisionId}' was not found.");
            return Results.Ok(ToResponse(details));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new PhotoPersonErrorResponse(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new PhotoPersonErrorResponse(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new PhotoPersonErrorResponse(exception.Message));
        }
    }

    private static PhotoDetailsResponse ToResponse(CataloguePhotoDetails details)
    {
        string fileName = FileNameOnly(details.SourceKey);
        return new PhotoDetailsResponse(
            details.RevisionId.ToString(),
            fileName,
            details.People.Select(person => new PhotoDetailsPersonResponse(
                person.PersonId.ToString(),
                person.DisplayName,
                person.ConfirmedFaceCount,
                person.ManualPresence)).ToArray(),
            ToMetadataResponse(details.CaptureMetadata, details.ExtendedMetadata));
    }

    private static PhotoMetadataResponse? ToMetadataResponse(
        PhotoCaptureMetadata? capture,
        CatalogueExtendedPhotoMetadata? extended)
    {
        if (capture is null)
        {
            return null;
        }

        return new PhotoMetadataResponse(
            capture.TakenAtLocal,
            capture.UtcOffset is null ? null : checked((int)capture.UtcOffset.Value.TotalMinutes),
            capture.Latitude,
            capture.Longitude,
            extended?.CameraMake,
            extended?.CameraModel,
            extended?.LensModel,
            extended?.Orientation,
            extended?.ExposureTime,
            extended?.Aperture,
            extended?.Iso,
            extended?.FocalLength,
            extended?.FocalLength35Mm,
            extended?.Flash,
            extended?.GpsAltitude,
            extended?.RawTags.Select(tag => new PhotoMetadataTagResponse(
                tag.Directory,
                tag.Name,
                tag.Value)).ToArray() ?? []);
    }

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

    private static bool TryParsePersonId(string value, out PersonId personId)
    {
        if (Guid.TryParse(value, out Guid guid) && guid != Guid.Empty)
        {
            personId = PersonId.From(guid);
            return true;
        }

        personId = default;
        return false;
    }

    private static string FileNameOnly(string sourceKey)
    {
        string normalized = sourceKey.Replace('\\', '/').TrimEnd('/');
        int separator = normalized.LastIndexOf('/');
        string fileName = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return string.IsNullOrWhiteSpace(fileName) ? "Unknown" : fileName;
    }
}
