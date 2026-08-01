using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/collections/photos", GetPhotosAsync);
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
            return Results.BadRequest(new
            {
                error = "Supply one or more comma-separated, non-empty person identifiers in the 'people' query parameter.",
            });
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
                new CollectionQueryResponse(
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
                    minimumConfidence)));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> GetPhotoContentAsync(
        string revisionId,
        CollectionPhotoFileResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid parsedRevisionId) || parsedRevisionId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        CollectionPhotoFile? file = await resolver.ResolveAsync(
            AssetRevisionId.From(parsedRevisionId),
            cancellationToken);
        return file is null
            ? Results.NotFound()
            : Results.File(file.Path, file.ContentType, enableRangeProcessing: true);
    }

    private static CollectionPhotoResponse ToResponse(CatalogueCollectionPhoto photo) => new(
        photo.RevisionId.ToString(),
        photo.AssetId.ToString(),
        $"/api/collections/photos/{photo.RevisionId}/content",
        photo.ObservedAtUtc,
        photo.MediaType,
        photo.Width,
        photo.Height,
        photo.People.Select(person => new CollectionPersonMatchResponse(
            person.PersonId.ToString(),
            person.DisplayName,
            person.ConfirmedFaceCount,
            person.SuggestedFaceCount,
            person.MaximumSuggestionScore)).ToArray());

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
