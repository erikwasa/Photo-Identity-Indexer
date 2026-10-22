using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Api;

/// <summary>
/// Collection-specific read-only similar-alternative review from the
/// already accepted Creative materialization group evidence.
/// Operator preferences persist, but have NO effect on slideshow selection
/// or the original Photo Identity exclusion/purge boundary.
/// </summary>
public static class CreativeSimilarityReviewEndpoints
{
    public static IEndpointRouteBuilder MapCreativeSimilarityReviewEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/creative-collections/{id:guid}/similarity-review", GetAsync);
        endpoints.MapPut("/api/creative-collections/{id:guid}/similarity-review", PutAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid id, ICreativeCollectionRecipeRepository recipes,
        CreativeCollectionMaterializationService materializer,
        CreativeSimilarityReviewStore store,
        CancellationToken cancellationToken,
        int offset = 0,
        int pageSize = CreativeSimilarityReviewPaging.DefaultPageSize)
    {
        if (!CreativeSimilarityReviewPaging.IsValid(offset, pageSize))
            return Results.BadRequest(new { error = "Review offset must be non-negative and pageSize must be between 1 and 80." });
        if (id == Guid.Empty)
            return Results.BadRequest(new { error = "Collection ID cannot be empty." });
        CreativeCollectionRecipe? recipe = await recipes.GetAsync(
            CreativeCollectionId.From(id), cancellationToken);
        if (recipe is null) return Results.NotFound();

        return await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(
            async token =>
            {
                CreativeCollectionMaterialization? materialized =
                    await materializer.MaterializeAsync(recipe, token);
                if (materialized is null) return Results.NotFound();
                PhotoVisualRedundancyGroup[] allGroups =
                    materialized.VisualRedundancy.Groups.ToArray();
                // Page the complete set rather than dropping groups after the first 80.
                // No grouping, selector or review-choice semantics change.
                PhotoVisualRedundancyGroup[] sample = CreativeSimilarityReviewPaging.Page(
                    allGroups, offset, pageSize);
                var selected = materialized.Selection.Selected
                    .Select(item => item.Candidate.RevisionId.ToString().ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                List<CreativeSimilarityGroupReviewResponse> groups = [];
                foreach (PhotoVisualRedundancyGroup group in sample)
                {
                    string key = CreativeSimilarityReviewStore.GroupKey(group);
                    CreativeSimilarityHumanChoice? choice = await store.GetAsync(
                        recipe.Id.ToString(), key, token);
                    groups.Add(new(
                        key, group.MaximumPairDistance, group.StartedAtLocal,
                        group.EndedAtLocal, group.Members.Count,
                        group.Members.Select(member => new CreativeSimilarityPhotoResponse(
                            member.RevisionId.ToString().ToLowerInvariant(),
                            $"/api/collections/photos/{member.RevisionId}/thumbnail",
                            member.DistanceFromRepresentative,
                            selected.Contains(member.RevisionId.ToString()))).ToArray(),
                        choice?.Decision, choice?.PreferredRevisionId));
                }
                return Results.Ok(new CreativeSimilarityReviewResponse(
                    recipe.Id.ToString(), recipe.Name,
                    materialized.VisualRedundancy.PolicyVersion,
                    materialized.VisualRedundancy.AlgorithmVersion,
                    materialized.VisualRedundancy.MaximumHammingDistance,
                    materialized.VisualRedundancy.MaximumCaptureSpan.TotalSeconds,
                    allGroups.Length, sample.Length, offset + sample.Length < allGroups.Length,
                    offset,
                    materialized.Generated.TotalCandidateCount,
                    materialized.Selection.SelectedCount,
                    groups.ToArray(),
                    false, false,
                    "Suggestions use already-local dHash evidence within bounded moments. " +
                    "Keep all is the default. Recorded preferences do not currently affect " +
                    "collection membership, slideshow playback or Photo Identity source copies."));
            }, cancellationToken);
    }

    private static async Task<IResult> PutAsync(
        Guid id, CreativeSimilarityReviewRequest request,
        ICreativeCollectionRecipeRepository recipes,
        CreativeCollectionMaterializationService materializer,
        CreativeSimilarityReviewStore store,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(request.GroupKey) ||
            request.Decision is not (CreativeSimilarityReviewStore.KeepAll or
                                     CreativeSimilarityReviewStore.PreferOne))
            return Results.BadRequest(new { error = "Invalid similarity review decision." });
        CreativeCollectionRecipe? recipe = await recipes.GetAsync(
            CreativeCollectionId.From(id), cancellationToken);
        if (recipe is null) return Results.NotFound();

        // Re-materialize the CURRENT collection selection and check the
        // exact revision membership. Old/stale group choices never transfer
        // to different photos even if generated group ordinal is recycled.
        return await CreativeCollectionPreviewEndpoints.WithMaterializationDeadlineAsync(
            async token =>
            {
                CreativeCollectionMaterialization? current =
                    await materializer.MaterializeAsync(recipe, token);
                if (current is null) return Results.NotFound();
                PhotoVisualRedundancyGroup? group = current.VisualRedundancy.Groups
                    .FirstOrDefault(candidate =>
                        CreativeSimilarityReviewStore.GroupKey(candidate) == request.GroupKey);
                if (group is null)
                    return Results.Conflict(new { error = "Similar photo group changed. Refresh preview." });

                string? preferred = request.PreferredRevisionId?.ToLowerInvariant();
                if (request.Decision == CreativeSimilarityReviewStore.PreferOne &&
                    !group.Members.Any(member =>
                        string.Equals(member.RevisionId.ToString(), preferred,
                            StringComparison.OrdinalIgnoreCase)))
                    return Results.BadRequest(new { error = "Preferred photo must be in this exact current group." });
                if (request.Decision == CreativeSimilarityReviewStore.KeepAll &&
                    preferred is not null)
                    return Results.BadRequest(new { error = "Keep all must not identify a preferred photo." });

                await store.SaveAsync(recipe.Id.ToString(), request.GroupKey,
                    request.Decision, preferred, token);
                return Results.Ok(new
                {
                    recorded = true,
                    collectionSelectionChanged = false,
                    slideshowChanged = false,
                    originalsChanged = false,
                    archiveExclusionsPerformed = 0
                });
            }, cancellationToken);
    }
}

