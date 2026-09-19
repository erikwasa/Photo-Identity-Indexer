using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public static class CreativeCollectionSelectionReasonCodes
{
    public const string DirectAnchor = "direct-anchor";
    public const string NewMoment = "new-moment";
    public const string NewTemporalBucket = "new-temporal-bucket";
    public const string RepeatedTemporalBucket = "repeated-temporal-bucket";
    public const string NewPeopleCombination = "new-people-combination";
    public const string ContextView = "context-view";
    public const string RepeatedMoment = "repeated-moment";
    public const string RepeatedPeopleCombination = "repeated-people-combination";
    public const string NearConsecutive = "near-consecutive";
    public const string VisualRedundancy = "visual-redundancy";
    public const string PresentationPrefer = "presentation-prefer";
    public const string NoveltyUnseen = "novelty-unseen";
    public const string NoveltyRecent = "novelty-recent";
    public const string NoveltyFrequency = "novelty-frequency";
    public const string SemanticNewConcept = "semantic-new-concept";
    public const string EmbeddingSimilarity = "embedding-similarity";
}

public static class CreativeCollectionEmbeddingDiversityPolicies
{
    public const string Disabled = "disabled";
    public const string BalancedV1 = "m26-image-embedding-diversity-v1";
}

public static class CreativeCollectionSemanticDiversityPolicies
{
    public const string Disabled = "disabled";
    public const string BalancedV1 = "m26-visible-content-diversity-v1";
}

/// <summary>
/// Versioned metadata-first policy for turning Creative Collection candidates into a bounded,
/// slideshow-ready sequence. Selection is deterministic for the same candidates, metadata,
/// target and policy version.
/// </summary>
public sealed record CreativeCollectionSelectionPolicy
{
    public const int MinimumTargetCount = 1;
    public const int MaximumTargetCount = 1000;

    public CreativeCollectionSelectionPolicy(
        string version,
        int temporalBucketCount,
        TimeSpan nearConsecutiveWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (temporalBucketCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(temporalBucketCount));
        }

        if (nearConsecutiveWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(nearConsecutiveWindow));
        }

        Version = version.Trim();
        TemporalBucketCount = temporalBucketCount;
        NearConsecutiveWindow = nearConsecutiveWindow;
    }

    public string Version { get; }
    public int TemporalBucketCount { get; }
    public TimeSpan NearConsecutiveWindow { get; }

    public static CreativeCollectionSelectionPolicy BalancedV1 { get; } =
        new("m26-target-diversity-balanced-v1", 8, TimeSpan.FromMinutes(2));

    public static void ValidateTargetCount(int targetCount)
    {
        if (targetCount is < MinimumTargetCount or > MaximumTargetCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetCount),
                $"Creative Collection target count must be between {MinimumTargetCount} and {MaximumTargetCount}.");
        }
    }
}

public sealed record CreativeCollectionSelectionReason(
    string Code,
    int ScoreDelta,
    string Detail);

public sealed record CreativeCollectionSelectedCandidate(
    CreativeCollectionCandidate Candidate,
    string? MomentId,
    string? PeopleCombinationKey,
    int SelectionScore,
    IReadOnlyList<CreativeCollectionSelectionReason> Reasons);

public sealed record CreativeCollectionSelectionResult(
    string PolicyVersion,
    int RequestedTargetCount,
    int AvailableCandidateCount,
    int SelectedDirectAnchorCount,
    int SelectedContextCount,
    IReadOnlyList<CreativeCollectionSelectedCandidate> Selected)
{
    public int SelectedCount => Selected.Count;
}

/// <summary>
/// Greedy deterministic selector that rewards coverage before repetition. The score deliberately
/// uses only metadata already available to Creative Collections: direct/context provenance,
/// derived moments, capture time and identified-person combinations.
/// </summary>
public static class CreativeCollectionSelector
{
    public static CreativeCollectionSelectionResult Select(
        CreativeCollectionCandidateSet candidates,
        IEnumerable<PhotoMomentCandidate> catalogue,
        PhotoMomentClusteringResult moments,
        int targetCount,
        CreativeCollectionSelectionPolicy policy) =>
        Select(
            candidates,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: null,
            exposureHistory: null,
            noveltyEnabled: false,
            noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
            targetCount,
            policy);

