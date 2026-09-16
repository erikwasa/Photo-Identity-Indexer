using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

/// <summary>
/// One canonical photo revision considered for derived moment clustering.
/// Capture time is photographic wall-clock time; no UTC conversion is inferred when an offset is absent.
/// Optional evidence may refine a time-based boundary when a policy explicitly enables it.
/// </summary>
public sealed record PhotoMomentCandidate(
    AssetRevisionId RevisionId,
    DateTime? TakenAtLocal,
    IReadOnlyCollection<string>? PeopleKeys = null,
    IReadOnlyCollection<string>? Tags = null,
    string? SourceGroupKey = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>
/// Versioned, regenerable moment-boundary policy. The base time gap is always authoritative unless
/// optional evidence settings are explicitly enabled.
/// </summary>
public sealed record PhotoMomentGapPolicy
{
    public PhotoMomentGapPolicy(
        string version,
        TimeSpan baseMaximumGap,
        TimeSpan? supportingEvidenceExtension = null,
        double? nearbyLocationKilometers = null,
        double? distantLocationSplitKilometers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (baseMaximumGap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseMaximumGap), "The base moment gap must be positive.");
        }

        TimeSpan resolvedExtension = supportingEvidenceExtension ?? TimeSpan.Zero;
        if (resolvedExtension < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(supportingEvidenceExtension));
        }

        if (nearbyLocationKilometers is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nearbyLocationKilometers));
        }

        if (distantLocationSplitKilometers is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distantLocationSplitKilometers));
        }

        Version = version.Trim();
        BaseMaximumGap = baseMaximumGap;
        SupportingEvidenceExtension = resolvedExtension;
        NearbyLocationKilometers = nearbyLocationKilometers;
        DistantLocationSplitKilometers = distantLocationSplitKilometers;
    }

    public string Version { get; }
    public TimeSpan BaseMaximumGap { get; }
    public TimeSpan SupportingEvidenceExtension { get; }
    public double? NearbyLocationKilometers { get; }
    public double? DistantLocationSplitKilometers { get; }

    public static PhotoMomentGapPolicy Evaluation30Minutes { get; } = new(
        "m26-time-gap-30m-v1",
        TimeSpan.FromMinutes(30));

    public static PhotoMomentGapPolicy Evaluation90Minutes { get; } = new(
        "m26-time-gap-90m-v1",
        TimeSpan.FromMinutes(90));

    public static PhotoMomentGapPolicy CreateTimeGapEvaluation(int gapMinutes)
    {
        if (gapMinutes is < 1 or > 12 * 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gapMinutes),
                "Evaluation gap must be between 1 and 720 minutes.");
        }

        return new PhotoMomentGapPolicy(
            $"m26-time-gap-{gapMinutes}m-v1",
            TimeSpan.FromMinutes(gapMinutes));
    }
}

public sealed record PhotoMomentMember(
    AssetRevisionId RevisionId,
    DateTime TakenAtLocal);

public sealed record PhotoMoment(
    string Id,
    string PolicyVersion,
    DateTime StartedAtLocal,
    DateTime EndedAtLocal,
    IReadOnlyList<PhotoMomentMember> Members);

public sealed record UnclusteredPhotoMomentCandidate(
    AssetRevisionId RevisionId,
    string Reason);

public sealed record PhotoMomentClusteringResult(
    string PolicyVersion,
    IReadOnlyList<PhotoMoment> Moments,
    IReadOnlyList<UnclusteredPhotoMomentCandidate> Unclustered);

/// <summary>
/// Pure derived grouping over canonical photo state. No moment membership is persisted or treated as
/// archive truth; callers regenerate the result whenever catalogue state or policy version changes.
/// </summary>
public static class PhotoMomentClusterer
{
    public const string MissingCaptureTimeReason = "missing-capture-time";

