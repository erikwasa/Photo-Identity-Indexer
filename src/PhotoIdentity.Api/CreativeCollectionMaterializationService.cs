using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record CreativeCollectionMaterialization(
    SmartCollectionDefinition Definition,
    CreativeCollectionCandidateSet Generated,
    CreativeCollectionSelectionResult Selection);

public sealed class CreativeCollectionMaterializationService
{
    private readonly ISmartCollectionRepository _definitions;
    private readonly ISmartCollectionQueryRepository _query;

    public CreativeCollectionMaterializationService(
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(query);
        _definitions = definitions;
        _query = query;
    }

    public async Task<CreativeCollectionMaterialization?> MaterializeAsync(
        SmartCollectionId collectionId,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateSupported();

        SmartCollectionDefinition? definition =
            await _definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return null;
        }

        SmartCollectionSlideshowSnapshot? anchorSnapshot =
            await _query.CreateSlideshowSnapshotAsync(collectionId, cancellationToken);
        if (anchorSnapshot is null)
        {
            return null;
        }

        PhotoMomentGapPolicy momentPolicy =
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(settings.MomentGapMinutes);
        CreativeCollectionContextPolicy contextPolicy =
            CreativeCollectionContextPolicy.FromVersion(settings.ContextPolicyVersion);

        IReadOnlyList<AssetRevisionId> anchorRevisionIds = anchorSnapshot.RevisionIds;
        if (anchorRevisionIds.Count == 0)
        {
            PhotoMomentClusteringResult noMoments = PhotoMomentClusterer.Cluster([], momentPolicy);
            CreativeCollectionCandidateSet noCandidates = CreativeCollectionCandidateGenerator.Generate(
                [],
                [],
                noMoments,
                contextPolicy);
            CreativeCollectionSelectionResult noSelection = CreativeCollectionSelector.Select(
                noCandidates,
                [],
                noMoments,
                settings.TargetCount,
                CreativeCollectionSelectionPolicy.BalancedV1);
            return new CreativeCollectionMaterialization(
                definition,
                noCandidates,
                noSelection);
        }

        IReadOnlyList<SmartCollectionPhoto> cataloguePhotos = await _query.QueryAllAsync(
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
            contextPolicy);
        CreativeCollectionSelectionResult selection = CreativeCollectionSelector.Select(
            generated,
            momentCandidates,
            moments,
            settings.TargetCount,
            CreativeCollectionSelectionPolicy.BalancedV1);

        return new CreativeCollectionMaterialization(
            definition,
            generated,
            selection);
    }
}
