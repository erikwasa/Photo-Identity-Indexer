using System.Collections.Concurrent;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

public sealed record CreativeCollectionMaterialization(
    SmartCollectionDefinition Definition,
    CreativeCollectionCandidateSet Generated,
    CreativeCollectionSelectionResult Selection);

public sealed class CreativeCollectionMaterializationService
{
    private const int VisualHashConcurrency = 4;

    private readonly ISmartCollectionRepository _definitions;
    private readonly ISmartCollectionQueryRepository _query;
    private readonly CollectionReviewProxyFileResolver _proxyResolver;

    public CreativeCollectionMaterializationService(
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        CollectionReviewProxyFileResolver proxyResolver)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(proxyResolver);
        _definitions = definitions;
        _query = query;
        _proxyResolver = proxyResolver;
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
        PhotoVisualRedundancyResult visualRedundancy =
            await BuildAcceptedVisualRedundancyAsync(
                generated.Candidates,
                moments,
                cancellationToken);
        CreativeCollectionSelectionResult selection = CreativeCollectionSelector.Select(
            generated,
            momentCandidates,
            moments,
            visualRedundancy,
            settings.TargetCount,
            CreativeCollectionSelectionPolicy.BalancedV1);

        return new CreativeCollectionMaterialization(
            definition,
            generated,
            selection);
    }

    private async Task<PhotoVisualRedundancyResult> BuildAcceptedVisualRedundancyAsync(
        IReadOnlyList<CreativeCollectionCandidate> candidates,
        PhotoMomentClusteringResult moments,
        CancellationToken cancellationToken)
    {
        ConcurrentBag<PhotoVisualFingerprint> fingerprints = [];
        using SemaphoreSlim gate = new(VisualHashConcurrency);
        OpenCvPerceptualHashCalculator calculator = new();

        Task[] work = candidates.Select(async candidate =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                CollectionPhotoFile? proxy = await _proxyResolver.ResolveAsync(
                    candidate.RevisionId,
                    cancellationToken);
                if (proxy is null)
                {
                    return;
                }

                try
                {
                    PhotoPerceptualHash64 hash = await calculator.ComputeAsync(
                        proxy.Path,
                        cancellationToken);
                    fingerprints.Add(new PhotoVisualFingerprint(
                        candidate.RevisionId,
                        candidate.TakenAtLocal,
                        hash));
                }
                catch (Exception exception) when (
                    exception is InvalidDataException or
                    IOException or
                    UnauthorizedAccessException)
                {
                    // Visual redundancy is optional derived evidence. A missing or unreadable
                    // proxy must never make Creative materialization fail or hydrate originals.
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(work);

        return PhotoVisualRedundancyGrouper.Group(
            fingerprints,
            moments,
            PhotoVisualRedundancyPolicy.AcceptedCreativeV1);
    }
}
