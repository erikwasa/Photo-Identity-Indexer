using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class CollectionEndpoints
{
    private const string ManifestFormat = "photoidentity.collection-manifest";
    private const int ManifestVersion = 2;
    private const int ManifestPageSize = 200;
    private const string ManifestMediaType = "application/vnd.photoidentity.collection-manifest+json";

    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos", GetPhotosAsync);
        endpoints.MapGet("/api/collections/manifest", GetManifestAsync);
        endpoints.MapGet("/api/collections/photos/{revisionId}/thumbnail", GetPhotoThumbnailAsync);
        endpoints.MapGet("/api/collections/photos/{revisionId}/preview", GetPhotoPreviewAsync);
        endpoints.MapGet("/api/collections/photos/{revisionId}/original", GetPhotoContentAsync);
        endpoints.MapGet("/api/collections/photos/{revisionId}/content", GetPhotoContentAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPhotosAsync(
        SqliteCollectionQueryRepository repository,
        string? people = null,
        string match = CatalogueCollectionMatchModes.All,
        string? reviewState = null,
        bool includeSuggestions = false,
        string? suggestionModelId = null,
        string? suggestionModelHash = null,
        double? minimumSuggestionScore = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        double? minimumConfidence = null,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default)
    {
        if (!TryPeople(people, out PersonId[] personIds))
        {
            return MissingPeople();
        }

        if (!TrySuggestionPolicy(
                includeSuggestions,
                suggestionModelId,
                suggestionModelHash,
                minimumSuggestionScore,
                out CatalogueCollectionSuggestionPolicy? suggestionPolicy,
                out string? suggestionError))
        {
            return Results.BadRequest(new { error = suggestionError });
        }

        try
        {
            CatalogueCollectionPhotoPage page = await repository.QueryPhotosAsync(
                personIds,
                match,
                suggestionPolicy,
                reviewState,
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
                ToQueryResponse(personIds, page, fromUtc, toUtc, minimumConfidence)));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> GetManifestAsync(
        HttpRequest request,
        SqliteCollectionQueryRepository repository,
        string? people = null,
        string match = CatalogueCollectionMatchModes.All,
        string? reviewState = null,
        bool includeSuggestions = false,
        string? suggestionModelId = null,
        string? suggestionModelHash = null,
        double? minimumSuggestionScore = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        double? minimumConfidence = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryPeople(people, out PersonId[] personIds))
        {
            return MissingPeople();
        }

        if (!TrySuggestionPolicy(
                includeSuggestions,
                suggestionModelId,
                suggestionModelHash,
                minimumSuggestionScore,
                out CatalogueCollectionSuggestionPolicy? suggestionPolicy,
                out string? suggestionError))
        {
            return Results.BadRequest(new { error = suggestionError });
        }

        try
        {
            CatalogueCollectionPhotoPage firstPage = await repository.QueryPhotosAsync(
                personIds,
                match,
                suggestionPolicy,
                reviewState,
                fromUtc,
                toUtc,
                minimumConfidence,
                offset: 0,
                limit: ManifestPageSize,
                cancellationToken);

            List<CatalogueCollectionPhoto> photos = new(firstPage.Total);
            photos.AddRange(firstPage.Items);
            int offset = firstPage.Items.Count;
            while (offset < firstPage.Total)
            {
                CatalogueCollectionPhotoPage page = await repository.QueryPhotosAsync(
                    personIds,
                    match,
                    suggestionPolicy,
                    reviewState,
                    fromUtc,
                    toUtc,
                    minimumConfidence,
                    offset,
                    ManifestPageSize,
                    cancellationToken);
                if (page.Items.Count == 0)
                {
                    break;
                }

                photos.AddRange(page.Items);
                offset += page.Items.Count;
            }

            CollectionManifestResponse manifest = new(
                ManifestFormat,
                ManifestVersion,
                photos.Count,
                ToQueryResponse(personIds, firstPage, fromUtc, toUtc, minimumConfidence),
                photos.Select(photo => ToManifestResponse(request, photo)).ToArray());
            return Results.Json(manifest, contentType: ManifestMediaType);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> GetPhotoThumbnailAsync(
        string revisionId,
        CollectionReviewProxyFileResolver proxyResolver,
        CollectionPhotoFileResolver originalResolver,
        OpenCvThumbnailRenderer renderer,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        CollectionPhotoFile? file = await proxyResolver.ResolveAsync(parsedRevisionId, cancellationToken)
            ?? await originalResolver.ResolveAsync(parsedRevisionId, cancellationToken);
        if (file is null)
        {
            return Results.NotFound();
        }

        EncodedThumbnail? thumbnail = await renderer.RenderAsync(file.Path, cancellationToken);
        return thumbnail is null
            ? Results.NotFound()
            : Results.File(thumbnail.Content, thumbnail.ContentType);
    }

    private static async Task<IResult> GetPhotoPreviewAsync(
        string revisionId,
        CollectionReviewProxyFileResolver proxyResolver,
        CollectionPhotoFileResolver originalResolver,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        CollectionPhotoFile? file = await proxyResolver.ResolveAsync(parsedRevisionId, cancellationToken)
            ?? await originalResolver.ResolveAsync(parsedRevisionId, cancellationToken);
        return file is null
            ? Results.NotFound()
            : Results.File(file.Path, file.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> GetPhotoContentAsync(
        string revisionId,
        CollectionPhotoFileResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        CollectionPhotoFile? file = await resolver.ResolveAsync(parsedRevisionId, cancellationToken);
        return file is null
            ? Results.NotFound()
            : Results.File(file.Path, file.ContentType, enableRangeProcessing: true);
    }

    private static CollectionPhotoResponse ToResponse(CatalogueCollectionPhoto photo) => new(
        photo.RevisionId.ToString(),
        photo.AssetId.ToString(),
        $"/api/collections/photos/{photo.RevisionId}/thumbnail",
        $"/api/collections/photos/{photo.RevisionId}/preview",
        $"/api/collections/photos/{photo.RevisionId}/original",
        photo.ObservedAtUtc,
        photo.MediaType,
        photo.Width,
        photo.Height,
        ToPeopleResponse(photo));

    private static CollectionManifestPhotoResponse ToManifestResponse(
        HttpRequest request,
        CatalogueCollectionPhoto photo) => new(
        photo.RevisionId.ToString(),
        photo.AssetId.ToString(),
        BuildPhotoUrl(request, photo.RevisionId, "thumbnail"),
        BuildPhotoUrl(request, photo.RevisionId, "preview"),
        BuildPhotoUrl(request, photo.RevisionId, "original"),
        photo.MediaType,
        photo.Width,
        photo.Height,
        ToPeopleResponse(photo));

    private static CollectionPersonMatchResponse[] ToPeopleResponse(CatalogueCollectionPhoto photo) =>
        photo.People.Select(person => new CollectionPersonMatchResponse(
            person.PersonId.ToString(),
            person.DisplayName,
            person.ConfirmedFaceCount,
            person.SuggestedFaceCount,
            person.MaximumSuggestionScore)).ToArray();

    private static CollectionQueryResponse ToQueryResponse(
        IReadOnlyList<PersonId> personIds,
        CatalogueCollectionPhotoPage page,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        double? minimumConfidence) => new(
        personIds.Select(value => value.ToString()).ToArray(),
        page.MatchMode,
        page.ReviewState,
        page.ReviewState == CatalogueCollectionReviewStates.Assigned,
        page.SuggestionPolicy is null
            ? null
            : new CollectionSuggestionPolicyResponse(
                page.SuggestionPolicy.ModelId.ToString(),
                page.SuggestionPolicy.ModelHash.ToString(),
                page.SuggestionPolicy.MinimumScore),
        fromUtc?.ToUniversalTime(),
        toUtc?.ToUniversalTime(),
        minimumConfidence);

    private static string BuildPhotoUrl(
        HttpRequest request,
        AssetRevisionId revisionId,
        string resource) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}/api/collections/photos/{revisionId}/{resource}";

    private static IResult MissingPeople() => Results.BadRequest(new
    {
        error = "Supply one or more comma-separated, non-empty person identifiers in the 'people' query parameter.",
    });

    private static bool TryRevisionId(string value, out AssetRevisionId revisionId)
    {
        revisionId = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        revisionId = AssetRevisionId.From(parsed);
        return true;
    }

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

    private static bool TrySuggestionPolicy(
        bool includeSuggestions,
        string? modelId,
        string? modelHash,
        double? minimumScore,
        out CatalogueCollectionSuggestionPolicy? policy,
        out string? error)
    {
        policy = null;
        error = null;

        bool suppliedAny =
            !string.IsNullOrWhiteSpace(modelId) ||
            !string.IsNullOrWhiteSpace(modelHash) ||
            minimumScore is not null;
        if (!includeSuggestions)
        {
            if (suppliedAny)
            {
                error = "Set 'includeSuggestions=true' before supplying suggestion model or threshold parameters.";
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(modelId) ||
            string.IsNullOrWhiteSpace(modelHash) ||
            minimumScore is null)
        {
            error = "Suggestion-backed queries require 'suggestionModelId', 'suggestionModelHash' and 'minimumSuggestionScore'.";
            return false;
        }

        try
        {
            policy = new CatalogueCollectionSuggestionPolicy(
                new ModelId(modelId),
                new Sha256Digest(modelHash),
                minimumScore.Value);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
