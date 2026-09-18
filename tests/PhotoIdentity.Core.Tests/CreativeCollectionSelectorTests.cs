using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class CreativeCollectionSelectorTests
{
    [Fact]
    public void Target_above_available_uses_every_unique_candidate_without_duplication()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 1, 1, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 1, 2, 10, 0, 0)),
            Candidate(3, new DateTime(2026, 1, 3, 10, 0, 0)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            targetCount: 10,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(3, result.SelectedCount);
        Assert.Equal(3, result.SelectedDirectAnchorCount);
        Assert.Equal(0, result.SelectedContextCount);
        Assert.Equal(3, result.Selected.Select(item => item.Candidate.RevisionId).Distinct().Count());
    }

    [Fact]
    public void Large_candidate_set_is_reduced_and_final_order_is_chronological()
    {
        PhotoMomentCandidate[] catalogue = Enumerable.Range(1, 10)
            .Select(index => Candidate(index, new DateTime(2026, 2, index, 10, 0, 0)))
            .ToArray();
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            targetCount: 4,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(4, result.SelectedCount);
        Assert.Equal(
            result.Selected.Select(item => item.Candidate.TakenAtLocal).OrderBy(value => value),
            result.Selected.Select(item => item.Candidate.TakenAtLocal));
    }

    [Fact]
    public void Temporal_and_moment_coverage_beats_near_consecutive_burst_members()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 3, 1, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 3, 1, 10, 0, 30)),
            Candidate(3, new DateTime(2026, 3, 1, 10, 1, 0)),
            Candidate(4, new DateTime(2026, 6, 1, 10, 0, 0)),
            Candidate(5, new DateTime(2026, 9, 1, 10, 0, 0)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            targetCount: 3,
            CreativeCollectionSelectionPolicy.BalancedV1);

        AssetRevisionId[] selected = result.Selected.Select(item => item.Candidate.RevisionId).ToArray();
        Assert.Contains(Revision(4), selected);
        Assert.Contains(Revision(5), selected);
        Assert.True(selected.Count(id => id is var value &&
            (value == Revision(1) || value == Revision(2) || value == Revision(3))) <= 1);
    }

    [Fact]
    public void Repeated_people_combination_loses_to_distinct_people_when_other_evidence_is_equal()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 4, 1, 10, 0, 0), ["person-a"]),
            Candidate(2, new DateTime(2026, 4, 1, 12, 0, 0), ["person-a"]),
            Candidate(3, new DateTime(2026, 4, 1, 14, 0, 0), ["person-b"]),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Contains(result.Selected, item => item.PeopleCombinationKey == "person-a");
        Assert.Contains(result.Selected, item => item.PeopleCombinationKey == "person-b");
    }

    [Fact]
    public void Distinct_context_can_survive_repetitive_direct_anchor_material()
    {
        PhotoMomentCandidate firstAnchor = Candidate(
            1,
            new DateTime(2026, 5, 1, 10, 0, 0),
            ["person-a"]);
        PhotoMomentCandidate secondAnchor = Candidate(
            2,
            new DateTime(2026, 5, 1, 10, 0, 30),
            ["person-a"]);
        PhotoMomentCandidate context = Candidate(
            3,
            new DateTime(2026, 5, 1, 10, 1, 0),
            ["person-b"]);
        PhotoMomentCandidate[] catalogue = [firstAnchor, secondAnchor, context];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);
        CreativeCollectionCandidateSet generated = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            [firstAnchor.RevisionId, secondAnchor.RevisionId],
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(1, result.SelectedDirectAnchorCount);
        Assert.Equal(1, result.SelectedContextCount);
        Assert.Contains(result.Selected, item => item.Candidate.RevisionId == context.RevisionId);
    }

    [Fact]
    public void Visual_redundancy_prefers_one_representative_before_a_distinct_frame()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 5, 2, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 5, 2, 10, 0, 5)),
            Candidate(3, new DateTime(2026, 5, 2, 10, 0, 10)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        PhotoVisualRedundancyResult redundancy = PhotoVisualRedundancyGrouper.Group(
            [
                new PhotoVisualFingerprint(Revision(1), catalogue[0].TakenAtLocal, new PhotoPerceptualHash64(0)),
                new PhotoVisualFingerprint(Revision(2), catalogue[1].TakenAtLocal, new PhotoPerceptualHash64(1)),
                new PhotoVisualFingerprint(Revision(3), catalogue[2].TakenAtLocal, new PhotoPerceptualHash64(ulong.MaxValue)),
            ],
            moments,
            PhotoVisualRedundancyPolicy.BalancedEvaluationV1);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            redundancy,
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal([Revision(1), Revision(3)],
            result.Selected.Select(item => item.Candidate.RevisionId).ToArray());
    }

    [Fact]
    public void Visual_redundancy_is_a_penalty_not_a_hard_exclusion()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 5, 3, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 5, 3, 10, 0, 5)),
            Candidate(3, new DateTime(2026, 5, 3, 10, 0, 10)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        PhotoVisualRedundancyResult redundancy = PhotoVisualRedundancyGrouper.Group(
            [
                new PhotoVisualFingerprint(Revision(1), catalogue[0].TakenAtLocal, new PhotoPerceptualHash64(0)),
                new PhotoVisualFingerprint(Revision(2), catalogue[1].TakenAtLocal, new PhotoPerceptualHash64(1)),
                new PhotoVisualFingerprint(Revision(3), catalogue[2].TakenAtLocal, new PhotoPerceptualHash64(ulong.MaxValue)),
            ],
            moments,
            PhotoVisualRedundancyPolicy.BalancedEvaluationV1);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            redundancy,
            targetCount: 3,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(3, result.SelectedCount);
        CreativeCollectionSelectedCandidate repeated = result.Selected.Single(item =>
            item.Candidate.RevisionId == Revision(2));
        Assert.Contains(repeated.Reasons, reason =>
            reason.Code == CreativeCollectionSelectionReasonCodes.VisualRedundancy &&
            reason.ScoreDelta < 0);
    }

    [Fact]
    public void Prefer_increases_priority_for_an_otherwise_eligible_photo()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 5, 4, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 5, 4, 12, 0, 0)),
            Candidate(3, new DateTime(2026, 5, 4, 14, 0, 0)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);

        CreativeCollectionSelectionResult automatic = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: null,
            targetCount: 1,
            CreativeCollectionSelectionPolicy.BalancedV1);
        CreativeCollectionSelectionResult preferred = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            new Dictionary<AssetRevisionId, string>
            {
                [Revision(3)] = PhotoPresentationPreferenceKinds.Prefer,
            },
            targetCount: 1,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(Revision(1), Assert.Single(automatic.Selected).Candidate.RevisionId);
        CreativeCollectionSelectedCandidate selected = Assert.Single(preferred.Selected);
        Assert.Equal(Revision(3), selected.Candidate.RevisionId);
        Assert.Contains(selected.Reasons, reason =>
            reason.Code == CreativeCollectionSelectionReasonCodes.PresentationPrefer &&
            reason.ScoreDelta > 0);
    }

    [Fact]
    public void Avoid_is_a_hard_presentation_exclusion_even_for_direct_anchors()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 5, 5, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 5, 5, 12, 0, 0)),
            Candidate(3, new DateTime(2026, 5, 5, 14, 0, 0)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            new Dictionary<AssetRevisionId, string>
            {
                [Revision(2)] = PhotoPresentationPreferenceKinds.Avoid,
            },
            targetCount: 10,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(2, result.AvailableCandidateCount);
        Assert.Equal(2, result.SelectedCount);
        Assert.DoesNotContain(result.Selected, item => item.Candidate.RevisionId == Revision(2));
        Assert.Contains(result.Selected, item => item.Candidate.RevisionId == Revision(1));
        Assert.Contains(result.Selected, item => item.Candidate.RevisionId == Revision(3));
    }

    [Fact]
    public void Novelty_disabled_preserves_existing_selection_even_when_history_exists()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 7, 1, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 8, 1, 10, 0, 0)),
            Candidate(3, new DateTime(2026, 9, 1, 10, 0, 0)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        Dictionary<AssetRevisionId, PhotoSlideshowExposureSummary> history = new()
        {
            [Revision(1)] = new(Revision(1), 7, new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero)),
        };

        CreativeCollectionSelectionResult baseline = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: null,
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);
        CreativeCollectionSelectionResult disabled = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: null,
            history,
            noveltyEnabled: false,
            noveltyEvaluatedAtUtc: new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(
            baseline.Selected.Select(item => item.Candidate.RevisionId),
            disabled.Selected.Select(item => item.Candidate.RevisionId));
    }

    [Fact]
    public void Novelty_enabled_prefers_unseen_photo_over_recently_repeated_photo()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 7, 1, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 8, 1, 10, 0, 0)),
            Candidate(3, new DateTime(2026, 9, 1, 10, 0, 0)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        Dictionary<AssetRevisionId, PhotoSlideshowExposureSummary> history = new()
        {
            [Revision(1)] = new(Revision(1), 4, new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero)),
        };

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: null,
            history,
            noveltyEnabled: true,
            noveltyEvaluatedAtUtc: new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
            targetCount: 1,
            CreativeCollectionSelectionPolicy.BalancedV1);

        CreativeCollectionSelectedCandidate selected = Assert.Single(result.Selected);
        Assert.NotEqual(Revision(1), selected.Candidate.RevisionId);
        Assert.Contains(
            selected.Reasons,
            reason => reason.Code == CreativeCollectionSelectionReasonCodes.NoveltyUnseen);
    }

    [Fact]
    public void Explicit_prefer_remains_stronger_than_novelty()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 7, 1, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 8, 1, 10, 0, 0)),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        Dictionary<AssetRevisionId, string> preferences = new()
        {
            [Revision(1)] = PhotoPresentationPreferenceKinds.Prefer,
        };
        Dictionary<AssetRevisionId, PhotoSlideshowExposureSummary> history = new()
        {
            [Revision(1)] = new(Revision(1), 1, new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero)),
        };

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            preferences,
            history,
            noveltyEnabled: true,
            noveltyEvaluatedAtUtc: new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
            targetCount: 1,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(Revision(1), Assert.Single(result.Selected).Candidate.RevisionId);
    }

    [Fact]
    public void Semantic_diversity_disabled_preserves_existing_selection()
    {
        DateTime taken = new(2026, 9, 1, 10, 0, 0);
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, taken),
            Candidate(2, taken),
            Candidate(3, taken),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        Dictionary<AssetRevisionId, IReadOnlyList<string>> concepts = new()
        {
            [Revision(1)] = ["indoors"],
            [Revision(2)] = ["indoors"],
            [Revision(3)] = ["outdoors"],
        };

        CreativeCollectionSelectionResult baseline = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);
        CreativeCollectionSelectionResult disabled = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: null,
            exposureHistory: null,
            noveltyEnabled: false,
            noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
            semanticConcepts: concepts,
            semanticDiversityEnabled: false,
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(
            baseline.Selected.Select(item => item.Candidate.RevisionId),
            disabled.Selected.Select(item => item.Candidate.RevisionId));
        Assert.Equal(CreativeCollectionSelectionPolicy.BalancedV1.Version, disabled.PolicyVersion);
    }

    [Fact]
    public void Semantic_diversity_rewards_new_visible_content_without_overriding_prefer()
    {
        DateTime taken = new(2026, 9, 1, 10, 0, 0);
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, taken),
            Candidate(2, taken),
            Candidate(3, taken),
        ];
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        Dictionary<AssetRevisionId, IReadOnlyList<string>> concepts = new()
        {
            [Revision(1)] = ["indoors"],
            [Revision(2)] = ["indoors"],
            [Revision(3)] = ["outdoors"],
        };

        CreativeCollectionSelectionResult semantic = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: null,
            exposureHistory: null,
            noveltyEnabled: false,
            noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
            semanticConcepts: concepts,
            semanticDiversityEnabled: true,
            targetCount: 2,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal([Revision(1), Revision(3)],
            semantic.Selected.Select(item => item.Candidate.RevisionId).ToArray());
        CreativeCollectionSelectedCandidate distinct = semantic.Selected.Single(item =>
            item.Candidate.RevisionId == Revision(3));
        Assert.Contains(distinct.Reasons, reason =>
            reason.Code == CreativeCollectionSelectionReasonCodes.SemanticNewConcept &&
            reason.ScoreDelta > 0);
        Assert.EndsWith(
            CreativeCollectionSemanticDiversityPolicies.BalancedV1,
            semantic.PolicyVersion,
            StringComparison.Ordinal);

        Dictionary<AssetRevisionId, string> preferences = new()
        {
            [Revision(2)] = PhotoPresentationPreferenceKinds.Prefer,
        };
        CreativeCollectionSelectionResult preferred = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            visualRedundancy: null,
            presentationPreferences: preferences,
            exposureHistory: null,
            noveltyEnabled: false,
            noveltyEvaluatedAtUtc: DateTimeOffset.UnixEpoch,
            semanticConcepts: concepts,
            semanticDiversityEnabled: true,
            targetCount: 1,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(Revision(2), Assert.Single(preferred.Selected).Candidate.RevisionId);
    }

    [Fact]
    public void Same_inputs_target_and_policy_are_deterministic_under_candidate_reordering()
    {
        PhotoMomentCandidate[] catalogue = Enumerable.Range(1, 8)
            .Select(index => Candidate(
                index,
                new DateTime(2026, 6, index, 10, 0, 0),
                [index % 2 == 0 ? "person-a" : "person-b"]))
            .ToArray();
        (CreativeCollectionCandidateSet generated, PhotoMomentClusteringResult moments) =
            GenerateAllAnchors(catalogue);
        CreativeCollectionCandidateSet reversed = generated with
        {
            Candidates = generated.Candidates.Reverse().ToArray(),
        };

        CreativeCollectionSelectionResult forward = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            4,
            CreativeCollectionSelectionPolicy.BalancedV1);
        CreativeCollectionSelectionResult backward = CreativeCollectionSelector.Select(
            reversed,
            catalogue.Reverse(),
            moments,
            4,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Equal(
            forward.Selected.Select(item => item.Candidate.RevisionId),
            backward.Selected.Select(item => item.Candidate.RevisionId));
    }

    [Fact]
    public void Zero_candidates_remains_empty_and_target_is_validated()
    {
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            [],
            PhotoMomentGapPolicy.Evaluation30Minutes);
        CreativeCollectionCandidateSet candidates = new(
            moments.PolicyVersion,
            CreativeCollectionContextPolicy.BalancedV1.Version,
            0,
            0,
            []);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            candidates,
            [],
            moments,
            25,
            CreativeCollectionSelectionPolicy.BalancedV1);

        Assert.Empty(result.Selected);
        Assert.Throws<ArgumentOutOfRangeException>(() => CreativeCollectionSelector.Select(
            candidates,
            [],
            moments,
            0,
            CreativeCollectionSelectionPolicy.BalancedV1));
    }

    private static (CreativeCollectionCandidateSet Generated, PhotoMomentClusteringResult Moments)
        GenerateAllAnchors(PhotoMomentCandidate[] catalogue)
    {
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);
        CreativeCollectionCandidateSet generated = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            catalogue.Select(candidate => candidate.RevisionId),
            moments,
            CreativeCollectionContextPolicy.BalancedV1);
        return (generated, moments);
    }

    private static PhotoMomentCandidate Candidate(
        int suffix,
        DateTime? takenAtLocal,
        IReadOnlyCollection<string>? peopleKeys = null) => new(
            Revision(suffix),
            takenAtLocal,
            peopleKeys);

    private static AssetRevisionId Revision(int suffix) => AssetRevisionId.From(
        Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"));
}
