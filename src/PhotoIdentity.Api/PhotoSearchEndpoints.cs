using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record PhotoSearchRequest(
    string Query,
    string? Mode = null,
    int Limit = 80);

public sealed record PhotoSearchResultResponse(
    string RevisionId,
    string ThumbnailUrl,
    string PreviewUrl,
    string PhotoUrl,
    double CombinedScore,
    double? SemanticScore,
    double? CaptionScore,
    string? CaptionLanguage,
    string? Caption,
    string[] Sources);

public sealed record PhotoSearchResponse(
    string Query,
    string Mode,
    bool SemanticModelAvailable,
    int IndexedPhotoCount,
    int DisplayableCaptionCount,
    double SearchMilliseconds,
    PhotoSearchResultResponse[] Items);

public sealed record PhotoSearchSaveCollectionRequest(
    string Name,
    string[] RevisionIds);

public static class PhotoSearchEndpoints
{
    public static IEndpointRouteBuilder MapPhotoSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/photo-search/query", QueryAsync);
        endpoints.MapGet("/api/photo-search/status", StatusAsync);
        endpoints.MapPost("/api/photo-search/collections", SaveCollectionAsync);
        return endpoints;
    }

    private static async Task<IResult> QueryAsync(
        PhotoSearchRequest request,
        PhotoSearchService search,
        CancellationToken cancellationToken)
    {
        try
        {
            PhotoSearchExecutionResult result = await search.SearchAsync(
                request.Query,
                request.Mode,
                request.Limit,
                cancellationToken);
            return Results.Ok(new PhotoSearchResponse(
                result.Query,
                result.Mode,
                result.SemanticModelAvailable,
                result.IndexedPhotoCount,
                result.DisplayableCaptionCount,
                result.SearchMilliseconds,
                result.Items.Select(item => new PhotoSearchResultResponse(
                    item.RevisionId.ToString(),
                    $"/api/collections/photos/{item.RevisionId}/thumbnail",
                    $"/api/collections/photos/{item.RevisionId}/preview",
                    $"/photo/{item.RevisionId}",
                    item.CombinedScore,
                    item.SemanticScore,
                    item.CaptionScore,
                    item.CaptionLanguage,
                    item.Caption,
                    item.Sources.ToArray())).ToArray()));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> StatusAsync(
        PhotoSearchService search,
        CancellationToken cancellationToken)
    {
        PhotoSearchStatus status = await search.GetStatusAsync(cancellationToken);
        return Results.Ok(status);
    }

    private static async Task<IResult> SaveCollectionAsync(
        PhotoSearchSaveCollectionRequest request,
        IPhotoListCollectionRepository collections,
        CancellationToken cancellationToken)
    {
        try
        {
            AssetRevisionId[] revisionIds = ParseRevisionIds(request.RevisionIds);
            PhotoListCollectionDefinition definition = await collections.CreateAsync(
                request.Name,
                revisionIds,
                cancellationToken);
            return Results.Created(
                $"/api/photo-list-collections/{definition.Id}",
                new PhotoListCollectionResponse(
                    definition.Id.ToString(),
                    definition.Name,
                    definition.RevisionIds.Select(item => item.ToString()).ToArray(),
                    definition.CreatedAtUtc,
                    definition.UpdatedAtUtc));
        }
        catch (PhotoListCollectionNameConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (PhotoListCollectionRevisionUnavailableException exception)
        {
            return Results.BadRequest(new
            {
                error = exception.Message,
                revisionIds = exception.RevisionIds.Select(item => item.ToString()).ToArray(),
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static AssetRevisionId[] ParseRevisionIds(string[]? values)
    {
        List<AssetRevisionId> revisionIds = [];
        foreach (string value in values ?? [])
        {
            if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException(
                    $"Revision identifier '{value}' is not a valid non-empty GUID.",
                    nameof(values));
            }
            revisionIds.Add(AssetRevisionId.From(parsed));
        }

        if (revisionIds.Count == 0)
        {
            throw new ArgumentException(
                "Select at least one search result before saving a slideshow collection.",
                nameof(values));
        }

        return PhotoListCollectionDefinition.ValidateRevisionIds(revisionIds);
    }
}
