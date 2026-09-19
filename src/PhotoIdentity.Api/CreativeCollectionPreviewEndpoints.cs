using System.Globalization;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record CreativeCollectionContextReasonResponse(
    string MomentId,
    string[] AnchorRevisionIds);

public sealed record CreativeCollectionPreviewCandidateResponse(
    string RevisionId,
    string Kind,
    DateTime? TakenAtLocal,
    string ThumbnailUrl,
    int ShowCount,
    DateTimeOffset? LastShownAtUtc,
    CreativeCollectionContextReasonResponse[] ContextReasons);

public sealed record CreativeCollectionSelectionReasonResponse(
    string Code,
    int ScoreDelta,
    string Detail);

public sealed record CreativeCollectionSelectedCandidateResponse(
    string RevisionId,
    string Kind,
    DateTime? TakenAtLocal,
    string ThumbnailUrl,
    string? MomentId,
    string? PeopleCombinationKey,
    int SelectionScore,
    int ShowCount,
    DateTimeOffset? LastShownAtUtc,
    CreativeCollectionSelectionReasonResponse[] SelectionReasons,
    CreativeCollectionContextReasonResponse[] ContextReasons);

public sealed record CreativeCollectionPreviewResponse(
    string CollectionId,
    string CollectionName,
    string MomentPolicyVersion,
    string ContextPolicyVersion,
    int DirectAnchorCount,
    int AddedContextCount,
    int TotalCandidateCount,
    bool NoAnchors,
    string SelectionPolicyVersion,
    bool NoveltyEnabled,
    string NoveltyPolicyVersion,
    int RequestedTargetCount,
    int SelectedDirectAnchorCount,
    int SelectedContextCount,
    int SelectedCount,
    int RepresentedMomentCount,
    int RepresentedTimePeriodCount,
    CreativeCollectionPreviewCandidateResponse[] Candidates,
    CreativeCollectionSelectedCandidateResponse[] SelectedCandidates);