    public static CreativeCollectionSelectionResult Select(
        CreativeCollectionCandidateSet candidates,
        IEnumerable<PhotoMomentCandidate> catalogue,
        PhotoMomentClusteringResult moments,
        PhotoVisualRedundancyResult? visualRedundancy,
        int targetCount,
        CreativeCollectionSelectionPolicy policy) =>
        Select(
            candidates,
            catalogue,
            moments,
            visualRedundancy,
            presentationPreferences: null,
            exposureHistory: null,
            noveltyEnabled: false,
            noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
            targetCount,
            policy);

    public static CreativeCollectionSelectionResult Select(
        CreativeCollectionCandidateSet candidates,
        IEnumerable<PhotoMomentCandidate> catalogue,
        PhotoMomentClusteringResult moments,
        PhotoVisualRedundancyResult? visualRedundancy,
        IReadOnlyDictionary<AssetRevisionId, string>? presentationPreferences,
        int targetCount,
        CreativeCollectionSelectionPolicy policy) =>
        Select(
            candidates,
            catalogue,
            moments,
            visualRedundancy,
            presentationPreferences,
            exposureHistory: null,
            noveltyEnabled: false,
            noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
            targetCount,
            policy);

    public static CreativeCollectionSelectionResult Select(
        CreativeCollectionCandidateSet candidates,
        IEnumerable<PhotoMomentCandidate> catalogue,
        PhotoMomentClusteringResult moments,
        PhotoVisualRedundancyResult? visualRedundancy,
        IReadOnlyDictionary<AssetRevisionId, string>? presentationPreferences,
        IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary>? exposureHistory,
        bool noveltyEnabled,
        DateTimeOffset noveltyEvaluatedAtUtc,
        int targetCount,
        CreativeCollectionSelectionPolicy policy) =>
        Select(
            candidates,
            catalogue,
            moments,
            visualRedundancy,
            presentationPreferences,
            exposureHistory,
            noveltyEnabled,
            noveltyEvaluatedAtUtc,
            semanticConcepts: null,
            semanticDiversityEnabled: false,
            targetCount,
            policy);

    public static CreativeCollectionSelectionResult Select(
        CreativeCollectionCandidateSet candidates,
        IEnumerable<PhotoMomentCandidate> catalogue,
        PhotoMomentClusteringResult moments,
        PhotoVisualRedundancyResult? visualRedundancy,
        IReadOnlyDictionary<AssetRevisionId, string>? presentationPreferences,
        IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary>? exposureHistory,
        bool noveltyEnabled,
        DateTimeOffset noveltyEvaluatedAtUtc,
        IReadOnlyDictionary<AssetRevisionId, IReadOnlyList<string>>? semanticConcepts,
        bool semanticDiversityEnabled,
        int targetCount,
        CreativeCollectionSelectionPolicy policy) =>
        Select(
            candidates,
            catalogue,
            moments,
            visualRedundancy,
            presentationPreferences,
            exposureHistory,
            noveltyEnabled,
            noveltyEvaluatedAtUtc,
            semanticConcepts,
            semanticDiversityEnabled,
            imageEmbeddings: null,
            embeddingDiversityEnabled: false,
            targetCount,
            policy);

