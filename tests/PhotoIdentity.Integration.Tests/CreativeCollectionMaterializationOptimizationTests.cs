using System.Reflection;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CreativeCollectionMaterializationOptimizationTests
{
    [Fact]
    public void Visual_fingerprint_cache_reuses_exact_proxy_version_and_invalidates_changes()
    {
        CreativeVisualFingerprintCache cache = new();
        AssetRevisionId revision = Revision(1);
        Sha256Digest firstProxy = new(new string('a', 64));
        Sha256Digest changedProxy = new(new string('b', 64));
        PhotoPerceptualHash64 expected = new(0x0123456789abcdefUL);

        cache.Store(
            revision,
            firstProxy,
            PhotoVisualRedundancyPolicy.AlgorithmVersion,
            expected);

        Assert.True(cache.TryGet(
            revision,
            firstProxy,
            PhotoVisualRedundancyPolicy.AlgorithmVersion,
            out PhotoPerceptualHash64 reused));
        Assert.Equal(expected, reused);
        Assert.False(cache.TryGet(
            revision,
            changedProxy,
            PhotoVisualRedundancyPolicy.AlgorithmVersion,
            out _));
        Assert.False(cache.TryGet(
            revision,
            firstProxy,
            PhotoVisualRedundancyPolicy.AlgorithmVersion + "-next",
            out _));
        Assert.False(cache.TryGet(
            Revision(2),
            firstProxy,
            PhotoVisualRedundancyPolicy.AlgorithmVersion,
            out _));
    }

    [Fact]
    public void Temporal_pruning_preserves_visual_groups_for_isolated_candidates()
    {
        CreativeCollectionCandidate[] candidates =
        [
            Candidate(1, 0),
            Candidate(2, 10),
            Candidate(3, 60),
            Candidate(4, 100),
            Candidate(5, 121),
        ];
        PhotoMomentClusteringResult moments = Moments(candidates);
        CreativeCollectionCandidate[] eligible = SelectEligible(candidates, moments);

        Assert.Equal([Revision(1), Revision(2)], eligible.Select(item => item.RevisionId).ToArray());

        PhotoVisualFingerprint[] fullFingerprints = candidates
            .Select(candidate => new PhotoVisualFingerprint(
                candidate.RevisionId,
                candidate.TakenAtLocal,
                new PhotoPerceptualHash64(0)))
            .ToArray();
        HashSet<AssetRevisionId> eligibleIds = eligible.Select(item => item.RevisionId).ToHashSet();
        PhotoVisualFingerprint[] prunedFingerprints = fullFingerprints
            .Where(item => eligibleIds.Contains(item.RevisionId))
            .ToArray();

        PhotoVisualRedundancyResult full = PhotoVisualRedundancyGrouper.Group(
            fullFingerprints,
            moments,
            PhotoVisualRedundancyPolicy.AcceptedCreativeV1);
        PhotoVisualRedundancyResult pruned = PhotoVisualRedundancyGrouper.Group(
            prunedFingerprints,
            moments,
            PhotoVisualRedundancyPolicy.AcceptedCreativeV1);

        Assert.Equal(
            full.Groups.Select(GroupSignature).ToArray(),
            pruned.Groups.Select(GroupSignature).ToArray());
    }

    [Fact]
    public void Thousand_sparse_candidates_require_no_visual_hash_work()
    {
        CreativeCollectionCandidate[] candidates = Enumerable.Range(1, 1000)
            .Select(index => Candidate(index, (index - 1) * 30))
            .ToArray();
        PhotoMomentClusteringResult moments = Moments(candidates);

        CreativeCollectionCandidate[] eligible = SelectEligible(candidates, moments);

        Assert.Empty(eligible);
    }

    private static string GroupSignature(PhotoVisualRedundancyGroup group) =>
        $"{group.MomentId}:{string.Join(',', group.Members.Select(member => member.RevisionId))}";

    private static CreativeCollectionCandidate[] SelectEligible(
        CreativeCollectionCandidate[] candidates,
        PhotoMomentClusteringResult moments)
    {
        MethodInfo method = typeof(CreativeCollectionMaterializationService).GetMethod(
            "SelectVisualFingerprintCandidates",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Visual candidate pruning helper was not found.");
        return (CreativeCollectionCandidate[])(method.Invoke(
            null,
            [candidates, moments, PhotoVisualRedundancyPolicy.AcceptedCreativeV1])
            ?? throw new InvalidOperationException("Visual candidate pruning returned no result."));
    }

    private static PhotoMomentClusteringResult Moments(CreativeCollectionCandidate[] candidates) =>
        PhotoMomentClusterer.Cluster(
            candidates.Select(candidate => new PhotoMomentCandidate(
                candidate.RevisionId,
                candidate.TakenAtLocal)).ToArray(),
            PhotoMomentGapPolicy.Evaluation30Minutes);

    private static CreativeCollectionCandidate Candidate(int suffix, int seconds) => new(
        Revision(suffix),
        new DateTime(2026, 1, 1, 10, 0, 0).AddSeconds(seconds),
        CreativeCollectionCandidateKinds.DirectAnchor,
        []);

    private static AssetRevisionId Revision(int suffix) => AssetRevisionId.From(
        Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"));
}