public static class CreativeCollectionPreviewEndpoints
{
    public static IEndpointRouteBuilder MapCreativeCollectionPreviewEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/smart-collections/{id:guid}/creative-preview",
            PreviewAsync);
        endpoints.MapPost(
            "/api/smart-collections/{id:guid}/creative-slideshow-snapshot",
            CreateCreativeSlideshowSnapshotAsync);
        endpoints.MapCreativeCollectionRecipeEndpoints();
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        Guid id,
        CreativeCollectionMaterializationService materializer,
        CancellationToken cancellationToken,
        int targetCount = 100,
        int momentGapMinutes = 30,
        string contextStrength = "balanced",
        bool novelty = false)
    {
        if (!TryCreateSettings(
                id,
                targetCount,
                momentGapMinutes,
                contextStrength,
                novelty,
                out SmartCollectionId collectionId,
                out CreativeCollectionRecipeSettings? settings,
                out IResult? error))
        {
            return error!;
        }

        CreativeCollectionMaterialization? materialized = await materializer.MaterializeAsync(
            collectionId,
            settings!,
            cancellationToken);
        return materialized is null
            ? Results.NotFound()
            : Results.Ok(ToPreviewResponse(materialized));
    }

    private static async Task<IResult> CreateCreativeSlideshowSnapshotAsync(
        Guid id,
        CreativeCollectionMaterializationService materializer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        int targetCount = 100,
        int momentGapMinutes = 30,
        string contextStrength = "balanced",
        bool novelty = false)
    {
        if (!TryCreateSettings(
                id,
                targetCount,
                momentGapMinutes,
                contextStrength,
                novelty,
                out SmartCollectionId collectionId,
                out CreativeCollectionRecipeSettings? settings,
                out IResult? error))
        {
            return error!;
        }

        CreativeCollectionMaterialization? materialized = await materializer.MaterializeAsync(
            collectionId,
            settings!,
            cancellationToken);
        return materialized is null
            ? Results.NotFound()
            : Results.Ok(ToSnapshotResponse(materialized, timeProvider.GetUtcNow().ToUniversalTime()));
    }

    internal static CreativeCollectionPreviewResponse ToPreviewResponse(
        CreativeCollectionMaterialization materialized)
    {
        CreativeCollectionPreviewCandidateResponse[] candidates = materialized.Generated.Candidates
            .Select(candidate => ToPreviewCandidate(
                candidate,
                materialized.ExposureHistory.GetValueOrDefault(candidate.RevisionId)))
            .ToArray();
        CreativeCollectionSelectedCandidateResponse[] selected = materialized.Selection.Selected
            .Select(candidate => ToSelectedCandidate(
                candidate,
                materialized.ExposureHistory.GetValueOrDefault(candidate.Candidate.RevisionId)))
            .ToArray();

        int representedMoments = selected
            .Select(candidate => candidate.MomentId)
            .Where(momentId => !string.IsNullOrWhiteSpace(momentId))
            .Distinct(StringComparer.Ordinal)
            .Count();
        int representedPeriods = selected
            .Where(candidate => candidate.TakenAtLocal.HasValue)
            .Select(candidate => candidate.TakenAtLocal!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new CreativeCollectionPreviewResponse(
            materialized.Definition.Id.ToString(),
            materialized.Definition.Name,
            materialized.Generated.MomentPolicyVersion,
            materialized.Generated.ContextPolicyVersion,
            materialized.Generated.DirectAnchorCount,
            materialized.Generated.AddedContextCount,
            materialized.Generated.TotalCandidateCount,
            materialized.Generated.NoAnchors,
            materialized.Selection.PolicyVersion,
            materialized.NoveltyPolicyVersion != CreativeCollectionNoveltyPolicies.Disabled,
            materialized.NoveltyPolicyVersion,
            materialized.Selection.RequestedTargetCount,
            materialized.Selection.SelectedDirectAnchorCount,
            materialized.Selection.SelectedContextCount,
            materialized.Selection.SelectedCount,
            representedMoments,
            representedPeriods,
            candidates,
            selected);
    }

    internal static SmartCollectionSlideshowSnapshotResponse ToSnapshotResponse(
        CreativeCollectionMaterialization materialized,
        DateTimeOffset createdAtUtc)
    {
        Dictionary<AssetRevisionId, string> visualGroupByRevision = materialized.VisualRedundancy.Groups
            .SelectMany(group => group.Members.Select(member => (member.RevisionId, group.Id)))
            .ToDictionary(pair => pair.RevisionId, pair => pair.Id);

        SmartCollectionSlideshowSnapshotItemResponse[] items = materialized.Selection.Selected
            .Select(item => new SmartCollectionSlideshowSnapshotItemResponse(
                item.Candidate.RevisionId.ToString(),
                item.MomentId,
                visualGroupByRevision.GetValueOrDefault(item.Candidate.RevisionId)))
            .ToArray();

        return new SmartCollectionSlideshowSnapshotResponse(
            materialized.Definition.Id.ToString(),
            materialized.Definition.Name,
            createdAtUtc,
            items,
            items.Length,
            materialized.Generated.MomentPolicyVersion,
            materialized.VisualRedundancy.PolicyVersion);
    }

    private static bool TryCreateSettings(
        Guid id,
        int targetCount,
        int momentGapMinutes,
        string contextStrength,
        bool novelty,
        out SmartCollectionId collectionId,
        out CreativeCollectionRecipeSettings? settings,
        out IResult? error)
    {
        try
        {
            collectionId = SmartCollectionId.From(id);
            CreativeCollectionContextPolicy contextPolicy =
                CreativeCollectionContextPolicy.FromStrength(contextStrength);
            settings = CreativeCollectionRecipeSettings.CreateForPreview(
                targetCount,
                momentGapMinutes,
                contextPolicy.Version,
                novelty);
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException)
        {
            collectionId = default;
            settings = null;
            error = Results.BadRequest(new { error = exception.Message });
            return false;
        }
    }

    private static CreativeCollectionPreviewCandidateResponse ToPreviewCandidate(
        CreativeCollectionCandidate candidate,
        PhotoSlideshowExposureSummary? exposure) => new(
        candidate.RevisionId.ToString(),
        candidate.Kind,
        candidate.TakenAtLocal,
        $"/api/collections/photos/{candidate.RevisionId}/thumbnail",
        exposure?.ShowCount ?? 0,
        exposure?.LastShownAtUtc,
        ToContextReasons(candidate.ContextReasons));

    private static CreativeCollectionSelectedCandidateResponse ToSelectedCandidate(
        CreativeCollectionSelectedCandidate selected,
        PhotoSlideshowExposureSummary? exposure) => new(
        selected.Candidate.RevisionId.ToString(),
        selected.Candidate.Kind,
        selected.Candidate.TakenAtLocal,
        $"/api/collections/photos/{selected.Candidate.RevisionId}/thumbnail",
        selected.MomentId,
        selected.PeopleCombinationKey,
        selected.SelectionScore,
        exposure?.ShowCount ?? 0,
        exposure?.LastShownAtUtc,
        selected.Reasons
            .Select(reason => new CreativeCollectionSelectionReasonResponse(
                reason.Code,
                reason.ScoreDelta,
                reason.Detail))
            .ToArray(),
        ToContextReasons(selected.Candidate.ContextReasons));

    private static CreativeCollectionContextReasonResponse[] ToContextReasons(
        IReadOnlyList<CreativeCollectionContextReason> reasons) => reasons
        .Select(reason => new CreativeCollectionContextReasonResponse(
            reason.MomentId,
            reason.AnchorRevisionIds.Select(anchor => anchor.ToString()).ToArray()))
        .ToArray();
}
