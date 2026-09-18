using System.Numerics;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

/// <summary>
/// Versioned 64-bit perceptual fingerprint derived from a rendered review proxy.
/// This is presentation evidence only; it is not source identity or duplicate truth.
/// </summary>
public readonly record struct PhotoPerceptualHash64(ulong Value)
{
    public int HammingDistance(PhotoPerceptualHash64 other) =>
        BitOperations.PopCount(Value ^ other.Value);

    public override string ToString() => Value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record PhotoVisualFingerprint(
    AssetRevisionId RevisionId,
    DateTime? TakenAtLocal,
    PhotoPerceptualHash64 Hash);

/// <summary>
/// Bounded, versioned policy for grouping visually redundant frames inside one inferred moment.
/// Complete-link matching is intentionally conservative: every member in a group must remain within
/// the hash threshold of every other member, and the total capture span must stay bounded.
/// </summary>
public sealed record PhotoVisualRedundancyPolicy
{
    public const string AlgorithmVersion = "opencv-dhash64-9x8-gray-v1";

    public PhotoVisualRedundancyPolicy(
        string version,
        int maximumHammingDistance,
        TimeSpan maximumCaptureSpan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (maximumHammingDistance is < 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumHammingDistance));
        }

        if (maximumCaptureSpan <= TimeSpan.Zero || maximumCaptureSpan > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCaptureSpan));
        }

        Version = version.Trim();
        MaximumHammingDistance = maximumHammingDistance;
        MaximumCaptureSpan = maximumCaptureSpan;
    }

    public string Version { get; }
    public int MaximumHammingDistance { get; }
    public TimeSpan MaximumCaptureSpan { get; }

    public static PhotoVisualRedundancyPolicy StrictEvaluationV1 { get; } =
        new("m26-dhash64-h4-20s-v1", 4, TimeSpan.FromSeconds(20));

    public static PhotoVisualRedundancyPolicy BalancedEvaluationV1 { get; } =
        new("m26-dhash64-h6-20s-v1", 6, TimeSpan.FromSeconds(20));

    public static PhotoVisualRedundancyPolicy BroadEvaluationV1 { get; } =
        new("m26-dhash64-h8-20s-v1", 8, TimeSpan.FromSeconds(20));

    public static IReadOnlyList<PhotoVisualRedundancyPolicy> EvaluationPolicies { get; } =
    [
        StrictEvaluationV1,
        BalancedEvaluationV1,
        BroadEvaluationV1,
    ];
}

public sealed record PhotoVisualRedundancyMember(
    AssetRevisionId RevisionId,
    DateTime TakenAtLocal,
    int DistanceFromRepresentative);

public sealed record PhotoVisualRedundancyGroup(
    string Id,
    string MomentId,
    string PolicyVersion,
    AssetRevisionId RepresentativeRevisionId,
    DateTime StartedAtLocal,
    DateTime EndedAtLocal,
    int MaximumPairDistance,
    IReadOnlyList<PhotoVisualRedundancyMember> Members);

public sealed record PhotoVisualRedundancyResult(
    string PolicyVersion,
    string AlgorithmVersion,
    int MaximumHammingDistance,
    TimeSpan MaximumCaptureSpan,
    int FingerprintCount,
    IReadOnlyList<PhotoVisualRedundancyGroup> Groups)
{
    public int GroupedPhotoCount => Groups.Sum(group => group.Members.Count);
    public int SuppressiblePhotoCount => Groups.Sum(group => Math.Max(0, group.Members.Count - 1));
}

