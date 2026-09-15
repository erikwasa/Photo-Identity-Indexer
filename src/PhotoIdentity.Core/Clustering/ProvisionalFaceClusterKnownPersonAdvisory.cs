using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Core.Clustering;

public static class ProvisionalFaceClusterKnownPersonAdvisoryStatuses
{
    public const string Strong = "strong";
    public const string Ambiguous = "ambiguous";
    public const string Insufficient = "insufficient";
}

public sealed record ProvisionalFaceClusterKnownPersonMemberEvidence(
    FaceOccurrenceId FaceOccurrenceId,
    string EvidenceGroup,
    PersonId SuggestedPersonId,
    string SuggestedPersonDisplayName,
    double Score,
    double? ScoreMargin);

public sealed record ProvisionalFaceClusterKnownPersonCandidate(
    PersonId PersonId,
    string DisplayName,
    int SupportCount,
    double SupportShare,
    int OrdinaryHighCount,
    int OrdinaryMediumCount,
    double MinimumScore,
    double MedianScore,
    double MaximumScore,
    double? MedianMargin);

public sealed record ProvisionalFaceClusterKnownPersonAdvisory(
    Guid ClusterRunId,
    ModelId ModelId,
    Sha256Digest ModelHash,
    string ClusterPolicyVersion,
    bool IncludeUnknown,
    string DerivedClusterKey,
    string AdvisoryPolicyVersion,
    int IdentitySuggestionPolicyVersion,
    int MemberCount,
    int IndependentMemberCount,
    int CoreCount,
    int InternalConflictCount,
    int RankedEvidenceCount,
    int QualifyingEvidenceCount,
    string Status,
    string Explanation,
    ProvisionalFaceClusterKnownPersonCandidate? Candidate,
    ProvisionalFaceClusterKnownPersonCandidate? CompetingCandidate,
    DateTimeOffset EvaluatedAtUtc)
{
    public double CoreShare => MemberCount == 0 ? 0 : (double)CoreCount / MemberCount;
    public double RankedEvidenceCoverage => IndependentMemberCount == 0 ? 0 : (double)RankedEvidenceCount / IndependentMemberCount;
    public bool CanonicalAssignmentAllowed => false;
}

