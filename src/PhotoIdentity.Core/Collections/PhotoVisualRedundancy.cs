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
/// Group membership criterion. The looser anchored-neighbor option is for human review only;
/// production slideshow selection continues to require complete-link matches.
/// </summary>
public enum PhotoVisualGroupingRule
{
    CompleteLink,
    AnchoredNeighbor,
}

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
        TimeSpan maximumCaptureSpan,
        PhotoVisualGroupingRule groupingRule = PhotoVisualGroupingRule.CompleteLink,
        int? maximumRepresentativeDistance = null)
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

        if (!Enum.IsDefined(groupingRule) ||
            (groupingRule == PhotoVisualGroupingRule.CompleteLink && maximumRepresentativeDistance is not null) ||
            (groupingRule == PhotoVisualGroupingRule.AnchoredNeighbor &&
                (maximumRepresentativeDistance is null ||
                 maximumRepresentativeDistance < maximumHammingDistance ||
                 maximumRepresentativeDistance > 64)))
        {
            throw new ArgumentException("Invalid grouping rule or representative distance.");
        }

        Version = version.Trim();
        MaximumHammingDistance = maximumHammingDistance;
        MaximumCaptureSpan = maximumCaptureSpan;
        GroupingRule = groupingRule;
        MaximumRepresentativeDistance = maximumRepresentativeDistance;
    }

    public string Version { get; }
    public int MaximumHammingDistance { get; }
    public TimeSpan MaximumCaptureSpan { get; }
    public PhotoVisualGroupingRule GroupingRule { get; }
    public int? MaximumRepresentativeDistance { get; }

    public static PhotoVisualRedundancyPolicy StrictEvaluationV1 { get; } =
        new("m26-dhash64-h4-20s-v1", 4, TimeSpan.FromSeconds(20));

    public static PhotoVisualRedundancyPolicy BalancedEvaluationV1 { get; } =
        new("m26-dhash64-h6-20s-v1", 6, TimeSpan.FromSeconds(20));

    public static PhotoVisualRedundancyPolicy BroadEvaluationV1 { get; } =
        new("m26-dhash64-h8-20s-v1", 8, TimeSpan.FromSeconds(20));

    public static PhotoVisualRedundancyPolicy AcceptedCreativeV1 { get; } =
        BroadEvaluationV1;

    /// <summary>
    /// Intentionally recall-oriented, manual-review-only burst grouping.
    /// A new frame needs ONE nearby member within H12 and must remain within
    /// H18 of the first frame and within 45 seconds. Not eligible for
    /// slideshow selection penalties or automatic omission.
    /// </summary>
    public static PhotoVisualRedundancyPolicy OperatorSimilarityReviewV2 { get; } =
        new("wi0638-review-neighbor-h12-anchor-h18-45s-v2", 12,
            TimeSpan.FromSeconds(45), PhotoVisualGroupingRule.AnchoredNeighbor, 18);

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
    IReadOnlyList<PhotoVisualRedundancyMember> Members,
    int VisualSubgroupCount = 1);

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

        if (policy.GroupingRule == PhotoVisualGroupingRule.AnchoredNeighbor)
        {
            // Looser than complete-link: adjacent photos in a burst can join
            // without requiring every pair to look identical. The fixed first
            // representative and capture-span limits prevent unbounded chains.
            return group.Representative.Hash.HammingDistance(candidate.Hash) <=
                       policy.MaximumRepresentativeDistance!.Value &&
                   group.Members.Any(existing =>
                       existing.Hash.HammingDistance(candidate.Hash) <= policy.MaximumHammingDistance);
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


/// <summary>
/// Human-review-only presentation grouping. The input groups have genuine local
/// dHash evidence under the H12/H18 45-second policy. This groups those nearby
/// matches into bounded burst review sets even when their *cross-group* hashes
/// differ greatly. Being in one review set is NOT evidence of mutual similarity
/// and is never suitable for collection/slideshow suppression.
/// </summary>
public static class PhotoSimilarityBurstReviewGrouper
{
    public const string PolicyVersion = "wi0638-review-nearby-bursts-gap4m-span10m-v3";
    public static readonly TimeSpan MaximumBetweenGroups = TimeSpan.FromMinutes(4);
    public static readonly TimeSpan MaximumReviewSpan = TimeSpan.FromMinutes(10);
    public const int MaximumReviewPhotos = 40;

    public static PhotoVisualRedundancyResult Combine(
        PhotoVisualRedundancyResult detected,
        IReadOnlyList<PhotoVisualFingerprint> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(detected);
        ArgumentNullException.ThrowIfNull(fingerprints);
        Dictionary<AssetRevisionId, PhotoVisualFingerprint> byRevision =
            fingerprints.ToDictionary(item => item.RevisionId);

        List<PhotoVisualRedundancyGroup> combined = [];
        foreach (IGrouping<string, PhotoVisualRedundancyGroup> moment in detected.Groups
                     .GroupBy(group => group.MomentId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            List<PhotoVisualRedundancyGroup> pending = [];
            foreach (PhotoVisualRedundancyGroup group in moment
                         .OrderBy(item => item.StartedAtLocal)
                         .ThenBy(item => item.RepresentativeRevisionId.ToString(), StringComparer.Ordinal)
                         .SelectMany(SplitOversizedGroup))
            {
                if (pending.Count > 0 && !CanAppend(pending, group))
                {
                    combined.Add(CombinePending(pending, byRevision));
                    pending.Clear();
                }

                pending.Add(group);
            }

            if (pending.Count > 0)
                combined.Add(CombinePending(pending, byRevision));
        }

        PhotoVisualRedundancyGroup[] ordered = combined
            .OrderBy(group => group.StartedAtLocal)
            .ThenBy(group => group.RepresentativeRevisionId.ToString(), StringComparer.Ordinal)
            .Select((group, index) => group with { Id = $"visual-group-{index + 1:D4}" })
            .ToArray();

        return new PhotoVisualRedundancyResult(
            PolicyVersion,
            detected.AlgorithmVersion,
            detected.MaximumHammingDistance,
            MaximumReviewSpan,
            detected.FingerprintCount,
            ordered);
    }

    // A single dense 45-second visual group may already exceed the review
    // payload limit. Split that one source group deterministically instead
    // of letting an otherwise bounded review set grow without limit.
    private static IEnumerable<PhotoVisualRedundancyGroup> SplitOversizedGroup(
        PhotoVisualRedundancyGroup group)
    {
        if (group.Members.Count <= MaximumReviewPhotos)
        {
            yield return group;
            yield break;
        }

        for (int offset = 0; offset < group.Members.Count; offset += MaximumReviewPhotos)
        {
            PhotoVisualRedundancyMember[] members = group.Members
                .Skip(offset)
                .Take(MaximumReviewPhotos)
                .OrderBy(member => member.TakenAtLocal)
                .ThenBy(member => member.RevisionId.ToString(), StringComparer.Ordinal)
                .ToArray();
            yield return group with
            {
                RepresentativeRevisionId = members[0].RevisionId,
                StartedAtLocal = members[0].TakenAtLocal,
                EndedAtLocal = members[^1].TakenAtLocal,
                Members = members,
                VisualSubgroupCount = 1
            };
        }
    }

    private static bool CanAppend(
        IReadOnlyList<PhotoVisualRedundancyGroup> pending,
        PhotoVisualRedundancyGroup next)
    {
        PhotoVisualRedundancyGroup first = pending[0];
        DateTime latestEnd = pending.Max(group => group.EndedAtLocal);
        int existingPhotos = pending.Sum(group => group.Members.Count);
        return first.MomentId == next.MomentId &&
               next.StartedAtLocal - latestEnd <= MaximumBetweenGroups &&
               Math.Max(latestEnd.Ticks, next.EndedAtLocal.Ticks) - first.StartedAtLocal.Ticks
                   <= MaximumReviewSpan.Ticks &&
               existingPhotos + next.Members.Count <= MaximumReviewPhotos;
    }

    private static PhotoVisualRedundancyGroup CombinePending(
        IReadOnlyList<PhotoVisualRedundancyGroup> pending,
        IReadOnlyDictionary<AssetRevisionId, PhotoVisualFingerprint> byRevision)
    {
        PhotoVisualRedundancyMember[] originalMembers = pending
            .SelectMany(group => group.Members)
            .OrderBy(member => member.TakenAtLocal)
            .ThenBy(member => member.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();

        if (originalMembers.Select(member => member.RevisionId).Distinct().Count() != originalMembers.Length)
            throw new InvalidDataException("Review source groups must not contain overlapping photo revisions.");

        PhotoVisualFingerprint first = byRevision[originalMembers[0].RevisionId];
        PhotoVisualRedundancyMember[] members = originalMembers
            .Select(member => member with
            {
                DistanceFromRepresentative = first.Hash.HammingDistance(byRevision[member.RevisionId].Hash)
            })
            .ToArray();

        int maximumPairDistance = 0;
        for (int i = 0; i < members.Length; i++)
        {
            for (int j = i + 1; j < members.Length; j++)
            {
                maximumPairDistance = Math.Max(maximumPairDistance,
                    byRevision[members[i].RevisionId].Hash.HammingDistance(
                        byRevision[members[j].RevisionId].Hash));
            }
        }

        return new PhotoVisualRedundancyGroup(
            string.Empty,
            pending[0].MomentId,
            PolicyVersion,
            first.RevisionId,
            members[0].TakenAtLocal,
            members[^1].TakenAtLocal,
            maximumPairDistance,
            members,
            pending.Sum(group => group.VisualSubgroupCount));
    }
}