/// <summary>
/// Pure derived visual grouping. It never mutates source assets, exact duplicate identity,
/// Smart Collection membership or inferred moment membership.
/// </summary>
public static class PhotoVisualRedundancyGrouper
{
    public static PhotoVisualRedundancyResult Group(
        IEnumerable<PhotoVisualFingerprint> fingerprints,
        PhotoMomentClusteringResult moments,
        PhotoVisualRedundancyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(policy);

        PhotoVisualFingerprint[] materialized = fingerprints.ToArray();
        AssetRevisionId? duplicate = materialized
            .GroupBy(item => item.RevisionId)
            .FirstOrDefault(group => group.Count() > 1)?
            .Key;
        if (duplicate.HasValue)
        {
            throw new ArgumentException(
                $"Visual fingerprint input contains duplicate revision '{duplicate.Value}'.",
                nameof(fingerprints));
        }

        Dictionary<AssetRevisionId, string> momentByRevision = moments.Moments
            .SelectMany(moment => moment.Members.Select(member => (member.RevisionId, moment.Id)))
            .ToDictionary(pair => pair.RevisionId, pair => pair.Id);

        List<RedundancyGroupBuilder> allGroups = [];
        foreach (IGrouping<string, PhotoVisualFingerprint> momentGroup in materialized
                     .Where(item =>
                         item.TakenAtLocal.HasValue &&
                         momentByRevision.ContainsKey(item.RevisionId))
                     .GroupBy(item => momentByRevision[item.RevisionId], StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            PhotoVisualFingerprint[] ordered = momentGroup
                .OrderBy(item => item.TakenAtLocal!.Value)
                .ThenBy(item => item.RevisionId.ToString(), StringComparer.Ordinal)
                .ToArray();

            List<RedundancyGroupBuilder> momentBuilders = [];
            foreach (PhotoVisualFingerprint candidate in ordered)
            {
                RedundancyGroupBuilder? compatible = momentBuilders.FirstOrDefault(group =>
                    CanJoin(group, candidate, policy));
                if (compatible is null)
                {
                    momentBuilders.Add(new RedundancyGroupBuilder(momentGroup.Key, candidate));
                }
                else
                {
                    compatible.Add(candidate);
                }
            }

            allGroups.AddRange(momentBuilders.Where(group => group.Members.Count > 1));
        }

        PhotoVisualRedundancyGroup[] resultGroups = allGroups
            .OrderBy(group => group.StartedAtLocal)
            .ThenBy(group => group.Representative.RevisionId.ToString(), StringComparer.Ordinal)
            .Select((group, index) => ToResult(group, index + 1, policy))
            .ToArray();

        return new PhotoVisualRedundancyResult(
            policy.Version,
            PhotoVisualRedundancyPolicy.AlgorithmVersion,
            policy.MaximumHammingDistance,
            policy.MaximumCaptureSpan,
            materialized.Length,
            resultGroups);
    }

    private static bool CanJoin(
        RedundancyGroupBuilder group,
        PhotoVisualFingerprint candidate,
        PhotoVisualRedundancyPolicy policy)
    {
        DateTime candidateTime = candidate.TakenAtLocal!.Value;
        if (candidateTime - group.StartedAtLocal > policy.MaximumCaptureSpan)
        {
            return false;
        }

        return group.Members.All(existing =>
            existing.Hash.HammingDistance(candidate.Hash) <= policy.MaximumHammingDistance);
    }

    private static PhotoVisualRedundancyGroup ToResult(
        RedundancyGroupBuilder group,
        int index,
        PhotoVisualRedundancyPolicy policy)
    {
        int maximumPairDistance = 0;
        for (int left = 0; left < group.Members.Count; left++)
        {
            for (int right = left + 1; right < group.Members.Count; right++)
            {
                maximumPairDistance = Math.Max(
                    maximumPairDistance,
                    group.Members[left].Hash.HammingDistance(group.Members[right].Hash));
            }
        }

        PhotoVisualFingerprint representative = group.Representative;
        PhotoVisualRedundancyMember[] members = group.Members
            .Select(member => new PhotoVisualRedundancyMember(
                member.RevisionId,
                DateTime.SpecifyKind(member.TakenAtLocal!.Value, DateTimeKind.Unspecified),
                representative.Hash.HammingDistance(member.Hash)))
            .ToArray();

        return new PhotoVisualRedundancyGroup(
            $"visual-group-{index:D4}",
            group.MomentId,
            policy.Version,
            representative.RevisionId,
            DateTime.SpecifyKind(group.StartedAtLocal, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(group.EndedAtLocal, DateTimeKind.Unspecified),
            maximumPairDistance,
            members);
    }

    private sealed class RedundancyGroupBuilder
    {
        public RedundancyGroupBuilder(string momentId, PhotoVisualFingerprint first)
        {
            MomentId = momentId;
            Members = [first];
        }

        public string MomentId { get; }
        public List<PhotoVisualFingerprint> Members { get; }
        public PhotoVisualFingerprint Representative => Members[0];
        public DateTime StartedAtLocal => Members[0].TakenAtLocal!.Value;
        public DateTime EndedAtLocal => Members[^1].TakenAtLocal!.Value;

        public void Add(PhotoVisualFingerprint candidate) => Members.Add(candidate);
    }
}
