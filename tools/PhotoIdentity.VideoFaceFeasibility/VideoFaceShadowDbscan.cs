using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using System.Text.Json.Serialization;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Shadow-only alternative to the capped chronological grouper. Reuses the
/// production M25 DBSCAN algorithm, but deliberately NOT its still-photo
/// thresholds, distinct-photo voting, or known-person advisory policy.
/// No embeddings, canonical IDs or membership maps leave memory.
/// </summary>
public sealed record VideoFaceShadowDbscanGroup(
    string Alias,
    int Observations,
    IReadOnlyList<VideoFaceGroupCrop> RepresentativeCrops,
    IReadOnlyList<VideoFaceCatalogueRank> TopCandidates);

public sealed record VideoFaceShadowDbscanReview(
    int SchemaVersion,
    string PolicyVersion,
    double MaximumCosineDistance,
    int MinimumSamples,
    int MinimumClusterSize,
    int ObservationsEvaluated,
    int ClusterCount,
    int ClusterMembers,
    int NoiseObservations,
    int NotSameFrameConstraints,
    int GroupsRepresentedForReview,
    int GroupsNotRepresentedForReview,
    int MembersInUnrepresentedGroups,
    int SameFrameCollisions,
    IReadOnlyList<VideoFaceShadowDbscanGroup> Groups)
{
    // The review list is bounded but the count of omitted groups and their
    // members is explicit. Noise is not deleted and may contain real faces.
    public bool AllObservationsAccountedFor =>
        ClusterMembers + NoiseObservations == ObservationsEvaluated;

    // Quality-biased, temporally stratified review examples, not all noise
    // and not a representative estimate of shadow singleton face precision.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoFaceUnassignedAudit? NoiseAudit { get; init; }
}

/// <summary>
/// Exact, bounded, operator-opt-in experimental comparison. Captures SFace
/// vectors that the normal selected video run ALREADY computed, and evaluates
/// M25 clustering at the end of the clip in memory. It does not modify the
/// baseline 64-group decisions, catalogue ranking, or face detection.
/// </summary>
public sealed class VideoFaceShadowDbscan
{
    public const int MaximumObservations = 180 * 32;
    public const int MaximumReviewGroups = 64;
    public const int MaximumNotSameConstraints = 100_000;
    public const double ExperimentalCosineDistance = 0.18;
    public const string ExperimentalPolicyVersion = "wi0155-video-shadow-dbscan-e018-min2-v1";

    private static readonly ProvisionalFaceClusterPolicy Policy = new(
        Version: ExperimentalPolicyVersion,
        Algorithm: "dbscan-cosine",
        MinimumClusterSize: 2,
        MinimumSamples: 2,
        DistanceThreshold: ExperimentalCosineDistance).Validate();

    private sealed record Observation(
        FaceOccurrenceId Id,
        AssetRevisionId FrameId,
        int FrameNumber,
        int FaceNumber,
        EmbeddingVector Embedding,
        VideoFaceGroupCrop Crop);

    private readonly List<Observation> _observations = [];
    private readonly Dictionary<int, List<FaceOccurrenceId>> _byFrame = [];
    private int _constraints;
    private int _lastFrame;
    private int _lastFace;

    public int Count => _observations.Count;

