using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class CreativeCollectionSelectionReasonTests
{
    [Fact]
    public void Selection_reasons_account_for_repeated_temporal_bucket_penalty()
    {
        PhotoMomentCandidate[] catalogue =
        [
            Candidate(1, new DateTime(2026, 7, 1, 10, 0, 0)),
            Candidate(2, new DateTime(2026, 7, 1, 10, 1, 0)),
            Candidate(3, new DateTime(2026, 7, 1, 20, 0, 0)),
        ];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            catalogue,
            PhotoMomentGapPolicy.Evaluation30Minutes);
        CreativeCollectionCandidateSet generated = CreativeCollectionCandidateGenerator.Generate(
            catalogue,
            catalogue.Select(candidate => candidate.RevisionId),
            moments,
            CreativeCollectionContextPolicy.BalancedV1);

        CreativeCollectionSelectionResult result = CreativeCollectionSelector.Select(
            generated,
            catalogue,
            moments,
            targetCount: 3,
            CreativeCollectionSelectionPolicy.BalancedV1);

        CreativeCollectionSelectedCandidate repeatedBucket = result.Selected.Single(item =>
            item.Candidate.RevisionId == Revision(2));
        Assert.Contains(repeatedBucket.Reasons, reason =>
            reason.Code == CreativeCollectionSelectionReasonCodes.RepeatedTemporalBucket &&
            reason.ScoreDelta < 0);
        Assert.Equal(
            100 + repeatedBucket.Reasons.Sum(reason => reason.ScoreDelta),
            repeatedBucket.SelectionScore);
    }

    private static PhotoMomentCandidate Candidate(int suffix, DateTime takenAtLocal) => new(
        Revision(suffix),
        takenAtLocal);

    private static AssetRevisionId Revision(int suffix) => AssetRevisionId.From(
        Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"));
}
