using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoVisualRedundancyGrouperTests
{
    [Fact]
    public void Threshold_is_inclusive_and_complete_link_prevents_similarity_chaining()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, 0),
            Candidate(2, 5),
            Candidate(3, 10),
        ];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);

        PhotoVisualFingerprint[] fingerprints =
        [
            Fingerprint(1, 0, 0b00000),
            Fingerprint(2, 5, 0b01111),
            Fingerprint(3, 10, 0b11111),
        ];

        PhotoVisualRedundancyResult result = PhotoVisualRedundancyGrouper.Group(
            fingerprints,
            moments,
            PhotoVisualRedundancyPolicy.StrictEvaluationV1);

        PhotoVisualRedundancyGroup group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Members.Count);
        Assert.Contains(group.Members, member => member.RevisionId == Revision(1));
        Assert.Contains(group.Members, member => member.RevisionId == Revision(2));
        Assert.DoesNotContain(group.Members, member => member.RevisionId == Revision(3));
        Assert.Equal(4, group.MaximumPairDistance);
        Assert.Equal(1, result.SuppressiblePhotoCount);
    }

    [Fact]
    public void Identical_hashes_in_different_moments_never_form_one_visual_group()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, 0),
            new(
                Revision(2),
                new DateTime(2026, 1, 1, 11, 0, 0)),
        ];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);

        PhotoVisualFingerprint[] fingerprints =
        [
            Fingerprint(1, 0, 0),
            new(
                Revision(2),
                new DateTime(2026, 1, 1, 11, 0, 0),
                new PhotoPerceptualHash64(0)),
        ];

        PhotoVisualRedundancyResult result = PhotoVisualRedundancyGrouper.Group(
            fingerprints,
            moments,
            PhotoVisualRedundancyPolicy.BroadEvaluationV1);

        Assert.Empty(result.Groups);
        Assert.Equal(2, result.FingerprintCount);
    }

    [Fact]
    public void Capture_span_boundary_is_inclusive_but_longer_runs_are_split()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, 0),
            Candidate(2, 20),
            Candidate(3, 21),
        ];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);
        PhotoVisualFingerprint[] fingerprints =
        [
            Fingerprint(1, 0, 0),
            Fingerprint(2, 20, 0),
            Fingerprint(3, 21, 0),
        ];

        PhotoVisualRedundancyResult result = PhotoVisualRedundancyGrouper.Group(
            fingerprints,
            moments,
            PhotoVisualRedundancyPolicy.StrictEvaluationV1);

        PhotoVisualRedundancyGroup group = Assert.Single(result.Groups);
        Assert.Equal(
            [Revision(1), Revision(2)],
            group.Members.Select(member => member.RevisionId).ToArray());
    }

    [Fact]
    public void Same_evidence_is_deterministic_under_input_reordering()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, 0),
            Candidate(2, 3),
            Candidate(3, 6),
            Candidate(4, 60),
        ];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);
        PhotoVisualFingerprint[] fingerprints =
        [
            Fingerprint(1, 0, 0),
            Fingerprint(2, 3, 1),
            Fingerprint(3, 6, 3),
            Fingerprint(4, 60, ulong.MaxValue),
        ];

        PhotoVisualRedundancyResult forward = PhotoVisualRedundancyGrouper.Group(
            fingerprints,
            moments,
            PhotoVisualRedundancyPolicy.BalancedEvaluationV1);
        PhotoVisualRedundancyResult reversed = PhotoVisualRedundancyGrouper.Group(
            fingerprints.Reverse(),
            moments,
            PhotoVisualRedundancyPolicy.BalancedEvaluationV1);

        Assert.Equal(
            forward.Groups.Select(group => (
                group.Id,
                group.RepresentativeRevisionId,
                Members: string.Join(",", group.Members.Select(member => member.RevisionId)))),
            reversed.Groups.Select(group => (
                group.Id,
                group.RepresentativeRevisionId,
                Members: string.Join(",", group.Members.Select(member => member.RevisionId)))));
    }

    [Fact]
    public void Duplicate_revision_fingerprints_are_rejected()
    {
        PhotoMomentCandidate candidate = Candidate(1, 0);
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            [candidate],
            PhotoMomentGapPolicy.Evaluation30Minutes);

        Assert.Throws<ArgumentException>(() => PhotoVisualRedundancyGrouper.Group(
            [
                Fingerprint(1, 0, 0),
                Fingerprint(1, 0, 1),
            ],
            moments,
            PhotoVisualRedundancyPolicy.StrictEvaluationV1));
    }

    private static PhotoMomentCandidate Candidate(int suffix, int seconds) => new(
        Revision(suffix),
        new DateTime(2026, 1, 1, 10, 0, 0).AddSeconds(seconds));

    private static PhotoVisualFingerprint Fingerprint(
        int suffix,
        int seconds,
        ulong hash) => new(
        Revision(suffix),
        new DateTime(2026, 1, 1, 10, 0, 0).AddSeconds(seconds),
        new PhotoPerceptualHash64(hash));

    private static AssetRevisionId Revision(int suffix) => AssetRevisionId.From(
        Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"));
}
