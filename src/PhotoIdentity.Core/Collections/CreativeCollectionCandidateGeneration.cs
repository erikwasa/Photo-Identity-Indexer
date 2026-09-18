using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public static class CreativeCollectionCandidateKinds
{
    public const string DirectAnchor = "direct-anchor";
    public const string ContextualAddition = "contextual-addition";
}

/// <summary>
/// Versioned context-expansion policy. Context is selected only from moments that contain at least
/// one direct Smart Collection anchor; context-only photos never become new expansion anchors.
/// </summary>
public sealed record CreativeCollectionContextPolicy
{
    public CreativeCollectionContextPolicy(
        string version,
        int maximumContextPhotosPerAnchoredMoment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (maximumContextPhotosPerAnchoredMoment < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumContextPhotosPerAnchoredMoment));
        }

        Version = version.Trim();
        MaximumContextPhotosPerAnchoredMoment = maximumContextPhotosPerAnchoredMoment;
    }

    public string Version { get; }
    public int MaximumContextPhotosPerAnchoredMoment { get; }

    public static CreativeCollectionContextPolicy FocusedV1 { get; } =
        new("m26-anchor-context-focused-v1", 2);

    public static CreativeCollectionContextPolicy BalancedV1 { get; } =
        new("m26-anchor-context-balanced-v1", 6);

    public static CreativeCollectionContextPolicy BroadV1 { get; } =
        new("m26-anchor-context-broad-v1", 12);

    public static CreativeCollectionContextPolicy FromVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return version.Trim() switch
        {
            "m26-anchor-context-focused-v1" => FocusedV1,
            "m26-anchor-context-balanced-v1" => BalancedV1,
            "m26-anchor-context-broad-v1" => BroadV1,
            _ => throw new ArgumentException(
                $"Creative Collection context policy '{version}' is not supported.",
                nameof(version)),
        };
    }

    public static CreativeCollectionContextPolicy FromStrength(string strength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strength);
        return strength.Trim().ToLowerInvariant() switch
        {
            "focused" => FocusedV1,
            "balanced" => BalancedV1,
            "broad" => BroadV1,
            _ => throw new ArgumentException(
                "Creative Collection context strength must be focused, balanced or broad.",
                nameof(strength)),
        };
    }

    public static string StrengthForVersion(string version)
    {
        CreativeCollectionContextPolicy policy = FromVersion(version);
        return ReferenceEquals(policy, FocusedV1)
            ? "focused"
            : ReferenceEquals(policy, BroadV1)
                ? "broad"
                : "balanced";
    }
}

public sealed record CreativeCollectionContextReason(
    string MomentId,
    IReadOnlyList<AssetRevisionId> AnchorRevisionIds);

public sealed record CreativeCollectionCandidate(
    AssetRevisionId RevisionId,
    DateTime? TakenAtLocal,
    string Kind,
    IReadOnlyList<CreativeCollectionContextReason> ContextReasons);

public sealed record CreativeCollectionCandidateSet(
    string MomentPolicyVersion,
    string ContextPolicyVersion,
    int DirectAnchorCount,
    int AddedContextCount,
    IReadOnlyList<CreativeCollectionCandidate> Candidates)
{
    public int TotalCandidateCount => Candidates.Count;
    public bool NoAnchors => DirectAnchorCount == 0;
}

