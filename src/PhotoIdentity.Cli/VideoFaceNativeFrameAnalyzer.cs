using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Production-style, in-memory per-face native frame analysis with the
/// existing YuNet detector, five-point aligner and SFace embedder contracts.
/// No frame is resized or upsampled; no grouping/heuristic identity label is
/// produced. Observation vectors live only until the guarded publisher runs.
/// </summary>
public sealed class VideoFaceNativeFrameAnalyzer(
    IFaceDetector detector, IFaceAligner aligner, IFaceEmbedder embedder)
{
    public const int MaximumFacesPerFrame = 32;
    public const int MaximumFramesPerAttempt = 4;

    public static void ValidateModels(
        ModelDescriptor detector, ModelDescriptor embedder)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(embedder);
        if (detector.Id.Value != "yunet-2023mar-fp32" ||
            detector.ModelHash.Value !=
                "8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4" ||
            detector.Role != ModelRole.FaceDetection ||
            embedder.Id.Value != "sface-2021dec-fp32" ||
            embedder.ModelHash.Value !=
                "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79" ||
            embedder.Role != ModelRole.FaceEmbedding ||
            embedder.OutputDimensions != 128 ||
            embedder.DistanceMetric != DistanceMetric.Cosine ||
            embedder.AlignmentProtocol != new AlignmentProtocolId("sface-five-point-v1"))
            throw new InvalidOperationException(
                "Video faces require the approved exact YuNet/SFace model pair.");
    }

    public async Task<IReadOnlyList<VideoFaceEmbeddingObservation>> AnalyzeAsync(
        ImageFrame nativeFrame, int frameOrdinal, long timestampMs,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(aligner);
        ArgumentNullException.ThrowIfNull(embedder);
        VideoFaceNativeFrameProfile.ValidateDecodedImage(nativeFrame);
        ValidateModels(detector.Descriptor, embedder.Descriptor);
        if (frameOrdinal is < 0 or >= MaximumFramesPerAttempt ||
            timestampMs is < 0 or > 3_600_000)
            throw new ArgumentOutOfRangeException(nameof(frameOrdinal));

        IReadOnlyList<DetectedFaceCandidate> faces;
        try
        {
            faces = await detector.DetectAsync(nativeFrame, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw VideoFaceFailureEvidence.Mark(error, "face-detector");
        }
        if (faces is null || faces.Count > MaximumFacesPerFrame)
            throw VideoFaceFailureEvidence.Mark(new InvalidDataException(
                "Native video frame exceeded the approved 32-face observation limit."),
                "face-detection-overflow");

        List<VideoFaceEmbeddingObservation> observations = [];
        for (int ordinal = 0; ordinal < faces.Count; ordinal++)
        {
            token.ThrowIfCancellationRequested();
            DetectedFaceCandidate face = faces[ordinal];
            if (face is null)
                throw VideoFaceFailureEvidence.Mark(
                    new InvalidDataException("Detector returned a null face."),
                    "face-observation-validation");
            AlignedFace aligned;
            try
            {
                aligned = await aligner.AlignAsync(
                    nativeFrame, face, embedder.Descriptor.AlignmentProtocol!.Value, token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw VideoFaceFailureEvidence.Mark(error, "face-alignment");
            }
            EmbeddingVector vector;
            try
            {
                vector = await embedder.EmbedAsync(aligned, token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw VideoFaceFailureEvidence.Mark(error, "face-embedding");
            }
            if (vector.Dimensions != 128)
                throw VideoFaceFailureEvidence.Mark(
                    new InvalidDataException("SFace vector dimensions are not 128."),
                    "face-observation-validation");
            var box = face.BoundingBox;
            observations.Add(new VideoFaceEmbeddingObservation(
                frameOrdinal, timestampMs, ordinal,
                nativeFrame.Size.Width, nativeFrame.Size.Height,
                box.X, box.Y, box.Width, box.Height, vector));
        }

        return observations;
    }
}
