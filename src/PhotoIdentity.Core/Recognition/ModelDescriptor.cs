using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

public enum ModelRole
{
    FaceDetection,
    FaceEmbedding,
}

public enum ModelFormat
{
    Onnx,
    TorchScript,
    Other,
}

public enum DistanceMetric
{
    Cosine,
    Euclidean,
}

public enum ModelInputShapeKind
{
    Fixed,
    DynamicMultipleOf,
}

public sealed record ModelInputShapePolicy
{
    public ModelInputShapePolicy(ModelInputShapeKind kind, int? multipleOf = null)
    {
        if (kind == ModelInputShapeKind.Fixed && multipleOf is not null)
        {
            throw new ArgumentException("Fixed input shapes cannot declare a dynamic multiple.", nameof(multipleOf));
        }

        if (kind == ModelInputShapeKind.DynamicMultipleOf && multipleOf <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(multipleOf),
                "Dynamic-multiple input shapes require a positive multiple.");
        }

        Kind = kind;
        MultipleOf = multipleOf;
    }

    public ModelInputShapeKind Kind { get; }
    public int? MultipleOf { get; }

    public static ModelInputShapePolicy Fixed { get; } = new(ModelInputShapeKind.Fixed);
}

public readonly record struct Sha256Digest
{
    public Sha256Digest(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalised = value.Trim().ToLowerInvariant();
        if (normalised.Length != 64 || normalised.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 digest must contain exactly 64 hexadecimal characters.", nameof(value));
        }

        Value = normalised;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public sealed record ModelDescriptor
{
    public ModelDescriptor(
        ModelId id,
        ModelRole role,
        ModelFormat format,
        Sha256Digest modelHash,
        ImageSize inputSize,
        string runtime,
        string licence,
        string sourceVersion,
        int? outputDimensions = null,
        DistanceMetric? distanceMetric = null,
        AlignmentProtocolId? alignmentProtocol = null,
        ModelInputShapePolicy? inputShapePolicy = null)
    {
        if (outputDimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputDimensions), "Output dimensions must be positive when supplied.");
        }

        if (role == ModelRole.FaceEmbedding && (outputDimensions is null || distanceMetric is null))
        {
            throw new ArgumentException("Embedding models require output dimensions and a distance metric.");
        }

        Id = id;
        Role = role;
        Format = format;
        ModelHash = modelHash;
        InputSize = inputSize;
        Runtime = Required(runtime, nameof(runtime));
        Licence = Required(licence, nameof(licence));
        SourceVersion = Required(sourceVersion, nameof(sourceVersion));
        OutputDimensions = outputDimensions;
        DistanceMetric = distanceMetric;
        AlignmentProtocol = alignmentProtocol;
        InputShapePolicy = inputShapePolicy ?? ModelInputShapePolicy.Fixed;
    }

    public ModelId Id { get; }
    public ModelRole Role { get; }
    public ModelFormat Format { get; }
    public Sha256Digest ModelHash { get; }
    public ImageSize InputSize { get; }
    public ModelInputShapePolicy InputShapePolicy { get; }
    public string Runtime { get; }
    public string Licence { get; }
    public string SourceVersion { get; }
    public int? OutputDimensions { get; }
    public DistanceMetric? DistanceMetric { get; }
    public AlignmentProtocolId? AlignmentProtocol { get; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