    public static CreativeCollectionSelectionResult Select(
        CreativeCollectionCandidateSet candidates,
        IEnumerable<PhotoMomentCandidate> catalogue,
        PhotoMomentClusteringResult moments,
        PhotoVisualRedundancyResult? visualRedundancy,
        IReadOnlyDictionary<AssetRevisionId, string>? presentationPreferences,
        IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary>? exposureHistory,
        bool noveltyEnabled,
        DateTimeOffset noveltyEvaluatedAtUtc,
        IReadOnlyDictionary<AssetRevisionId, IReadOnlyList<string>>? semanticConcepts,
        bool semanticDiversityEnabled,
        IReadOnlyDictionary<AssetRevisionId, IReadOnlyList<float>>? imageEmbeddings,
        bool embeddingDiversityEnabled,
        int targetCount,
        CreativeCollectionSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(policy);
        CreativeCollectionSelectionPolicy.ValidateTargetCount(targetCount);

        Dictionary<AssetRevisionId, PhotoMomentCandidate> catalogueByRevision = catalogue
            .ToDictionary(candidate => candidate.RevisionId);
        Dictionary<AssetRevisionId, string> momentByRevision = moments.Moments
            .SelectMany(moment => moment.Members.Select(member => (member.RevisionId, moment.Id)))
            .ToDictionary(pair => pair.RevisionId, pair => pair.Id);
        Dictionary<AssetRevisionId, string> visualGroupByRevision = BuildVisualGroupMap(visualRedundancy);

        foreach (CreativeCollectionCandidate candidate in candidates.Candidates)
        {
            if (!catalogueByRevision.ContainsKey(candidate.RevisionId))
            {
                throw new ArgumentException(
                    $"Creative Collection candidate '{candidate.RevisionId}' is not present in the supplied catalogue.",
                    nameof(catalogue));
            }
        }

        CreativeCollectionCandidate[] eligibleCandidates = candidates.Candidates
            .Where(candidate =>
                !string.Equals(
                    presentationPreferences?.GetValueOrDefault(candidate.RevisionId),
                    PhotoPresentationPreferenceKinds.Avoid,
                    StringComparison.Ordinal))
            .ToArray();

        int selectionCount = Math.Min(targetCount, eligibleCandidates.Length);
        if (selectionCount == 0)
        {
            return new CreativeCollectionSelectionResult(
                policy.Version,
                targetCount,
                0,
                0,
                0,
                []);
        }

        Dictionary<AssetRevisionId, SelectionMetadata> metadata = eligibleCandidates
            .ToDictionary(
                candidate => candidate.RevisionId,
                candidate => CreateMetadata(
                    candidate,
                    catalogueByRevision[candidate.RevisionId],
                    momentByRevision.GetValueOrDefault(candidate.RevisionId),
                    visualGroupByRevision.GetValueOrDefault(candidate.RevisionId),
                    presentationPreferences?.GetValueOrDefault(candidate.RevisionId),
                    exposureHistory?.GetValueOrDefault(candidate.RevisionId),
                    semanticConcepts?.GetValueOrDefault(candidate.RevisionId),
                    NormalizeEmbedding(imageEmbeddings?.GetValueOrDefault(candidate.RevisionId))));
        AssignTemporalBuckets(metadata.Values, policy.TemporalBucketCount);

        List<CreativeCollectionSelectedCandidate> selected = [];
        HashSet<AssetRevisionId> selectedIds = [];
        Dictionary<string, int> momentCounts = new(StringComparer.Ordinal);
        Dictionary<int, int> temporalBucketCounts = [];
        Dictionary<string, int> peopleCombinationCounts = new(StringComparer.Ordinal);
        Dictionary<string, int> visualGroupCounts = new(StringComparer.Ordinal);
        Dictionary<string, int> semanticConceptCounts = new(StringComparer.Ordinal);
        List<IReadOnlyList<float>> selectedEmbeddings = [];

        while (selected.Count < selectionCount)
        {
            ScoredCandidate next = eligibleCandidates
                .Where(candidate => !selectedIds.Contains(candidate.RevisionId))
                .Select(candidate => Score(
                    candidate,
                    metadata[candidate.RevisionId],
                    selected,
                    momentCounts,
                    temporalBucketCounts,
                    peopleCombinationCounts,
                    visualGroupCounts,
                    semanticConceptCounts,
                    selectedEmbeddings,
                    noveltyEnabled,
                    noveltyEvaluatedAtUtc,
                    semanticDiversityEnabled,
                    embeddingDiversityEnabled,
                    policy))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Metadata.TakenAtLocal.HasValue ? 0 : 1)
                .ThenBy(candidate => candidate.Metadata.TakenAtLocal ?? DateTime.MaxValue)
                .ThenBy(candidate => candidate.Candidate.RevisionId.ToString(), StringComparer.Ordinal)
                .First();

            selectedIds.Add(next.Candidate.RevisionId);
            Increment(momentCounts, next.Metadata.MomentId);
            if (next.Metadata.TemporalBucket is int bucket)
            {
                temporalBucketCounts[bucket] = temporalBucketCounts.GetValueOrDefault(bucket) + 1;
            }
            Increment(peopleCombinationCounts, next.Metadata.PeopleCombinationKey);
            Increment(visualGroupCounts, next.Metadata.VisualGroupId);
            foreach (string concept in next.Metadata.SemanticConcepts)
            {
                semanticConceptCounts[concept] = semanticConceptCounts.GetValueOrDefault(concept) + 1;
            }
            if (next.Metadata.ImageEmbedding is not null)
            {
                selectedEmbeddings.Add(next.Metadata.ImageEmbedding);
            }

            selected.Add(new CreativeCollectionSelectedCandidate(
                next.Candidate,
                next.Metadata.MomentId,
                next.Metadata.PeopleCombinationKey,
                next.Score,
                next.Reasons));
        }

