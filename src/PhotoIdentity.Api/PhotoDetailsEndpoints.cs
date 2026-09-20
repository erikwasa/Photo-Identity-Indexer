using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Sources;
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
        endpoints.MapPut("/api/collections/photos/{revisionId}/capture-date", SetCaptureDateAsync);
        endpoints.MapDelete("/api/collections/photos/{revisionId}/capture-date", ClearCaptureDateAsync);
        endpoints.MapPhotoPresentationPreferenceEndpoints();
        return endpoints;
    }

    private static async Task<IResult> GetPhotoDetailsAsync(
        string revisionId,
        IPhotoDetailsRepository repository,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new PhotoPersonErrorResponse("The asset revision identifier is invalid."));
        }

        PhotoDetails? details = await repository.GetAsync(parsedRevisionId, cancellationToken);
        if (details is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(await ToResponseAsync(details, services, cancellationToken));
    }

    private static async Task<IResult> AddManualPersonAsync(
        string revisionId,
        PhotoPersonMutationRequest request,
        IPhotoPersonRepository repository,
        IPhotoDetailsRepository detailsRepository,
        IServiceProvider services,
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

        try
        {
            await repository.AddManualPersonAsync(
                parsedRevisionId,
                personId,
                LocalMaintainerActor,
                cancellationToken);
            PhotoDetails details = await detailsRepository.GetAsync(parsedRevisionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Asset revision '{parsedRevisionId}' was not found.");
            return Results.Ok(await ToResponseAsync(details, services, cancellationToken));
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
        IPhotoPersonRepository repository,
        IPhotoDetailsRepository detailsRepository,
        IServiceProvider services,
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

        try
        {
            await repository.RemoveManualPersonAsync(
                parsedRevisionId,
                parsedPersonId,
                LocalMaintainerActor,
                cancellationToken);
            PhotoDetails details = await detailsRepository.GetAsync(parsedRevisionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Asset revision '{parsedRevisionId}' was not found.");
            return Results.Ok(await ToResponseAsync(details, services, cancellationToken));
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

    private static async Task<IResult> SetCaptureDateAsync(
        string revisionId,
        PhotoCaptureDateMutationRequest request,
        IPhotoDetailsRepository detailsRepository,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new PhotoCaptureDateErrorResponse("The asset revision identifier is invalid."));
        }

        IPhotoCaptureDateRepository? repository = services.GetService<IPhotoCaptureDateRepository>();
        if (repository is null)
        {
            return Results.Conflict(new PhotoCaptureDateErrorResponse(
                "Manual capture-date editing is available only when PostgreSQL is the selected catalogue provider."));
        }

        PhotoCaptureDateValue value;
        try
        {
            value = PhotoCaptureDateValue.Parse(request.Value);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new PhotoCaptureDateErrorResponse(exception.Message));
        }

        try
        {
            PhotoCaptureDateState state = await repository.SetManualAsync(
                parsedRevisionId,
                value,
                LocalMaintainerActor,
                cancellationToken);
            PhotoDetails details = await detailsRepository.GetAsync(parsedRevisionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Asset revision '{parsedRevisionId}' was not found.");
            return Results.Ok(ToResponse(details, state, canEditCaptureDate: true));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new PhotoCaptureDateErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> ClearCaptureDateAsync(
        string revisionId,
        IPhotoDetailsRepository detailsRepository,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!TryParseRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new PhotoCaptureDateErrorResponse("The asset revision identifier is invalid."));
        }

        IPhotoCaptureDateRepository? repository = services.GetService<IPhotoCaptureDateRepository>();
        if (repository is null)
        {
            return Results.Conflict(new PhotoCaptureDateErrorResponse(
                "Manual capture-date editing is available only when PostgreSQL is the selected catalogue provider."));
        }

        try
        {
            PhotoCaptureDateState state = await repository.ClearManualAsync(
                parsedRevisionId,
                LocalMaintainerActor,
                cancellationToken);
            PhotoDetails details = await detailsRepository.GetAsync(parsedRevisionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Asset revision '{parsedRevisionId}' was not found.");
            return Results.Ok(ToResponse(details, state, canEditCaptureDate: true));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new PhotoCaptureDateErrorResponse(exception.Message));
        }
    }

    private static async Task<PhotoDetailsResponse> ToResponseAsync(
        PhotoDetails details,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        IPhotoCaptureDateRepository? captureDates = services.GetService<IPhotoCaptureDateRepository>();
        if (captureDates is not null)
        {
            PhotoCaptureDateState state = await captureDates.GetStateAsync(
                details.RevisionId,
                cancellationToken);
            return ToResponse(details, state, canEditCaptureDate: true);
        }

        DateTime? extracted = details.CaptureMetadata?.TakenAtLocal;
        PhotoCaptureDateRange? range = extracted is null
            ? null
            : new PhotoCaptureDateRange(
                DateOnly.FromDateTime(extracted.Value),
                DateOnly.FromDateTime(extracted.Value));
        PhotoCaptureDateState fallback = new(
            details.RevisionId,
            extracted,
            null,
            range,
            range is null ? null : PhotoCaptureDateSources.Extracted,
            []);
        return ToResponse(details, fallback, canEditCaptureDate: false);
    }

    private static PhotoDetailsResponse ToResponse(
        PhotoDetails details,
        PhotoCaptureDateState captureDate,
        bool canEditCaptureDate)
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
            ToMetadataResponse(details.CaptureMetadata, details.ExtendedMetadata),
            ToCaptureDateResponse(captureDate, canEditCaptureDate));
    }

    private static PhotoCaptureDateResponse ToCaptureDateResponse(
        PhotoCaptureDateState state,
        bool canEditCaptureDate)
    {
        string? precision = state.ManualDate?.Precision;
        if (precision is null && state.EffectiveSource == PhotoCaptureDateSources.Extracted)
        {
            precision = "timestamp";
        }

        return new PhotoCaptureDateResponse(
            state.EffectiveRange?.From.ToString("yyyy-MM-dd"),
            state.EffectiveRange?.To.ToString("yyyy-MM-dd"),
            state.EffectiveSource,
            precision,
            state.ManualDate?.ToString(),
            state.ExtractedTakenAtLocal,
            canEditCaptureDate);
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