/// <summary>
/// Pure, read-only Creative Collection expansion. Exact Smart Collection matches are supplied as
/// immutable anchors. Expansion uses already-derived moment membership and cannot recurse through
/// context-only photos.
/// </summary>
public static class CreativeCollectionCandidateGenerator
{
    public static CreativeCollectionCandidateSet Generate(
        IEnumerable<PhotoMomentCandidate> catalogue,
        IEnumerable<AssetRevisionId> directAnchorRevisionIds,
        PhotoMomentClusteringResult moments,
        CreativeCollectionContextPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(directAnchorRevisionIds);
        ArgumentNullException.ThrowIfNull(moments);
        ArgumentNullException.ThrowIfNull(policy);

        Dictionary<AssetRevisionId, PhotoMomentCandidate> catalogueByRevision = [];
        foreach (PhotoMomentCandidate candidate in catalogue)
        {
            if (!catalogueByRevision.TryAdd(candidate.RevisionId, candidate))
            {
                throw new ArgumentException(
                    $"Creative Collection catalogue contains duplicate revision '{candidate.RevisionId}'.",
                    nameof(catalogue));
            }
        }

        AssetRevisionId[] anchors = directAnchorRevisionIds
            .Distinct()
            .OrderBy(revisionId => revisionId.ToString(), StringComparer.Ordinal)
            .ToArray();

        foreach (AssetRevisionId anchor in anchors)
        {
            if (!catalogueByRevision.ContainsKey(anchor))
            {
                throw new ArgumentException(
                    $"Direct anchor '{anchor}' is not present in the supplied catalogue.",
                    nameof(directAnchorRevisionIds));
            }
        }

        if (anchors.Length == 0)
        {
            return new CreativeCollectionCandidateSet(
                moments.PolicyVersion,
                policy.Version,
                0,
                0,
                []);
        }

        HashSet<AssetRevisionId> anchorSet = anchors.ToHashSet();
        Dictionary<AssetRevisionId, List<CreativeCollectionContextReason>> reasonsByRevision = [];

        foreach (PhotoMoment moment in moments.Moments)
        {
            PhotoMomentMember[] momentAnchors = moment.Members
                .Where(member => anchorSet.Contains(member.RevisionId))
                .OrderBy(member => member.TakenAtLocal)
                .ThenBy(member => member.RevisionId.ToString(), StringComparer.Ordinal)
                .ToArray();
            if (momentAnchors.Length == 0 || policy.MaximumContextPhotosPerAnchoredMoment == 0)
            {
                continue;
            }

            AssetRevisionId[] admittingAnchors = momentAnchors
                .Select(member => member.RevisionId)
                .ToArray();

            PhotoMomentMember[] selectedContext = moment.Members
                .Where(member => !anchorSet.Contains(member.RevisionId))
                .OrderBy(member => NearestAnchorDistanceTicks(member, momentAnchors))
                .ThenBy(member => member.TakenAtLocal)
                .ThenBy(member => member.RevisionId.ToString(), StringComparer.Ordinal)
                .Take(policy.MaximumContextPhotosPerAnchoredMoment)
                .ToArray();

            foreach (PhotoMomentMember context in selectedContext)
            {
                if (!reasonsByRevision.TryGetValue(context.RevisionId, out List<CreativeCollectionContextReason>? reasons))
                {
                    reasons = [];
                    reasonsByRevision.Add(context.RevisionId, reasons);
                }

                if (!reasons.Any(reason => string.Equals(reason.MomentId, moment.Id, StringComparison.Ordinal)))
                {
                    reasons.Add(new CreativeCollectionContextReason(moment.Id, admittingAnchors));
                }
            }
        }

        AssetRevisionId[] selectedRevisionIds = anchors
            .Concat(reasonsByRevision.Keys)
            .Distinct()
            .ToArray();

        CreativeCollectionCandidate[] candidates = selectedRevisionIds
            .Select(revisionId =>
            {
                PhotoMomentCandidate source = catalogueByRevision[revisionId];
                bool directAnchor = anchorSet.Contains(revisionId);
                IReadOnlyList<CreativeCollectionContextReason> reasons =
                    reasonsByRevision.TryGetValue(revisionId, out List<CreativeCollectionContextReason>? storedReasons)
                        ? storedReasons
                            .OrderBy(reason => reason.MomentId, StringComparer.Ordinal)
                            .ToArray()
                        : [];
                return new CreativeCollectionCandidate(
                    revisionId,
                    source.TakenAtLocal,
                    directAnchor
                        ? CreativeCollectionCandidateKinds.DirectAnchor
                        : CreativeCollectionCandidateKinds.ContextualAddition,
                    reasons);
            })
            .OrderBy(candidate => candidate.TakenAtLocal.HasValue ? 0 : 1)
            .ThenBy(candidate => candidate.TakenAtLocal ?? DateTime.MaxValue)
            .ThenBy(candidate => candidate.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();

        int addedContextCount = candidates.Count(candidate =>
            candidate.Kind == CreativeCollectionCandidateKinds.ContextualAddition);
        return new CreativeCollectionCandidateSet(
            moments.PolicyVersion,
            policy.Version,
            anchors.Length,
            addedContextCount,
            candidates);
    }

    private static long NearestAnchorDistanceTicks(
        PhotoMomentMember context,
        IReadOnlyList<PhotoMomentMember> anchors)
    {
        long nearest = long.MaxValue;
        foreach (PhotoMomentMember anchor in anchors)
        {
            long distance = Math.Abs((context.TakenAtLocal - anchor.TakenAtLocal).Ticks);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }
}