public sealed record CreativeSimilarityReviewRequest(
    string GroupKey, string Decision, string? PreferredRevisionId);
public sealed record CreativeSimilarityPhotoResponse(
    string RevisionId, string ThumbnailUrl, int DistanceFromRepresentative, bool SelectedByCurrentRecipe);
public sealed record CreativeSimilarityGroupReviewResponse(
    string GroupKey, int MaximumPairDistance, DateTime StartedAtLocal, DateTime EndedAtLocal,
    int MemberCount, CreativeSimilarityPhotoResponse[] Members,
    string? Decision, string? PreferredRevisionId);
public sealed record CreativeSimilarityReviewResponse(
    string CollectionId, string CollectionName, string PolicyVersion,
    string AlgorithmVersion, int MaximumHammingDistance, double MaximumCaptureSpanSeconds,
    int TotalGroups, int DisplayedGroups, bool GroupSampleTruncated,
    int GroupOffset, int TotalCandidates, int SelectedCount, CreativeSimilarityGroupReviewResponse[] Groups,
    bool CollectionSelectionChanged, bool AutoOmissionEnabled, string EvidenceSemantics);

/// <summary>
/// Bounded, deterministic review paging without altering the saved group's
/// fingerprint or the complete-link similarity policy.
/// </summary>
public static class CreativeSimilarityReviewPaging
{
    public const int DefaultPageSize = 12;
    public const int MaximumPageSize = 80;

    public static bool IsValid(int offset, int pageSize) =>
        offset >= 0 && pageSize is >= 1 and <= MaximumPageSize;

    public static T[] Page<T>(IReadOnlyList<T> groups, int offset, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (!IsValid(offset, pageSize))
            throw new ArgumentOutOfRangeException(nameof(pageSize),
                "Review offset must be non-negative and page size must be between 1 and 80.");
        return groups.Skip(offset).Take(pageSize).ToArray();
    }
}
