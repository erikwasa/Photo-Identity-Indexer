using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

public sealed record CreativeCollectionMaterialization(
    SmartCollectionDefinition Definition,
    CreativeCollectionCandidateSet Generated,
    CreativeCollectionSelectionResult Selection,
    PhotoVisualRedundancyResult VisualRedundancy,
    IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary> ExposureHistory,
    string NoveltyPolicyVersion);

public sealed class CreativeCollectionMaterializationService
{
    private static readonly int VisualHashConcurrency = Math.Clamp(Environment.ProcessorCount, 4, 12);

    private readonly ISmartCollectionRepository _definitions;
    private readonly ISmartCollectionQueryRepository _query;
    private readonly CollectionReviewProxyFileResolver _proxyResolver;
    private readonly IPhotoPresentationPreferenceRepository _presentationPreferences;
    private readonly IPhotoSlideshowExposureRepository _exposures;
    private readonly TimeProvider _timeProvider;
    private readonly CreativeVisualFingerprintCache _fingerprintCache;
    private readonly ILogger<CreativeCollectionMaterializationService>? _logger;

    public CreativeCollectionMaterializationService(
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        CollectionReviewProxyFileResolver proxyResolver,
        IPhotoPresentationPreferenceRepository presentationPreferences,
        IPhotoSlideshowExposureRepository exposures,
        TimeProvider timeProvider,
        ILogger<CreativeCollectionMaterializationService>? logger = null)
        : this(
            definitions,
            query,
            proxyResolver,
            presentationPreferences,
            exposures,
            timeProvider,
            new CreativeVisualFingerprintCache(),
            logger)
    {
    }

    public CreativeCollectionMaterializationService(
        ISmartCollectionRepository definitions,
        ISmartCollectionQueryRepository query,
        CollectionReviewProxyFileResolver proxyResolver,
        IPhotoPresentationPreferenceRepository presentationPreferences,
        IPhotoSlideshowExposureRepository exposures,
        TimeProvider timeProvider,
        CreativeVisualFingerprintCache fingerprintCache,
        ILogger<CreativeCollectionMaterializationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(proxyResolver);
        ArgumentNullException.ThrowIfNull(presentationPreferences);
        ArgumentNullException.ThrowIfNull(exposures);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(fingerprintCache);
        _definitions = definitions;
        _query = query;
        _proxyResolver = proxyResolver;
        _presentationPreferences = presentationPreferences;
        _exposures = exposures;
        _timeProvider = timeProvider;
        _fingerprintCache = fingerprintCache;
        _logger = logger;
    }

    public async Task<CreativeCollectionMaterialization?> MaterializeAsync(
        SmartCollectionId collectionId,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateSupported();

        Stopwatch totalTimer = Stopwatch.StartNew();
        long definitionAndAnchorMilliseconds;
        long catalogueAndCandidatesMilliseconds;
        long visualMilliseconds;
        long preferenceAndExposureMilliseconds;
        long selectionMilliseconds;

        Stopwatch phase = Stopwatch.StartNew();
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
        definitionAndAnchorMilliseconds = phase.ElapsedMilliseconds;

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
            PhotoVisualRedundancyResult noVisualRedundancy = PhotoVisualRedundancyGrouper.Group(
                [],
                noMoments,
                PhotoVisualRedundancyPolicy.AcceptedCreativeV1);
            return new CreativeCollectionMaterialization(
                definition,
                noCandidates,
                noSelection,
                noVisualRedundancy,
                new Dictionary<AssetRevisionId, PhotoSlideshowExposureSummary>(),
                settings.NoveltyEnabled
                    ? CreativeCollectionNoveltyPolicies.BalancedV1
                    : CreativeCollectionNoveltyPolicies.Disabled);
        }

        phase.Restart();
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
        catalogueAndCandidatesMilliseconds = phase.ElapsedMilliseconds;

        phase.Restart();
        VisualRedundancyBuild visual = await BuildAcceptedVisualRedundancyAsync(
            generated.Candidates,
            moments,
            cancellationToken);
        visualMilliseconds = phase.ElapsedMilliseconds;

