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
    int RequestedTargetCount,
    int SelectedDirectAnchorCount,
    int SelectedContextCount,
    int SelectedCount,
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
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        Guid id,
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        CancellationToken cancellationToken,
        int targetCount = 100,
        int momentGapMinutes = 30)
    {
        if (!TryCreatePolicies(
                id,
                targetCount,
                momentGapMinutes,
                out SmartCollectionId collectionId,
                out PhotoMomentGapPolicy? momentPolicy,
                out IResult? error))
        {
            return error!;
        }

        CreativeCollectionMaterialization? materialized = await MaterializeAsync(
            collectionId,
            momentPolicy!,
            targetCount,
            definitions,
            query,
            cancellationToken);
        if (materialized is null)
        {
            return Results.NotFound();
        }

        CreativeCollectionPreviewCandidateResponse[] candidates = materialized.Generated.Candidates
            .Select(ToPreviewCandidate)
            .ToArray();
        CreativeCollectionSelectedCandidateResponse[] selected = materialized.Selection.Selected
            .Select(ToSelectedCandidate)
            .ToArray();

        return Results.Ok(new CreativeCollectionPreviewResponse(
            materialized.Definition.Id.ToString(),
            materialized.Definition.Name,
            materialized.Generated.MomentPolicyVersion,
            materialized.Generated.ContextPolicyVersion,
            materialized.Generated.DirectAnchorCount,
            materialized.Generated.AddedContextCount,
            materialized.Generated.TotalCandidateCount,
            materialized.Generated.NoAnchors,
            materialized.Selection.PolicyVersion,
            materialized.Selection.RequestedTargetCount,
            materialized.Selection.SelectedDirectAnchorCount,
            materialized.Selection.SelectedContextCount,
            materialized.Selection.SelectedCount,
            candidates,
            selected));
    }

    private static async Task<IResult> CreateCreativeSlideshowSnapshotAsync(
        Guid id,
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        int targetCount = 100,
        int momentGapMinutes = 30)
    {
        if (!TryCreatePolicies(
                id,
                targetCount,
                momentGapMinutes,
                out SmartCollectionId collectionId,
                out PhotoMomentGapPolicy? momentPolicy,
                out IResult? error))
        {
            return error!;
        }

        CreativeCollectionMaterialization? materialized = await MaterializeAsync(
            collectionId,
            momentPolicy!,
            targetCount,
            definitions,
            query,
            cancellationToken);
        if (materialized is null)
        {
            return Results.NotFound();
        }

        SmartCollectionSlideshowSnapshotItemResponse[] items = materialized.Selection.Selected
            .Select(item => new SmartCollectionSlideshowSnapshotItemResponse(
                item.Candidate.RevisionId.ToString()))
            .ToArray();

        return Results.Ok(new SmartCollectionSlideshowSnapshotResponse(
            materialized.Definition.Id.ToString(),
            materialized.Definition.Name,
            timeProvider.GetUtcNow().ToUniversalTime(),
            items,
            items.Length));
    }

    private static async Task<CreativeCollectionMaterialization?> MaterializeAsync(
        SmartCollectionId collectionId,
        PhotoMomentGapPolicy momentPolicy,
        int targetCount,
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        CancellationToken cancellationToken)
    {
        SmartCollectionDefinition? definition =
            await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return null;
        }

        SmartCollectionSlideshowSnapshot? anchorSnapshot =
            await query.CreateSlideshowSnapshotAsync(collectionId, cancellationToken);
        if (anchorSnapshot is null)
        {
            return null;
        }

        IReadOnlyList<AssetRevisionId> anchorRevisionIds = anchorSnapshot.RevisionIds;
        if (anchorRevisionIds.Count == 0)
        {
            PhotoMomentClusteringResult noMoments = PhotoMomentClusterer.Cluster([], momentPolicy);
            CreativeCollectionCandidateSet noCandidates = CreativeCollectionCandidateGenerator.Generate(
                [],
                [],
                noMoments,
                CreativeCollectionContextPolicy.BalancedV1);
            CreativeCollectionSelectionResult noSelection = CreativeCollectionSelector.Select(
                noCandidates,
                [],
                noMoments,
                targetCount,
                CreativeCollectionSelectionPolicy.BalancedV1);
            return new CreativeCollectionMaterialization(
                definition,
                noCandidates,
                noSelection);
        }

        // Use the same Smart Collection query repository for context so deleted/excluded visibility
        // remains identical to direct anchors. Empty filtering only broadens eligibility for same-moment
        // context; it does not change archive truth or persist Creative Collection membership.
        IReadOnlyList<SmartCollectionPhoto> cataloguePhotos = await query.QueryAllAsync(
            new SmartCollectionFilter(),
            cancellationToken);

        PhotoMomentCandidate[] momentCandidates = cataloguePhotos
            .Select(photo => new PhotoMomentCandidate(
                photo.RevisionId,
                photo.TakenAtLocal,
                PeopleKeys: photo.PeopleKeys,
                Latitude: photo.Latitude,
                Longitude: photo.Longitude))
            .ToArray();
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            momentCandidates,
            momentPolicy);
        CreativeCollectionCandidateSet generated = CreativeCollectionCandidateGenerator.Generate(
            momentCandidates,
            anchorRevisionIds,
            moments,
            CreativeCollectionContextPolicy.BalancedV1);
        CreativeCollectionSelectionResult selection = CreativeCollectionSelector.Select(
            generated,
            momentCandidates,
            moments,
            targetCount,
            CreativeCollectionSelectionPolicy.BalancedV1);

        return new CreativeCollectionMaterialization(
            definition,
            generated,
            selection);
    }

    private static bool TryCreatePolicies(
        Guid id,
        int targetCount,
        int momentGapMinutes,
        out SmartCollectionId collectionId,
        out PhotoMomentGapPolicy? momentPolicy,
        out IResult? error)
    {
        try
        {
            collectionId = SmartCollectionId.From(id);
            CreativeCollectionSelectionPolicy.ValidateTargetCount(targetCount);
            momentPolicy = PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            collectionId = default;
            momentPolicy = null;
            error = Results.BadRequest(new { error = exception.Message });
            return false;
        }
    }

    private static CreativeCollectionPreviewCandidateResponse ToPreviewCandidate(
        CreativeCollectionCandidate candidate) => new(
        candidate.RevisionId.ToString(),
        candidate.Kind,
        candidate.TakenAtLocal,
        $"/api/collections/photos/{candidate.RevisionId}/thumbnail",
        ToContextReasons(candidate.ContextReasons));

    private static CreativeCollectionSelectedCandidateResponse ToSelectedCandidate(
        CreativeCollectionSelectedCandidate selected) => new(
        selected.Candidate.RevisionId.ToString(),
        selected.Candidate.Kind,
        selected.Candidate.TakenAtLocal,
        $"/api/collections/photos/{selected.Candidate.RevisionId}/thumbnail",
        selected.MomentId,
        selected.PeopleCombinationKey,
        selected.SelectionScore,
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

    private sealed record CreativeCollectionMaterialization(
        SmartCollectionDefinition Definition,
        CreativeCollectionCandidateSet Generated,
        CreativeCollectionSelectionResult Selection);
}