        CreativeCollectionSelectedCandidate[] finalOrder = selected
            .OrderBy(candidate => candidate.Candidate.TakenAtLocal.HasValue ? 0 : 1)
            .ThenBy(candidate => candidate.Candidate.TakenAtLocal ?? DateTime.MaxValue)
            .ThenBy(candidate => candidate.Candidate.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();

        List<string> effectivePolicies = [policy.Version];
        if (semanticDiversityEnabled)
        {
            effectivePolicies.Add(CreativeCollectionSemanticDiversityPolicies.BalancedV1);
        }
        if (embeddingDiversityEnabled)
        {
            effectivePolicies.Add(CreativeCollectionEmbeddingDiversityPolicies.BalancedV1);
        }
        string effectivePolicyVersion = string.Join("+", effectivePolicies);

        return new CreativeCollectionSelectionResult(
            effectivePolicyVersion,
            targetCount,
            eligibleCandidates.Length,
            finalOrder.Count(candidate =>
                candidate.Candidate.Kind == CreativeCollectionCandidateKinds.DirectAnchor),
            finalOrder.Count(candidate =>
                candidate.Candidate.Kind == CreativeCollectionCandidateKinds.ContextualAddition),
            finalOrder);
    }

    private static SelectionMetadata CreateMetadata(
        CreativeCollectionCandidate candidate,
        PhotoMomentCandidate source,
        string? momentId,
        string? visualGroupId,
        string? presentationPreference,
        PhotoSlideshowExposureSummary? exposure,
        IReadOnlyList<string>? semanticConcepts,
        IReadOnlyList<float>? imageEmbedding)
    {
        string? peopleCombinationKey = source.PeopleKeys is null
            ? null
            : string.Join(
                "+",
                source.PeopleKeys
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(peopleCombinationKey))
        {
            peopleCombinationKey = null;
        }

        return new SelectionMetadata(
            candidate.TakenAtLocal,
            momentId,
            peopleCombinationKey,
            visualGroupId,
            presentationPreference,
            exposure,
            NormalizeSemanticConcepts(semanticConcepts),
            imageEmbedding);
    }

