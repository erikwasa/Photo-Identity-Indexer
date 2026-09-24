using System.Data.Common;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record SlideshowExposureRequest(
    Guid SessionId,
    Guid CollectionId,
    bool Creative,
    string RevisionId);

public sealed record SlideshowExposureResponse(
    string RevisionId,
    bool Recorded,
    int ShowCount,
    DateTimeOffset? LastShownAtUtc);

public static class SlideshowExposureEndpoints
{
    public static IEndpointRouteBuilder MapSlideshowExposureEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/slideshows/exposures", RecordAsync);
        return endpoints;
    }

    private static async Task<IResult> RecordAsync(
        SlideshowExposureRequest request,
        IPhotoSlideshowExposureRepository repository,
        ICreativeCollectionRecipeRepository creativeCollections,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty ||
            request.CollectionId == Guid.Empty ||
            !Guid.TryParse(request.RevisionId, out Guid revisionGuid) ||
            revisionGuid == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Slideshow exposure identifiers are invalid." });
        }

        try
        {
            SmartCollectionId collectionId = SmartCollectionId.From(request.CollectionId);
            if (request.Creative)
            {
                CreativeCollectionRecipe? named = await creativeCollections.GetAsync(
                    CreativeCollectionId.From(request.CollectionId),
                    cancellationToken);
                if (named is not null)
                {
                    collectionId = named.AnchorCollectionId;
                }
            }

            AssetRevisionId revisionId = AssetRevisionId.From(revisionGuid);
            bool recorded = await repository.RecordPresentedAsync(
                request.SessionId,
                collectionId,
                request.Creative,
                revisionId,
                cancellationToken);
            IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary> summaries =
                await repository.GetSummariesAsync([revisionId], cancellationToken);
            PhotoSlideshowExposureSummary summary = summaries.GetValueOrDefault(revisionId)
                ?? new PhotoSlideshowExposureSummary(revisionId, 0, null);
            return Results.Ok(new SlideshowExposureResponse(
                request.RevisionId,
                recorded,
                summary.ShowCount,
                summary.LastShownAtUtc));
        }
        catch (DbException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