public sealed record ProvisionalFaceClusterKnownPersonAdvisoryPolicy(
    string Version,
    int MinimumSupportCount,
    double MinimumSupportShare,
    double MinimumCoreShare,
    int MaximumCompetingSupportCount,
    double MaximumCompetingSupportShare)
{
    public static ProvisionalFaceClusterKnownPersonAdvisoryPolicy Initial { get; } = new(
        "m25-cluster-known-person-v1",
        MinimumSupportCount: 3,
        MinimumSupportShare: 0.60,
        MinimumCoreShare: 0.60,
        MaximumCompetingSupportCount: 1,
        MaximumCompetingSupportShare: 0.20);

    public ProvisionalFaceClusterKnownPersonAdvisory Evaluate(
        Guid clusterRunId,
        ModelId modelId,
        Sha256Digest modelHash,
        string clusterPolicyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        int memberCount,
        int independentMemberCount,
        int coreCount,
        int internalConflictCount,
        ReviewIdentitySuggestionPolicy identitySuggestionPolicy,
        IReadOnlyCollection<ProvisionalFaceClusterKnownPersonMemberEvidence> evidence,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterPolicyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(derivedClusterKey);
        ArgumentNullException.ThrowIfNull(identitySuggestionPolicy);
        ArgumentNullException.ThrowIfNull(evidence);
        Validate();
        identitySuggestionPolicy.Validate();
        if (memberCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(memberCount));
        }
        if (independentMemberCount < 1 || independentMemberCount > memberCount)
        {
            throw new ArgumentOutOfRangeException(nameof(independentMemberCount));
        }
        if (coreCount < 0 || coreCount > memberCount)
        {
            throw new ArgumentOutOfRangeException(nameof(coreCount));
        }
        if (internalConflictCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(internalConflictCount));
        }

        ProvisionalFaceClusterKnownPersonMemberEvidence[] ranked = evidence
            .GroupBy(item => item.FaceOccurrenceId)
            .Select(group => group
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.SuggestedPersonId.ToString(), StringComparer.Ordinal)
                .First())
            .ToArray();

        foreach (ProvisionalFaceClusterKnownPersonMemberEvidence item in ranked)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.EvidenceGroup);
            if (!double.IsFinite(item.Score) || item.Score < -1 || item.Score > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(evidence), "Suggestion scores must be finite cosine similarities.");
            }
            if (item.ScoreMargin is double margin && (!double.IsFinite(margin) || margin < 0 || margin > 2))
            {
                throw new ArgumentOutOfRangeException(nameof(evidence), "Suggestion margins must be between 0 and 2.");
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(item.SuggestedPersonDisplayName);
        }

        ProvisionalFaceClusterKnownPersonMemberEvidence[] qualifying = ranked
            .Where(item => item.Score >= identitySuggestionPolicy.MediumScoreThreshold)
            .ToArray();
        ProvisionalFaceClusterKnownPersonMemberEvidence[] independentQualifying =
            DeduplicateEvidenceGroups(qualifying);

        ProvisionalFaceClusterKnownPersonCandidate[] candidates = independentQualifying
            .GroupBy(item => new { item.SuggestedPersonId, item.SuggestedPersonDisplayName })
            .Select(group => BuildCandidate(
                group.Key.SuggestedPersonId,
                group.Key.SuggestedPersonDisplayName,
                group.ToArray(),
                independentMemberCount,
                identitySuggestionPolicy))
            .OrderByDescending(candidate => candidate.SupportCount)
            .ThenByDescending(candidate => candidate.MedianScore)
            .ThenBy(candidate => candidate.PersonId.ToString(), StringComparer.Ordinal)
            .ToArray();

        ProvisionalFaceClusterKnownPersonCandidate? candidate = candidates.FirstOrDefault();
        ProvisionalFaceClusterKnownPersonCandidate? competitor = candidates.Skip(1).FirstOrDefault();
        string status;
        string explanation;

        if (candidate is null)
        {
            status = ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Insufficient;
            explanation = $"No independent exact-content group has rank-1 evidence at or above the ordinary Medium threshold ({identitySuggestionPolicy.MediumScoreThreshold:F2}).";
        }
        else if (candidate.SupportCount < MinimumSupportCount)
        {
            status = ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Insufficient;
            explanation = $"{candidate.DisplayName} has {candidate.SupportCount} independent exact-content qualifying vote(s); {MinimumSupportCount} are required.";
        }
        else if (candidate.SupportShare < MinimumSupportShare)
        {
            status = ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Insufficient;
            explanation = $"{candidate.DisplayName} has {candidate.SupportShare:P0} qualifying support across independent exact-content evidence; at least {MinimumSupportShare:P0} is required.";
        }
        else if (internalConflictCount > 0)
        {
            status = ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Ambiguous;
            explanation = "The current group contains explicit not-same discovery evidence, so cluster-assisted identity confidence fails closed.";
        }
        else if ((double)coreCount / memberCount < MinimumCoreShare)
        {
            status = ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Ambiguous;
            explanation = $"Cluster Core support is {(double)coreCount / memberCount:P0}; at least {MinimumCoreShare:P0} is required for strong advisory evidence.";
        }
        else if (competitor is not null &&
                 (competitor.SupportCount > MaximumCompetingSupportCount || competitor.SupportShare > MaximumCompetingSupportShare))
        {
            status = ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Ambiguous;
            explanation = $"Competing support for {competitor.DisplayName} is too strong ({competitor.SupportCount} independent vote(s), {competitor.SupportShare:P0} of independent evidence).";
        }
        else
        {
            status = ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Strong;
            explanation = $"{candidate.SupportCount} independent exact-content groups ({candidate.SupportShare:P0}) favor {candidate.DisplayName} at or above the ordinary Medium threshold, with no material competing or cluster-conflict signal.";
        }

        return new(
            clusterRunId,
            modelId,
            modelHash,
            clusterPolicyVersion,
            includeUnknown,
            derivedClusterKey,
            Version,
            identitySuggestionPolicy.Version,
            memberCount,
            independentMemberCount,
            coreCount,
            internalConflictCount,
            ranked.Select(item => item.EvidenceGroup).Distinct(StringComparer.Ordinal).Count(),
            independentQualifying.Length,
            status,
            explanation,
            candidate,
            competitor,
            evaluatedAtUtc.ToUniversalTime());
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        if (MinimumSupportCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSupportCount));
        }
        ValidateShare(MinimumSupportShare, nameof(MinimumSupportShare));
        ValidateShare(MinimumCoreShare, nameof(MinimumCoreShare));
        if (MaximumCompetingSupportCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCompetingSupportCount));
        }
        ValidateShare(MaximumCompetingSupportShare, nameof(MaximumCompetingSupportShare));
    }

    private static ProvisionalFaceClusterKnownPersonMemberEvidence[] DeduplicateEvidenceGroups(
        IEnumerable<ProvisionalFaceClusterKnownPersonMemberEvidence> support) =>
        support
            .GroupBy(item => item.EvidenceGroup, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.SuggestedPersonId.ToString(), StringComparer.Ordinal)
                .ThenBy(item => item.FaceOccurrenceId.ToString(), StringComparer.Ordinal)
                .First())
            .ToArray();

    private static ProvisionalFaceClusterKnownPersonCandidate BuildCandidate(
        PersonId personId,
        string displayName,
        IReadOnlyList<ProvisionalFaceClusterKnownPersonMemberEvidence> support,
        int independentMemberCount,
        ReviewIdentitySuggestionPolicy identitySuggestionPolicy)
    {
        double[] scores = support.Select(item => item.Score).OrderBy(value => value).ToArray();
        double[] margins = support
            .Where(item => item.ScoreMargin.HasValue)
            .Select(item => item.ScoreMargin!.Value)
            .OrderBy(value => value)
            .ToArray();
        int highCount = support.Count(item => string.Equals(
            identitySuggestionPolicy.Classify(item.Score, item.ScoreMargin),
            ReviewIdentitySuggestionConfidenceGroups.High,
            StringComparison.Ordinal));
        return new(
            personId,
            displayName,
            support.Count,
            (double)support.Count / independentMemberCount,
            highCount,
            support.Count - highCount,
            scores[0],
            Median(scores),
            scores[^1],
            margins.Length == 0 ? null : Median(margins));
    }

    private static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;

    private static void ValidateShare(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(name, "Share thresholds must be between 0 and 1.");
        }
    }
}

public interface IProvisionalFaceClusterKnownPersonAdvisoryRepository
{
    Task<ProvisionalFaceClusterKnownPersonAdvisory?> GetCurrentAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string clusterPolicyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        CancellationToken cancellationToken = default);
}