    private static void AssignTemporalBuckets(
        IEnumerable<SelectionMetadata> metadata,
        int bucketCount)
    {
        SelectionMetadata[] timestamped = metadata
            .Where(item => item.TakenAtLocal.HasValue)
            .ToArray();
        if (timestamped.Length == 0)
        {
            return;
        }

        DateTime minimum = timestamped.Min(item => item.TakenAtLocal!.Value);
        DateTime maximum = timestamped.Max(item => item.TakenAtLocal!.Value);
        long spanTicks = Math.Max(1, maximum.Ticks - minimum.Ticks);
        foreach (SelectionMetadata item in timestamped)
        {
            long offsetTicks = item.TakenAtLocal!.Value.Ticks - minimum.Ticks;
            int bucket = (int)Math.Min(
                bucketCount - 1,
                (double)offsetTicks * bucketCount / (spanTicks + 1d));
            item.TemporalBucket = bucket;
        }
    }

    private static ScoredCandidate Score(
        CreativeCollectionCandidate candidate,
        SelectionMetadata metadata,
        IReadOnlyList<CreativeCollectionSelectedCandidate> selected,
        IReadOnlyDictionary<string, int> momentCounts,
        IReadOnlyDictionary<int, int> temporalBucketCounts,
        IReadOnlyDictionary<string, int> peopleCombinationCounts,
        IReadOnlyDictionary<string, int> visualGroupCounts,
        IReadOnlyDictionary<string, int> semanticConceptCounts,
        IReadOnlyList<IReadOnlyList<float>> selectedEmbeddings,
        bool noveltyEnabled,
        DateTimeOffset noveltyEvaluatedAtUtc,
        bool semanticDiversityEnabled,
        bool embeddingDiversityEnabled,
        CreativeCollectionSelectionPolicy policy)
    {
        List<CreativeCollectionSelectionReason> reasons = [];
        int score = 100;

        if (candidate.Kind == CreativeCollectionCandidateKinds.DirectAnchor)
        {
            AddReason(reasons, CreativeCollectionSelectionReasonCodes.DirectAnchor, 35,
                "Direct Smart Collection anchors retain a relevance preference.");
            score += 35;
        }
        else
        {
            AddReason(reasons, CreativeCollectionSelectionReasonCodes.ContextView, 15,
                "Context photos receive a smaller base value so distinct contextual views can survive selection.");
            score += 15;
        }

        if (string.Equals(
                metadata.PresentationPreference,
                PhotoPresentationPreferenceKinds.Prefer,
                StringComparison.Ordinal))
        {
            AddReason(
                reasons,
                CreativeCollectionSelectionReasonCodes.PresentationPrefer,
                220,
                "Explicit presentation preference increases this eligible photo's priority.");
            score += 220;
        }

        if (noveltyEnabled)
        {
            if (metadata.Exposure is null || metadata.Exposure.ShowCount == 0)
            {
                AddReason(
                    reasons,
                    CreativeCollectionSelectionReasonCodes.NoveltyUnseen,
                    100,
                    "This photo has not been recorded as presented in a prior slideshow session.");
                score += 100;
            }
            else
            {
                if (metadata.Exposure.LastShownAtUtc is DateTimeOffset lastShown)
                {
                    TimeSpan age = noveltyEvaluatedAtUtc.ToUniversalTime() - lastShown.ToUniversalTime();
                    int recencyPenalty = age <= TimeSpan.FromDays(7)
                        ? -100
                        : age <= TimeSpan.FromDays(30)
                            ? -60
                            : age <= TimeSpan.FromDays(180)
                                ? -25
                                : 0;
                    if (recencyPenalty != 0)
                    {
                        AddReason(
                            reasons,
                            CreativeCollectionSelectionReasonCodes.NoveltyRecent,
                            recencyPenalty,
                            $"Previously presented {Math.Max(0, age.TotalDays):0.#} day(s) before this selection.");
                        score += recencyPenalty;
                    }
                }

                int frequencyPenalty = -Math.Min(40, metadata.Exposure.ShowCount * 5);
                if (frequencyPenalty != 0)
                {
                    AddReason(
                        reasons,
                        CreativeCollectionSelectionReasonCodes.NoveltyFrequency,
                        frequencyPenalty,
                        $"Previously presented in {metadata.Exposure.ShowCount} slideshow session(s).");
                    score += frequencyPenalty;
                }
            }
        }

        if (metadata.MomentId is string momentId)
        {
            int count = momentCounts.GetValueOrDefault(momentId);
            if (count == 0)
            {
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.NewMoment, 90,
                    $"Adds coverage for moment '{momentId}'.");
                score += 90;
            }
            else
            {
                int penalty = -45 * count;
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.RepeatedMoment, penalty,
                    $"Moment '{momentId}' already has {count} selected photo(s).");
                score += penalty;
            }
        }

        if (metadata.TemporalBucket is int bucket)
        {
            int count = temporalBucketCounts.GetValueOrDefault(bucket);
            if (count == 0)
            {
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.NewTemporalBucket, 70,
                    $"Adds coverage for temporal bucket {bucket + 1}.");
                score += 70;
            }
            else
            {
                int penalty = -Math.Min(20, count * 5);
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.RepeatedTemporalBucket, penalty,
                    $"Temporal bucket {bucket + 1} already has {count} selected photo(s).");
                score += penalty;
            }
        }

        if (metadata.PeopleCombinationKey is string peopleKey)
        {
            int count = peopleCombinationCounts.GetValueOrDefault(peopleKey);
            if (count == 0)
            {
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.NewPeopleCombination, 45,
                    $"Adds identified-person combination '{peopleKey}'.");
                score += 45;
            }
            else
            {
                int penalty = -20 * count;
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.RepeatedPeopleCombination, penalty,
                    $"Identified-person combination '{peopleKey}' already has {count} selected photo(s).");
                score += penalty;
            }
        }

        if (semanticDiversityEnabled && metadata.SemanticConcepts.Count > 0)
        {
            string[] unseen = metadata.SemanticConcepts
                .Where(concept => semanticConceptCounts.GetValueOrDefault(concept) == 0)
                .Take(2)
                .ToArray();
            if (unseen.Length > 0)
            {
                int bonus = unseen.Length * 25;
                AddReason(
                    reasons,
                    CreativeCollectionSelectionReasonCodes.SemanticNewConcept,
                    bonus,
                    $"Adds visible-content concept coverage: {string.Join(", ", unseen)}.");
                score += bonus;
            }
        }

        if (embeddingDiversityEnabled &&
            metadata.ImageEmbedding is not null &&
            selectedEmbeddings.Count > 0)
        {
            double maximumSimilarity = selectedEmbeddings
                .Max(selectedEmbedding =>
                    PhotoEmbeddingSimilarity.Cosine(
                        metadata.ImageEmbedding,
                        selectedEmbedding));
            int penalty = maximumSimilarity switch
            {
                >= 0.97d => -160,
                >= 0.92d => -90,
                >= 0.85d => -40,
                >= 0.75d => -15,
                _ => 0,
            };
            if (penalty != 0)
            {
                AddReason(
                    reasons,
                    CreativeCollectionSelectionReasonCodes.EmbeddingSimilarity,
                    penalty,
                    $"Most similar already-selected whole-image embedding has cosine similarity {maximumSimilarity:0.000}.");
                score += penalty;
            }
        }

        if (metadata.VisualGroupId is string visualGroupId)
        {
            int count = visualGroupCounts.GetValueOrDefault(visualGroupId);
            if (count > 0)
            {
                int penalty = -180 * count;
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.VisualRedundancy, penalty,
                    $"Visual redundancy group '{visualGroupId}' already has {count} selected representative photo(s).");
                score += penalty;
            }
        }

        if (metadata.TakenAtLocal is DateTime takenAtLocal)
        {
            int nearCount = selected.Count(existing =>
                existing.Candidate.TakenAtLocal is DateTime selectedTime &&
                Math.Abs((selectedTime - takenAtLocal).Ticks) <= policy.NearConsecutiveWindow.Ticks);
            if (nearCount > 0)
            {
                int penalty = -55 * nearCount;
                AddReason(reasons, CreativeCollectionSelectionReasonCodes.NearConsecutive, penalty,
                    $"Within {policy.NearConsecutiveWindow.TotalMinutes:0.#} minutes of {nearCount} already selected photo(s).");
                score += penalty;
            }
        }

        return new ScoredCandidate(candidate, metadata, score, reasons);
    }

    private static IReadOnlyList<string> NormalizeSemanticConcepts(
        IReadOnlyList<string>? concepts) =>
        concepts is null
            ? []
            : concepts
                .Where(concept => !string.IsNullOrWhiteSpace(concept))
                .Select(concept => concept.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(concept => concept, StringComparer.Ordinal)
                .ToArray();

    private static IReadOnlyList<float>? NormalizeEmbedding(
        IReadOnlyList<float>? embedding)
    {
        if (embedding is null)
        {
            return null;
        }
        if (embedding.Count == 0 || embedding.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException(
                "Whole-image embedding evidence must contain finite values.",
                nameof(embedding));
        }

        double squaredNorm = embedding.Sum(value => (double)value * value);
        if (!double.IsFinite(squaredNorm) || squaredNorm <= 0)
        {
            throw new ArgumentException(
                "Whole-image embedding evidence must have a positive finite norm.",
                nameof(embedding));
        }

        double norm = Math.Sqrt(squaredNorm);
        return embedding
            .Select(value => (float)(value / norm))
            .ToArray();
    }

    private static Dictionary<AssetRevisionId, string> BuildVisualGroupMap(
        PhotoVisualRedundancyResult? visualRedundancy)
    {
        Dictionary<AssetRevisionId, string> result = [];
        if (visualRedundancy is null)
        {
            return result;
        }

        foreach (PhotoVisualRedundancyGroup group in visualRedundancy.Groups)
        {
            foreach (PhotoVisualRedundancyMember member in group.Members)
            {
                if (!result.TryAdd(member.RevisionId, group.Id))
                {
                    throw new ArgumentException(
                        $"Visual redundancy evidence assigns revision '{member.RevisionId}' to multiple groups.",
                        nameof(visualRedundancy));
                }
            }
        }

        return result;
    }

    private static void AddReason(
        ICollection<CreativeCollectionSelectionReason> reasons,
        string code,
        int scoreDelta,
        string detail) =>
        reasons.Add(new CreativeCollectionSelectionReason(code, scoreDelta, detail));

    private static void Increment(IDictionary<string, int> counts, string? key)
    {
        if (key is null)
        {
            return;
        }

        counts[key] = counts.TryGetValue(key, out int current)
            ? current + 1
            : 1;
    }

    private sealed class SelectionMetadata
    {
        public SelectionMetadata(
            DateTime? takenAtLocal,
            string? momentId,
            string? peopleCombinationKey,
            string? visualGroupId,
            string? presentationPreference,
            PhotoSlideshowExposureSummary? exposure,
            IReadOnlyList<string> semanticConcepts,
            IReadOnlyList<float>? imageEmbedding)
        {
            TakenAtLocal = takenAtLocal;
            MomentId = momentId;
            PeopleCombinationKey = peopleCombinationKey;
            VisualGroupId = visualGroupId;
            PresentationPreference = presentationPreference;
            Exposure = exposure;
            SemanticConcepts = semanticConcepts;
            ImageEmbedding = imageEmbedding;
        }

        public DateTime? TakenAtLocal { get; }
        public string? MomentId { get; }
        public string? PeopleCombinationKey { get; }
        public string? VisualGroupId { get; }
        public string? PresentationPreference { get; }
        public PhotoSlideshowExposureSummary? Exposure { get; }
        public IReadOnlyList<string> SemanticConcepts { get; }
        public IReadOnlyList<float>? ImageEmbedding { get; }
        public int? TemporalBucket { get; set; }
    }

    private sealed record ScoredCandidate(
        CreativeCollectionCandidate Candidate,
        SelectionMetadata Metadata,
        int Score,
        IReadOnlyList<CreativeCollectionSelectionReason> Reasons);
}
