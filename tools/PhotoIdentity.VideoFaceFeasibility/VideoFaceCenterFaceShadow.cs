using System.Text.Json.Serialization;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Entire-candidate, in-memory independent-detector comparison. A candidate
/// lacking CenterFace corroboration remains a counted, bounded, reviewable
/// YuNet observation, never an automatically rejected non-face.
/// </summary>
public sealed record VideoFaceCenterFaceCounterfactual(
    int SchemaVersion,
    string IndependentDetectorId,
    string IndependentDetectorSha256,
    double MinimumIntersectionOverUnion,
    int FramesChecked,
    int RetainedYuNetObservations,
    int CorroboratedAt030,
    int CorroboratedAt050,
    int NotCorroboratedAt030,
    VideoFaceUnassignedAudit NotCorroboratedAudit,
    VideoFaceShadowDbscanReview CorroboratedOnlyDbscan)
{
    public bool AllCandidatesAccountedFor =>
        CorroboratedAt030 + NotCorroboratedAt030 == RetainedYuNetObservations &&
        CorroboratedAt050 <= CorroboratedAt030 &&
        NotCorroboratedAudit.TotalUnassignedObservations == NotCorroboratedAt030 &&
        CorroboratedOnlyDbscan.ObservationsEvaluated == CorroboratedAt030 &&
        CorroboratedOnlyDbscan.AllObservationsAccountedFor &&
        CorroboratedOnlyDbscan.SameFrameCollisions == 0;
}

/// <summary>
/// Optional shadow accumulator for an already-approved SFace catalogue run.
/// All per-frame face and embedding arrays are reused; only one independent
/// detector pass is made per decoded frame. Corroborated group membership
/// remains entirely separate from the baseline, including all catalogue ranks.
/// </summary>
public sealed class VideoFaceCenterFaceShadow
{
    private readonly VideoFaceShadowDbscan _corroborated = new();
    private readonly VideoFaceUnassignedAuditSampler _notCorroborated;
    private readonly string _detectorId;
    private readonly string _detectorSha256;
    private int _frames;
    private int _observed;
    private int _agreed030;
    private int _agreed050;

    public VideoFaceCenterFaceShadow(
        long firstTimestampMs, long lastTimestampMs, ModelDescriptor independentDescriptor)
    {
        ArgumentNullException.ThrowIfNull(independentDescriptor);
        _notCorroborated = new(firstTimestampMs, lastTimestampMs);
        _detectorId = independentDescriptor.Id.Value;
        _detectorSha256 = independentDescriptor.ModelHash.Value;
    }

    public void ObserveFrame(
        int frameNumber, long timestampMs, ImageSize imageSize,
        IReadOnlyList<DetectedFaceCandidate> yunet,
        IReadOnlyList<EmbeddingVector> vectors,
        IReadOnlyList<DetectedFaceCandidate> independent)
    {
        ArgumentNullException.ThrowIfNull(yunet);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(independent);
        if (frameNumber != _frames + 1 ||
            frameNumber is < 1 or > VideoFaceFrameSetLoader.MaximumCoverageFrames ||
            timestampMs < 0 ||
            imageSize.Width < 1 || imageSize.Height < 1 ||
            yunet.Count != vectors.Count || yunet.Count > 32 ||
            _observed + yunet.Count > VideoFaceShadowDbscan.MaximumObservations)
        {
            throw new ArgumentException("Independent shadow frame/candidate accounting mismatch.");
        }

        for (int i = 0; i < yunet.Count; i++)
        {
            DetectedFaceCandidate face = yunet[i];
            NormalizedBoundingBox b = face.BoundingBox;
            double minPixels = Math.Min(
                b.Width * imageSize.Width, b.Height * imageSize.Height);
            var crop = new VideoFaceGroupCrop(frameNumber, timestampMs, i + 1,
                b.X, b.Y, b.Width, b.Height,
                Math.Round(face.Confidence, 4),
                Math.Round(face.Confidence * Math.Min(1d, Math.Max(0d, minPixels) / 160d), 4));
            double overlap030 = VideoFaceIndependentDetectorAudit.BestOverlap(
                crop, independent, .30);
            double overlap050 = VideoFaceIndependentDetectorAudit.BestOverlap(
                crop, independent, .50);
            bool agrees030 = overlap030 >= VideoFaceIndependentDetectorAudit.OverlapForExploration;
            bool agrees050 = overlap050 >= VideoFaceIndependentDetectorAudit.OverlapForExploration;
            _observed++;

            if (agrees050 && !agrees030)
                throw new InvalidOperationException("Independent agreement thresholds are inconsistent.");

            if (agrees030)
            {
                _agreed030++;
                _corroborated.Observe(frameNumber, timestampMs, i + 1,
                    vectors[i], face, imageSize);
            }
            else
            {
                // Preserved as a private, human-reviewable crop. Disagreement
                // neither deletes the YuNet observation nor proves background.
                _notCorroborated.Observe(crop);
            }
            if (agrees050) _agreed050++;
        }
        _frames++;
    }

    public async Task<VideoFaceCenterFaceCounterfactual> EvaluateAsync(
        VideoFaceCataloguePersonMatcher catalogue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        int notCorroborated = _observed - _agreed030;
        var result = new VideoFaceCenterFaceCounterfactual(
            1, _detectorId, _detectorSha256,
            VideoFaceIndependentDetectorAudit.OverlapForExploration,
            _frames, _observed, _agreed030, _agreed050, notCorroborated,
            _notCorroborated.CreateReview(notCorroborated),
            await _corroborated.EvaluateAsync(catalogue, cancellationToken));
        if (!result.AllCandidatesAccountedFor)
            throw new InvalidOperationException(
                "Independent shadow lost observations or merged simultaneous faces.");
        return result;
    }
}
