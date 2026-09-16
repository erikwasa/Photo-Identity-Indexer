using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoMomentClustererTests
{
    [Fact]
    public void Gap_exactly_at_threshold_stays_in_same_moment_and_gap_over_threshold_splits()
    {
        PhotoMomentCandidate first = Candidate(1, new DateTime(2026, 1, 2, 10, 0, 0));
        PhotoMomentCandidate boundary = Candidate(2, new DateTime(2026, 1, 2, 10, 30, 0));
        PhotoMomentCandidate split = Candidate(3, new DateTime(2026, 1, 2, 11, 0, 1));

        PhotoMomentClusteringResult result = PhotoMomentClusterer.Cluster(
            [first, boundary, split],
            PhotoMomentGapPolicy.Evaluation30Minutes);

        Assert.Equal(2, result.Moments.Count);
        Assert.Equal([first.RevisionId, boundary.RevisionId], result.Moments[0].Members.Select(member => member.RevisionId));
        Assert.Equal([split.RevisionId], result.Moments[1].Members.Select(member => member.RevisionId));
    }

    [Fact]
    public void Identical_timestamps_use_revision_id_as_stable_tie_break()
    {
        DateTime captured = new(2026, 2, 3, 14, 15, 16);
        PhotoMomentCandidate laterId = Candidate(2, captured);
        PhotoMomentCandidate earlierId = Candidate(1, captured);

        PhotoMomentClusteringResult result = PhotoMomentClusterer.Cluster(
            [laterId, earlierId],
            PhotoMomentGapPolicy.Evaluation30Minutes);

        PhotoMoment moment = Assert.Single(result.Moments);
        Assert.Equal(
            [earlierId.RevisionId, laterId.RevisionId],
            moment.Members.Select(member => member.RevisionId));
    }

    [Fact]
    public void Midnight_does_not_force_a_split_when_capture_gap_is_small()
    {
        PhotoMomentCandidate beforeMidnight = Candidate(1, new DateTime(2026, 4, 5, 23, 55, 0));
        PhotoMomentCandidate afterMidnight = Candidate(2, new DateTime(2026, 4, 6, 0, 5, 0));

        PhotoMomentClusteringResult result = PhotoMomentClusterer.Cluster(
            [beforeMidnight, afterMidnight],
            PhotoMomentGapPolicy.Evaluation30Minutes);

        Assert.Single(result.Moments);
        Assert.Equal(2, result.Moments[0].Members.Count);
    }

    [Fact]
    public void Missing_capture_time_remains_explicitly_unclustered()
    {
        PhotoMomentCandidate timestamped = Candidate(1, new DateTime(2026, 5, 6, 9, 0, 0));
        PhotoMomentCandidate missing = Candidate(2, null);

        PhotoMomentClusteringResult result = PhotoMomentClusterer.Cluster(
            [missing, timestamped],
            PhotoMomentGapPolicy.Evaluation30Minutes);

        Assert.Single(result.Moments);
        UnclusteredPhotoMomentCandidate unclustered = Assert.Single(result.Unclustered);
        Assert.Equal(missing.RevisionId, unclustered.RevisionId);
        Assert.Equal(PhotoMomentClusterer.MissingCaptureTimeReason, unclustered.Reason);
    }

    [Fact]
    public void Reordering_same_catalogue_state_produces_same_membership_order_and_ids()
    {
        PhotoMomentCandidate first = Candidate(1, new DateTime(2026, 6, 7, 8, 0, 0));
        PhotoMomentCandidate second = Candidate(2, new DateTime(2026, 6, 7, 8, 20, 0));
        PhotoMomentCandidate third = Candidate(3, new DateTime(2026, 6, 7, 12, 0, 0));

        PhotoMomentClusteringResult forward = PhotoMomentClusterer.Cluster(
            [first, second, third],
            PhotoMomentGapPolicy.Evaluation30Minutes);
        PhotoMomentClusteringResult shuffled = PhotoMomentClusterer.Cluster(
            [third, first, second],
            PhotoMomentGapPolicy.Evaluation30Minutes);

        Assert.Equal(
            forward.Moments.Select(MomentSignature),
            shuffled.Moments.Select(MomentSignature));
    }

    [Fact]
    public void Time_only_policy_does_not_require_optional_evidence()
    {
        PhotoMomentCandidate first = Candidate(1, new DateTime(2026, 7, 8, 10, 0, 0));
        PhotoMomentCandidate second = Candidate(2, new DateTime(2026, 7, 8, 11, 0, 0));

        PhotoMomentClusteringResult result = PhotoMomentClusterer.Cluster(
            [first, second],
            PhotoMomentGapPolicy.Evaluation90Minutes);

        Assert.Single(result.Moments);
    }

    [Fact]
    public void Explicit_supporting_evidence_can_extend_a_candidate_boundary()
    {
        PhotoMomentGapPolicy policy = new(
            "test-support-v1",
            TimeSpan.FromMinutes(30),
            supportingEvidenceExtension: TimeSpan.FromMinutes(20));
        PhotoMomentCandidate first = Candidate(
            1,
            new DateTime(2026, 8, 9, 10, 0, 0),
            peopleKeys: ["person-a"]);
        PhotoMomentCandidate second = Candidate(
            2,
            new DateTime(2026, 8, 9, 10, 40, 0),
            peopleKeys: ["person-a"]);

        PhotoMomentClusteringResult result = PhotoMomentClusterer.Cluster([first, second], policy);

        Assert.Single(result.Moments);
    }

    [Fact]
    public void Explicit_distant_location_evidence_can_split_a_time_candidate()
    {
        PhotoMomentGapPolicy policy = new(
            "test-location-split-v1",
            TimeSpan.FromMinutes(90),
            distantLocationSplitKilometers: 50);
        PhotoMomentCandidate stockholm = Candidate(
            1,
            new DateTime(2026, 9, 10, 10, 0, 0),
            latitude: 59.3293,
            longitude: 18.0686);
        PhotoMomentCandidate gothenburg = Candidate(
            2,
            new DateTime(2026, 9, 10, 10, 10, 0),
            latitude: 57.7089,
            longitude: 11.9746);

        PhotoMomentClusteringResult result = PhotoMomentClusterer.Cluster([stockholm, gothenburg], policy);

        Assert.Equal(2, result.Moments.Count);
    }

    private static string MomentSignature(PhotoMoment moment) =>
        $"{moment.Id}:{string.Join(',', moment.Members.Select(member => member.RevisionId.ToString()))}";

    private static PhotoMomentCandidate Candidate(
        int suffix,
        DateTime? takenAtLocal,
        IReadOnlyCollection<string>? peopleKeys = null,
        IReadOnlyCollection<string>? tags = null,
        string? sourceGroupKey = null,
        double? latitude = null,
        double? longitude = null) => new(
            Revision(suffix),
            takenAtLocal,
            peopleKeys,
            tags,
            sourceGroupKey,
            latitude,
            longitude);

    private static AssetRevisionId Revision(int suffix) => AssetRevisionId.From(
        Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"));
}
