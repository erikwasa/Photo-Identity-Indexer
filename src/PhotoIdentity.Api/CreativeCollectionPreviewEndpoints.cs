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

public sealed record CreativeCollectionPreviewResponse(
    string CollectionId,
    string CollectionName,
    string MomentPolicyVersion,
    string ContextPolicyVersion,
    int DirectAnchorCount,
    int AddedContextCount,
    int TotalCandidateCount,
    bool NoAnchors,
    CreativeCollectionPreviewCandidateResponse[] Candidates);

public static class CreativeCollectionPreviewEndpoints
{
    private const int QueryPageSize = 200;

    public static IEndpointRouteBuilder MapCreativeCollectionPreviewEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/smart-collections/{id:guid}/creative-preview",
            PreviewAsync);
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        Guid id,
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        CancellationToken cancellationToken,
        int momentGapMinutes = 30)
    {
        SmartCollectionId collectionId;
        try
        {
            collectionId = SmartCollectionId.From(id);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        PhotoMomentGapPolicy momentPolicy;
        try
        {
            momentPolicy = PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        SmartCollectionDefinition? definition =
            await definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<SmartCollectionPhoto> anchorPhotos = await QueryAllAsync(
            query,
            definition.Filter,
            cancellationToken);
        if (anchorPhotos.Count == 0)
        {
            return Results.Ok(new CreativeCollectionPreviewResponse(
                definition.Id.ToString(),
                definition.Name,
                momentPolicy.Version,
                CreativeCollectionContextPolicy.BalancedV1.Version,
                0,
                0,
                0,
                true,
                []));
        }

        // The context catalogue is intentionally obtained through the same Smart Collection query
        // repository as direct anchors. That keeps source deletion/exclusion visibility boundaries
        // identical; only the filter is empty so same-moment non-matches can be considered.
        IReadOnlyList<SmartCollectionPhoto> cataloguePhotos = await QueryAllAsync(
            query,
            new SmartCollectionFilter(),
            cancellationToken);

        PhotoMomentCandidate[] momentCandidates = cataloguePhotos
            .Select(photo => new PhotoMomentCandidate(
                photo.RevisionId,
                photo.TakenAtLocal,
                Latitude: photo.Latitude,
                Longitude: photo.Longitude))
            .ToArray();
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            momentCandidates,
            momentPolicy);
        CreativeCollectionCandidateSet generated = CreativeCollectionCandidateGenerator.Generate(
            momentCandidates,
            anchorPhotos.Select(photo => photo.RevisionId),
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        CreativeCollectionPreviewCandidateResponse[] candidates = generated.Candidates
            .Select(candidate => new CreativeCollectionPreviewCandidateResponse(
                candidate.RevisionId.ToString(),
                candidate.Kind,
                candidate.TakenAtLocal,
                $"/api/collections/photos/{candidate.RevisionId}/thumbnail",
                candidate.ContextReasons
                    .Select(reason => new CreativeCollectionContextReasonResponse(
                        reason.MomentId,
                        reason.AnchorRevisionIds.Select(anchor => anchor.ToString()).ToArray()))
                    .ToArray()))
            .ToArray();

        return Results.Ok(new CreativeCollectionPreviewResponse(
            definition.Id.ToString(),
            definition.Name,
            generated.MomentPolicyVersion,
            generated.ContextPolicyVersion,
            generated.DirectAnchorCount,
            generated.AddedContextCount,
            generated.TotalCandidateCount,
            generated.NoAnchors,
            candidates));
    }

    private static async Task<IReadOnlyList<SmartCollectionPhoto>> QueryAllAsync(
        ISmartCollectionQueryRepository query,
        SmartCollectionFilter filter,
        CancellationToken cancellationToken)
    {
        List<SmartCollectionPhoto> items = [];
        int offset = 0;

        while (true)
        {
            SmartCollectionPhotoPage page = await query.QueryAsync(
                filter,
                offset,
                QueryPageSize,
                cancellationToken);
            items.AddRange(page.Items);
            offset += page.Items.Count;

            if (page.Items.Count == 0 || offset >= page.Total)
            {
                break;
            }
        }

        return items;
    }
}