        phase.Restart();
        IReadOnlyDictionary<AssetRevisionId, string> presentationPreferences =
            await _presentationPreferences.GetEffectiveAsync(
                generated.Candidates.Select(candidate => candidate.RevisionId),
                cancellationToken);
        IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary> exposureHistory =
            await _exposures.GetSummariesAsync(
                generated.Candidates.Select(candidate => candidate.RevisionId),
                cancellationToken);
        preferenceAndExposureMilliseconds = phase.ElapsedMilliseconds;

        phase.Restart();
        CreativeCollectionSelectionResult selection = CreativeCollectionSelector.Select(
            generated,
            momentCandidates,
            moments,
            visual.Result,
            presentationPreferences,
            exposureHistory,
            settings.NoveltyEnabled,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            settings.TargetCount,
            CreativeCollectionSelectionPolicy.BalancedV1);
        selectionMilliseconds = phase.ElapsedMilliseconds;
        totalTimer.Stop();

        _logger?.LogInformation(
            "Creative materialization: anchors={AnchorCount} catalogue={CatalogueCount} candidates={CandidateCount} target={TargetCount} visualEligible={VisualEligibleCount} proxies={ResolvedProxyCount} cacheHits={CacheHitCount} hashesComputed={HashesComputed} missingOrUnreadable={MissingOrUnreadableCount} msAnchor={AnchorMilliseconds} msCatalogueCandidates={CatalogueCandidatesMilliseconds} msVisual={VisualMilliseconds} msPreferencesExposure={PreferenceExposureMilliseconds} msSelection={SelectionMilliseconds} msTotal={TotalMilliseconds}.",
            anchorRevisionIds.Count,
            cataloguePhotos.Count,
            generated.TotalCandidateCount,
            settings.TargetCount,
            visual.EligibleCandidateCount,
            visual.ResolvedProxyCount,
            visual.CacheHitCount,
            visual.ComputedCount,
            visual.MissingOrUnreadableCount,
            definitionAndAnchorMilliseconds,
            catalogueAndCandidatesMilliseconds,
            visualMilliseconds,
            preferenceAndExposureMilliseconds,
            selectionMilliseconds,
            totalTimer.ElapsedMilliseconds);

