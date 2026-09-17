using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class CreativeCollectionCandidateGeneratorTests
{
    [Fact]
    public void Person_anchor_can_admit_same_moment_photo_without_selected_person()
    {
        PhotoMomentCandidate anchor = Candidate(
            1,
            new DateTime(2026, 1, 2, 10, 0, 0),
            peopleKeys: ["selected-person"]);
        PhotoMomentCandidate context = Candidate(
            2,
            new DateTime(2026, 1, 2, 10, 8, 0),
            peopleKeys: []);
        PhotoMomentCandidate later = Candidate(
            3,
            new DateTime(2026, 1, 2, 12, 0, 0),
            peopleKeys: []);
        PhotoMomentCandidate[] catalogue = [anchor, context, later];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);

        CreativeCollectionCandidateSet result = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            [anchor.RevisionId],
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        Assert.Equal(1, result.DirectAnchorCount);
        Assert.Equal(1, result.AddedContextCount);
        Assert.Equal(2, result.TotalCandidateCount);
        Assert.Equal(CreativeCollectionCandidateKinds.DirectAnchor, result.Candidates[0].Kind);
        CreativeCollectionCandidate added = Assert.Single(result.Candidates.Where(candidate =>
            candidate.Kind == CreativeCollectionCandidateKinds.ContextualAddition));
        Assert.Equal(context.RevisionId, added.RevisionId);
        CreativeCollectionContextReason reason = Assert.Single(added.ContextReasons);
        Assert.Equal("moment-0001", reason.MomentId);
        Assert.Equal([anchor.RevisionId], reason.AnchorRevisionIds);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.RevisionId == later.RevisionId);
    }

    [Fact]
    public void Multiple_anchors_deduplicate_context_and_preserve_all_admitting_anchors()
    {
        PhotoMomentCandidate firstAnchor = Candidate(1, new DateTime(2026, 2, 3, 9, 0, 0));
        PhotoMomentCandidate context = Candidate(2, new DateTime(2026, 2, 3, 9, 5, 0));
        PhotoMomentCandidate secondAnchor = Candidate(3, new DateTime(2026, 2, 3, 9, 10, 0));
        PhotoMomentCandidate[] catalogue = [firstAnchor, context, secondAnchor];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);

        CreativeCollectionCandidateSet result = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            [secondAnchor.RevisionId, firstAnchor.RevisionId, firstAnchor.RevisionId],
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        Assert.Equal(2, result.DirectAnchorCount);
        Assert.Equal(1, result.AddedContextCount);
        Assert.Equal(3, result.Candidates.Select(candidate => candidate.RevisionId).Distinct().Count());
        CreativeCollectionCandidate added = Assert.Single(result.Candidates.Where(candidate =>
            candidate.Kind == CreativeCollectionCandidateKinds.ContextualAddition));
        CreativeCollectionContextReason reason = Assert.Single(added.ContextReasons);
        Assert.Equal(
            new[] { firstAnchor.RevisionId, secondAnchor.RevisionId },
            reason.AnchorRevisionIds);
    }

    [Fact]
    public void Context_policy_bounds_each_anchored_moment_and_does_not_recurse_into_later_moment()
    {
        PhotoMomentCandidate anchor = Candidate(1, new DateTime(2026, 3, 4, 10, 0, 0));
        PhotoMomentCandidate[] nearby = Enumerable.Range(2, 6)
            .Select(index => Candidate(index, new DateTime(2026, 3, 4, 10, index, 0)))
            .ToArray();
        PhotoMomentCandidate later = Candidate(20, new DateTime(2026, 3, 4, 12, 0, 0));
        PhotoMomentCandidate laterCompanion = Candidate(21, new DateTime(2026, 3, 4, 12, 5, 0));
        PhotoMomentCandidate[] catalogue = [anchor, .. nearby, later, laterCompanion];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);
        CreativeCollectionContextPolicy policy = new("test-context-v1", 2);

        CreativeCollectionCandidateSet result = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            [anchor.RevisionId],
            moments,
            policy);

        Assert.Equal(1, result.DirectAnchorCount);
        Assert.Equal(2, result.AddedContextCount);
        Assert.Equal(3, result.TotalCandidateCount);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.RevisionId == later.RevisionId);
        Assert.DoesNotContain(result.Candidates, candidate => candidate.RevisionId == laterCompanion.RevisionId);
    }

    [Fact]
    public void Zero_anchors_is_explicit_and_never_broadens_to_context()
    {
        PhotoMomentCandidate photo = Candidate(1, new DateTime(2026, 4, 5, 10, 0, 0));
        PhotoMomentCandidate[] catalogue = [photo];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);

        CreativeCollectionCandidateSet result = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            [],
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        Assert.True(result.NoAnchors);
        Assert.Equal(0, result.DirectAnchorCount);
        Assert.Equal(0, result.AddedContextCount);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Missing_timestamp_anchor_is_preserved_without_inventing_context()
    {
        PhotoMomentCandidate timestamped = Candidate(1, new DateTime(2026, 5, 6, 10, 0, 0));
        PhotoMomentCandidate missing = Candidate(2, null);
        PhotoMomentCandidate[] catalogue = [timestamped, missing];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);

        CreativeCollectionCandidateSet result = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            [missing.RevisionId],
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        CreativeCollectionCandidate candidate = Assert.Single(result.Candidates);
        Assert.Equal(missing.RevisionId, candidate.RevisionId);
        Assert.Equal(CreativeCollectionCandidateKinds.DirectAnchor, candidate.Kind);
        Assert.Empty(candidate.ContextReasons);
    }

    [Fact]
    public void Same_catalogue_anchors_and_policies_are_deterministic_under_input_reordering()
    {
        PhotoMomentCandidate first = Candidate(1, new DateTime(2026, 6, 7, 10, 0, 0));
        PhotoMomentCandidate second = Candidate(2, new DateTime(2026, 6, 7, 10, 5, 0));
        PhotoMomentCandidate third = Candidate(3, new DateTime(2026, 6, 7, 10, 10, 0));
        PhotoMomentCandidate[] catalogue = [first, second, third];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);

        CreativeCollectionCandidateSet forward = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            [third.RevisionId, first.RevisionId],
            moments,
            CreativeCollectionContextPolicy.BalancedV1);
        CreativeCollectionCandidateSet reversed = CreativeCollectionCandidateGenerator.Generate(
            catalogue.Reverse(),
            [first.RevisionId, third.RevisionId],
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        Assert.Equal(Signatures(forward), Signatures(reversed));
    }

    private static string[] Signatures(CreativeCollectionCandidateSet set) => set.Candidates
        .Select(candidate =>
            $"{candidate.RevisionId}:{candidate.Kind}:{string.Join("|", candidate.ContextReasons.Select(reason => $"{reason.MomentId}:{string.Join(",", reason.AnchorRevisionIds)}"))}")
        .ToArray();

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