    public void Observe(int frameNumber, long timestampMs, int faceNumber,
        EmbeddingVector embedding, DetectedFaceCandidate detection, ImageSize decodedSize)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(detection);
        if (frameNumber is < 1 or > 180 || faceNumber is < 1 or > 32 ||
            timestampMs < 0 || decodedSize.Width < 1 || decodedSize.Height < 1 ||
            frameNumber < _lastFrame ||
            (frameNumber == _lastFrame && faceNumber <= _lastFace))
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumber),
                "Shadow observations must be within bounds and ordered by frame and face.");
        }

        if (_observations.Count == MaximumObservations)
        {
            throw new InvalidOperationException("The shadow observation limit was exceeded.");
        }

        var b = detection.BoundingBox;
        double size = Math.Min(b.Width * decodedSize.Width, b.Height * decodedSize.Height);
        var crop = new VideoFaceGroupCrop(
            frameNumber, timestampMs, faceNumber,
            b.X, b.Y, b.Width, b.Height,
            Math.Round(detection.Confidence, 4),
            Math.Round(detection.Confidence * Math.Min(1d, Math.Max(0d, size) / 160d), 4));

        // Local surrogate IDs are deterministic ordinals, NOT database face
        // occurrence or asset revision IDs. They are never serialized.
        int ordinal = _observations.Count + 1;
        FaceOccurrenceId id = FaceOccurrenceId.From(
            Guid.Parse($"00000000-0000-0000-0000-{ordinal:X12}"));
        AssetRevisionId frameId = AssetRevisionId.From(
            Guid.Parse($"00000000-0000-0000-0000-{frameNumber:X12}"));

        if (!_byFrame.TryGetValue(frameNumber, out var simultaneous))
        {
            simultaneous = [];
            _byFrame.Add(frameNumber, simultaneous);
        }

        _constraints += simultaneous.Count;
        if (_constraints > MaximumNotSameConstraints)
        {
            throw new InvalidOperationException("Shadow same-frame constraint budget exceeded.");
        }

        simultaneous.Add(id);
        _observations.Add(new Observation(
            id, frameId, frameNumber, faceNumber, embedding, crop));
        _lastFrame = frameNumber;
        _lastFace = faceNumber;
    }

    public async Task<VideoFaceShadowDbscanReview> EvaluateAsync(
        VideoFaceCataloguePersonMatcher matcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        if (_observations.Count == 0)
        {
            return new VideoFaceShadowDbscanReview(
                1, ExperimentalPolicyVersion, ExperimentalCosineDistance,
                2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        }

        // The M25 implementation has a strict sparse-edge budget. If an
        // over-dense set exceeds it, fail closed instead of skipping faces
        // or silently making the cosine threshold stricter.
        var input = _observations.Select(o => new ProvisionalFaceClusterInputFace(
            o.Id, o.FrameId, "unreviewed-video-candidate", o.Embedding)).ToArray();

        var constraints = new List<ProvisionalFaceNotSameConstraint>(_constraints);
        foreach (List<FaceOccurrenceId> ids in _byFrame.Values)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    constraints.Add(ProvisionalFaceNotSameConstraint.Create(ids[i], ids[j]));
                }
            }
        }

        var clusterer = new ProvisionalFaceDbscanClusterer();
        ProvisionalFaceClusterComputation computed = await clusterer.ComputeAsync(
            input, Policy, constraints, cancellationToken: cancellationToken);
        if (computed.EvaluatedFaceCount != _observations.Count ||
            computed.Memberships.Count != _observations.Count ||
            computed.NoiseCount < 0)
        {
            throw new InvalidOperationException("Shadow DBSCAN did not account for all observations.");
        }

        Dictionary<FaceOccurrenceId, ProvisionalFaceClusterComputedMembership> memberships =
            computed.Memberships.ToDictionary(item => item.FaceOccurrenceId);
        if (memberships.Count != _observations.Count)
        {
            throw new InvalidOperationException("Shadow membership has duplicate or missing members.");
        }

        var clusters = _observations
            .Where(o => memberships[o.Id].DerivedClusterKey is not null)
            .GroupBy(o => memberships[o.Id].DerivedClusterKey!, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(o => o.FrameNumber)
                .ThenBy(o => o.FaceNumber)
                .ToArray())
            .OrderByDescending(group => group.Length)
            .ThenBy(group => group[0].FrameNumber)
            .ThenBy(group => group[0].FaceNumber)
            .ToArray();

        int members = clusters.Sum(group => group.Length);
        int noise = _observations.Count - members;
        int collisions = clusters.Sum(group =>
            group.Length - group.Select(o => o.FrameNumber).Distinct().Count());

        if (computed.ClusterCount != clusters.Length ||
            computed.NoiseCount != noise ||
            collisions != 0 ||
            members + noise != _observations.Count)
        {
            throw new InvalidOperationException(
                "Shadow DBSCAN violated observation accounting or same-frame constraints.");
        }

        VideoFaceShadowDbscanGroup[] chosen = clusters
            .Take(MaximumReviewGroups)
            .Select((group, i) =>
            {
                // Up to five strongest spatially suitable moments, with a
                // minimum frame gap when feasible. No identity labels vote
                // in clustering and all five remain advisory only.
                List<Observation> selected = [];
                foreach (Observation o in group
                    .OrderByDescending(o => o.Crop.Quality)
                    .ThenBy(o => o.FrameNumber)
                    .ThenBy(o => o.FaceNumber))
                {
                    if (selected.Any(s =>
                        Math.Abs(s.FrameNumber - o.FrameNumber) <
                        VideoFaceProvisionalGrouper.MinimumRepresentativeFrameSeparation))
                    {
                        continue;
                    }

                    selected.Add(o);
                    if (selected.Count == VideoFaceProvisionalGrouper.MaximumCropsPerGroup)
                        break;
                }

                // If a cluster is confined to adjacent frames, still preserve
                // at least its strongest real observation for review.
                if (selected.Count == 0)
                    throw new InvalidOperationException("Shadow cluster has no representative.");

                return new VideoFaceShadowDbscanGroup(
                    $"D{i + 1:D3}", group.Length,
                    selected.Select(o => o.Crop).ToArray(),
                    matcher.RankRepresentatives(
                        selected.Select(o => o.Embedding).ToArray()));
            }).ToArray();

        int selectedMembers = clusters.Take(MaximumReviewGroups).Sum(g => g.Length);
        var noiseSampler = new VideoFaceUnassignedAuditSampler(
            _observations[0].Crop.TimestampMs, _observations[^1].Crop.TimestampMs);
        foreach (Observation candidate in _observations)
        {
            if (memberships[candidate.Id].Role == ProvisionalFaceClusterMemberRole.Noise)
                noiseSampler.Observe(candidate.Crop);
        }

        return new VideoFaceShadowDbscanReview(
            1, ExperimentalPolicyVersion, ExperimentalCosineDistance,
            Policy.MinimumSamples, Policy.MinimumClusterSize,
            _observations.Count, clusters.Length, members, noise,
            constraints.Count, chosen.Length, clusters.Length - chosen.Length,
            members - selectedMembers, collisions, chosen)
        {
            NoiseAudit = noiseSampler.CreateReview(noise),
        };
    }
}