        return new CreativeCollectionMaterialization(
            definition,
            generated,
            selection,
            visual.Result,
            exposureHistory,
            settings.NoveltyEnabled
                ? CreativeCollectionNoveltyPolicies.BalancedV1
                : CreativeCollectionNoveltyPolicies.Disabled);
    }

    internal static CreativeCollectionCandidate[] SelectVisualFingerprintCandidates(
        IReadOnlyList<CreativeCollectionCandidate> candidates,
        PhotoMomentClusteringResult moments,
        PhotoVisualRedundancyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(policy);

        Dictionary<AssetRevisionId, string> momentByRevision = moments.Moments
            .SelectMany(moment => moment.Members.Select(member => (member.RevisionId, moment.Id)))
            .ToDictionary(pair => pair.RevisionId, pair => pair.Id);

        List<CreativeCollectionCandidate> eligible = [];
        foreach (IGrouping<string, CreativeCollectionCandidate> group in candidates
                     .Where(candidate =>
                         candidate.TakenAtLocal.HasValue &&
                         momentByRevision.ContainsKey(candidate.RevisionId))
                     .GroupBy(candidate => momentByRevision[candidate.RevisionId], StringComparer.Ordinal))
        {
            CreativeCollectionCandidate[] ordered = group
                .OrderBy(candidate => candidate.TakenAtLocal!.Value)
                .ThenBy(candidate => candidate.RevisionId.ToString(), StringComparer.Ordinal)
                .ToArray();
            for (int index = 0; index < ordered.Length; index++)
            {
                DateTime takenAt = ordered[index].TakenAtLocal!.Value;
                bool previousWithinSpan = index > 0 &&
                    takenAt - ordered[index - 1].TakenAtLocal!.Value <= policy.MaximumCaptureSpan;
                bool nextWithinSpan = index + 1 < ordered.Length &&
                    ordered[index + 1].TakenAtLocal!.Value - takenAt <= policy.MaximumCaptureSpan;
                if (previousWithinSpan || nextWithinSpan)
                {
                    eligible.Add(ordered[index]);
                }
            }
        }

        return eligible
            .OrderBy(candidate => candidate.TakenAtLocal.HasValue ? 0 : 1)
            .ThenBy(candidate => candidate.TakenAtLocal ?? DateTime.MaxValue)
            .ThenBy(candidate => candidate.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<VisualRedundancyBuild> BuildAcceptedVisualRedundancyAsync(
        IReadOnlyList<CreativeCollectionCandidate> candidates,
        PhotoMomentClusteringResult moments,
        CancellationToken cancellationToken)
    {
        PhotoVisualRedundancyPolicy policy = PhotoVisualRedundancyPolicy.AcceptedCreativeV1;
        CreativeCollectionCandidate[] eligible = SelectVisualFingerprintCandidates(
            candidates,
            moments,
            policy);
        if (eligible.Length == 0)
        {
            return new VisualRedundancyBuild(
                PhotoVisualRedundancyGrouper.Group([], moments, policy),
                0,
                0,
                0,
                0,
                0);
        }

        IReadOnlyDictionary<AssetRevisionId, ResolvedCollectionReviewProxy> proxies =
            await _proxyResolver.ResolveManyWithMetadataAsync(
                eligible.Select(candidate => candidate.RevisionId).ToArray(),
                cancellationToken);

        ConcurrentBag<PhotoVisualFingerprint> fingerprints = [];
        int cacheHits = 0;
        int computed = 0;
        int missingOrUnreadable = eligible.Length - proxies.Count;
        using SemaphoreSlim gate = new(VisualHashConcurrency);
        OpenCvPerceptualHashCalculator calculator = new();

        Task[] work = eligible.Select(async candidate =>
        {
            if (!proxies.TryGetValue(candidate.RevisionId, out ResolvedCollectionReviewProxy? proxy))
            {
                return;
            }

            if (_fingerprintCache.TryGet(
                    candidate.RevisionId,
                    proxy.Metadata.ContentHash,
                    PhotoVisualRedundancyPolicy.AlgorithmVersion,
                    out PhotoPerceptualHash64 cached))
            {
                Interlocked.Increment(ref cacheHits);
                fingerprints.Add(new PhotoVisualFingerprint(
                    candidate.RevisionId,
                    candidate.TakenAtLocal,
                    cached));
                return;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                // Recheck after waiting so concurrent materializations converge on the same cache.
                if (_fingerprintCache.TryGet(
                        candidate.RevisionId,
                        proxy.Metadata.ContentHash,
                        PhotoVisualRedundancyPolicy.AlgorithmVersion,
                        out cached))
                {
                    Interlocked.Increment(ref cacheHits);
                    fingerprints.Add(new PhotoVisualFingerprint(
                        candidate.RevisionId,
                        candidate.TakenAtLocal,
                        cached));
                    return;
                }

                try
                {
                    PhotoPerceptualHash64 hash = await calculator.ComputeAsync(
                        proxy.File.Path,
                        cancellationToken);
                    _fingerprintCache.Store(
                        candidate.RevisionId,
                        proxy.Metadata.ContentHash,
                        PhotoVisualRedundancyPolicy.AlgorithmVersion,
                        hash);
                    Interlocked.Increment(ref computed);
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
                    Interlocked.Increment(ref missingOrUnreadable);
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

        return new VisualRedundancyBuild(
            PhotoVisualRedundancyGrouper.Group(
                fingerprints,
                moments,
                policy),
            eligible.Length,
            proxies.Count,
            cacheHits,
            computed,
            missingOrUnreadable);
    }

    private sealed record VisualRedundancyBuild(
        PhotoVisualRedundancyResult Result,
        int EligibleCandidateCount,
        int ResolvedProxyCount,
        int CacheHitCount,
        int ComputedCount,
        int MissingOrUnreadableCount);
}
