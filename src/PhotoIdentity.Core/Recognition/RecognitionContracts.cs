using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Core.Recognition;

public sealed record DecodeOptions(ImageSize? MaximumSize = null);

public interface IImageDecoder
{
    Task<ImageFrame> DecodeAsync(
        Stream source,
        DecodeOptions options,
        CancellationToken cancellationToken);
}

public sealed record DetectedFaceCandidate
{
    public DetectedFaceCandidate(
        NormalizedBoundingBox boundingBox,
        NormalizedFaceLandmarks landmarks,
        double confidence)
    {
        if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        }

        BoundingBox = boundingBox;
        Landmarks = landmarks;
        Confidence = confidence;
    }

    public NormalizedBoundingBox BoundingBox { get; }
    public NormalizedFaceLandmarks Landmarks { get; }
    public double Confidence { get; }
}

public interface IFaceDetector
{
    ModelDescriptor Descriptor { get; }

    Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
        ImageFrame image,
        CancellationToken cancellationToken);
}

public sealed record AlignedFace(ImageFrame Image, AlignmentProtocolId Protocol);

public interface IFaceAligner
{
    Task<AlignedFace> AlignAsync(
        ImageFrame image,
        DetectedFaceCandidate detection,
        AlignmentProtocolId protocol,
        CancellationToken cancellationToken);
}

public interface IFaceEmbedder
{
    ModelDescriptor Descriptor { get; }

    Task<EmbeddingVector> EmbedAsync(
        AlignedFace face,
        CancellationToken cancellationToken);
}

public sealed record IdentityCandidate
{
    public IdentityCandidate(PersonId personId, double score)
    {
        if (!double.IsFinite(score))
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Score must be finite.");
        }

        PersonId = personId;
        Score = score;
    }

    public PersonId PersonId { get; }
    public double Score { get; }
}

public interface IIdentityMatcher
{
    Task<IReadOnlyList<IdentityCandidate>> FindCandidatesAsync(
        FaceOccurrenceId faceId,
        ModelId embeddingModelId,
        CancellationToken cancellationToken);
}