    public static PhotoMomentClusteringResult Cluster(
        IEnumerable<PhotoMomentCandidate> candidates,
        PhotoMomentGapPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(policy);

        PhotoMomentCandidate[] materialized = candidates.ToArray();
        EnsureUniqueRevisionIds(materialized);

        PhotoMomentCandidate[] timestamped = materialized
            .Where(candidate => candidate.TakenAtLocal.HasValue)
            .OrderBy(candidate => candidate.TakenAtLocal!.Value)
            .ThenBy(candidate => candidate.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();

        UnclusteredPhotoMomentCandidate[] unclustered = materialized
            .Where(candidate => !candidate.TakenAtLocal.HasValue)
            .OrderBy(candidate => candidate.RevisionId.ToString(), StringComparer.Ordinal)
            .Select(candidate => new UnclusteredPhotoMomentCandidate(
                candidate.RevisionId,
                MissingCaptureTimeReason))
            .ToArray();

        List<PhotoMoment> moments = [];
        if (timestamped.Length == 0)
        {
            return new PhotoMomentClusteringResult(policy.Version, moments, unclustered);
        }

        List<PhotoMomentCandidate> current = [timestamped[0]];
        for (int index = 1; index < timestamped.Length; index++)
        {
            PhotoMomentCandidate previous = timestamped[index - 1];
            PhotoMomentCandidate next = timestamped[index];
            if (ShouldSplit(previous, next, policy))
            {
                moments.Add(CreateMoment(moments.Count, current, policy.Version));
                current = [];
            }

            current.Add(next);
        }

        moments.Add(CreateMoment(moments.Count, current, policy.Version));
        return new PhotoMomentClusteringResult(policy.Version, moments, unclustered);
    }

    private static bool ShouldSplit(
        PhotoMomentCandidate previous,
        PhotoMomentCandidate next,
        PhotoMomentGapPolicy policy)
    {
        DateTime previousTime = previous.TakenAtLocal!.Value;
        DateTime nextTime = next.TakenAtLocal!.Value;
        TimeSpan gap = nextTime - previousTime;

        if (policy.DistantLocationSplitKilometers is double splitDistance &&
            TryDistanceKilometers(previous, next, out double distance) &&
            distance >= splitDistance)
        {
            return true;
        }

        if (gap <= policy.BaseMaximumGap)
        {
            return false;
        }

        if (policy.SupportingEvidenceExtension == TimeSpan.Zero ||
            gap > policy.BaseMaximumGap + policy.SupportingEvidenceExtension)
        {
            return true;
        }

        return !HasSupportingEvidence(previous, next, policy);
    }

    private static bool HasSupportingEvidence(
        PhotoMomentCandidate previous,
        PhotoMomentCandidate next,
        PhotoMomentGapPolicy policy)
    {
        if (SharesAny(previous.PeopleKeys, next.PeopleKeys) ||
            SharesAny(previous.Tags, next.Tags))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(previous.SourceGroupKey) &&
            string.Equals(previous.SourceGroupKey, next.SourceGroupKey, StringComparison.Ordinal))
        {
            return true;
        }

        return policy.NearbyLocationKilometers is double nearbyDistance &&
            TryDistanceKilometers(previous, next, out double distance) &&
            distance <= nearbyDistance;
    }

    private static bool SharesAny(
        IReadOnlyCollection<string>? first,
        IReadOnlyCollection<string>? second)
    {
        if (first is null || second is null || first.Count == 0 || second.Count == 0)
        {
            return false;
        }

        HashSet<string> values = new(first.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
        return second.Any(value => !string.IsNullOrWhiteSpace(value) && values.Contains(value));
    }

    private static bool TryDistanceKilometers(
        PhotoMomentCandidate first,
        PhotoMomentCandidate second,
        out double distance)
    {
        distance = 0;
        if (first.Latitude is not double firstLatitude ||
            first.Longitude is not double firstLongitude ||
            second.Latitude is not double secondLatitude ||
            second.Longitude is not double secondLongitude)
        {
            return false;
        }

        const double earthRadiusKilometers = 6371.0088;
        double latitudeDelta = DegreesToRadians(secondLatitude - firstLatitude);
        double longitudeDelta = DegreesToRadians(secondLongitude - firstLongitude);
        double firstLatitudeRadians = DegreesToRadians(firstLatitude);
        double secondLatitudeRadians = DegreesToRadians(secondLatitude);
        double a =
            Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
            Math.Cos(firstLatitudeRadians) * Math.Cos(secondLatitudeRadians) *
            Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        a = Math.Clamp(a, 0d, 1d);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        distance = earthRadiusKilometers * c;
        return true;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private static PhotoMoment CreateMoment(
        int index,
        IReadOnlyList<PhotoMomentCandidate> candidates,
        string policyVersion)
    {
        PhotoMomentMember[] members = candidates
            .Select(candidate => new PhotoMomentMember(
                candidate.RevisionId,
                DateTime.SpecifyKind(candidate.TakenAtLocal!.Value, DateTimeKind.Unspecified)))
            .ToArray();

        return new PhotoMoment(
            $"moment-{index + 1:D4}",
            policyVersion,
            members[0].TakenAtLocal,
            members[^1].TakenAtLocal,
            members);
    }

    private static void EnsureUniqueRevisionIds(IReadOnlyCollection<PhotoMomentCandidate> candidates)
    {
        AssetRevisionId? duplicate = candidates
            .GroupBy(candidate => candidate.RevisionId)
            .FirstOrDefault(group => group.Count() > 1)?
            .Key;
        if (duplicate.HasValue)
        {
            throw new ArgumentException(
                $"Moment clustering input contains duplicate revision '{duplicate.Value}'.",
                nameof(candidates));
        }
    }
}
