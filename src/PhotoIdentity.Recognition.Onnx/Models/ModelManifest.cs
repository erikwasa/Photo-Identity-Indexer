using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Recognition.Onnx.Models;

public sealed record ModelManifest
{
    public required int SchemaVersion { get; init; }
    public required string ModelId { get; init; }
    public required string Role { get; init; }
    public required string Format { get; init; }
    public required string FileName { get; init; }
    public required Uri DownloadUri { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public required string Runtime { get; init; }
    public required string SourceVersion { get; init; }
    public required ModelInputManifest Input { get; init; }
    public required ModelOutputManifest Output { get; init; }
    public required ModelLicenceManifest Licences { get; init; }
    public string? AlignmentProtocol { get; init; }

    public ModelDescriptor ToDescriptor()
    {
        ModelManifestValidator.Validate(this);

        ModelRole role = Role switch
        {
            "faceDetection" => ModelRole.FaceDetection,
            "faceEmbedding" => ModelRole.FaceEmbedding,
            _ => throw new ModelManifestException($"Unsupported model role '{Role}'."),
        };

        ModelFormat format = Format switch
        {
            "onnx" => ModelFormat.Onnx,
            _ => throw new ModelManifestException($"Unsupported model format '{Format}'."),
        };

        DistanceMetric? distanceMetric = Output.DistanceMetric switch
        {
            null => null,
            "cosine" => DistanceMetric.Cosine,
            "euclidean" => DistanceMetric.Euclidean,
            _ => throw new ModelManifestException(
                $"Unsupported distance metric '{Output.DistanceMetric}'."),
        };

        AlignmentProtocolId? alignmentProtocol = string.IsNullOrWhiteSpace(AlignmentProtocol)
            ? null
            : new AlignmentProtocolId(AlignmentProtocol);

        ModelInputShapePolicy inputShapePolicy = Input.ShapePolicy?.Kind switch
        {
            null or "fixed" => ModelInputShapePolicy.Fixed,
            "dynamic-multiple-of" => new ModelInputShapePolicy(
                ModelInputShapeKind.DynamicMultipleOf,
                Input.ShapePolicy.MultipleOf,
                Input.ShapePolicy.MaximumLongEdge),
            _ => throw new ModelManifestException(
                $"Unsupported input shape policy '{Input.ShapePolicy.Kind}'."),
        };

        return new ModelDescriptor(
            new ModelId(ModelId),
            role,
            format,
            new Sha256Digest(Sha256),
            new ImageSize(Input.Width, Input.Height),
            Runtime,
            Licences.Weights.Spdx,
            SourceVersion,
            Output.Dimensions,
            distanceMetric,
            alignmentProtocol,
            inputShapePolicy);
    }
}

public sealed record ModelInputManifest
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string ColourOrder { get; init; }
    public required string DataType { get; init; }
    public required ModelNormalisationManifest Normalisation { get; init; }
    public ModelInputShapeManifest? ShapePolicy { get; init; }
}

public sealed record ModelInputShapeManifest
{
    public required string Kind { get; init; }
    public int? MultipleOf { get; init; }
    public int? MaximumLongEdge { get; init; }
}

public sealed record ModelNormalisationManifest
{
    public required double Scale { get; init; }
    public required double[] Mean { get; init; }
}

public sealed record ModelOutputManifest
{
    public required string Kind { get; init; }
    public int? Dimensions { get; init; }
    public required string Normalisation { get; init; }
    public string? DistanceMetric { get; init; }
    public required string Semantics { get; init; }
}

public sealed record ModelLicenceManifest
{
    public required LicenceRecord Code { get; init; }
    public required LicenceRecord Weights { get; init; }
    public required TrainingDataRecord TrainingData { get; init; }
}

public sealed record LicenceRecord
{
    public required string Spdx { get; init; }
    public required Uri Source { get; init; }
}

public sealed record TrainingDataRecord
{
    public required string Name { get; init; }
    public required string Licence { get; init; }
    public required string Notes { get; init; }
}

public sealed class ModelManifestException : Exception
{
    public ModelManifestException(string message)
        : base(message)
    {
    }

    public ModelManifestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ModelManifestValidator
{
    private static readonly HashSet<string> SupportedColourOrders =
        new(StringComparer.Ordinal) { "BGR", "RGB" };

    private static readonly HashSet<string> SupportedDataTypes =
        new(StringComparer.Ordinal) { "float32", "uint8" };

    private static readonly HashSet<string> SupportedOutputNormalisations =
        new(StringComparer.Ordinal) { "none", "l2-by-adapter" };

    public static void Validate(ModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        List<string> errors = [];

        if (manifest.SchemaVersion != 1)
        {
            errors.Add($"schemaVersion must be 1, not {manifest.SchemaVersion}.");
        }

        Required(manifest.ModelId, "modelId", errors);
        Required(manifest.Role, "role", errors);
        Required(manifest.Format, "format", errors);
        Required(manifest.FileName, "fileName", errors);
        Required(manifest.Sha256, "sha256", errors);
        Required(manifest.Runtime, "runtime", errors);
        Required(manifest.SourceVersion, "sourceVersion", errors);

        if (manifest.Role is not ("faceDetection" or "faceEmbedding"))
        {
            errors.Add("role must be 'faceDetection' or 'faceEmbedding'.");
        }

        if (manifest.Format != "onnx")
        {
            errors.Add("format must be 'onnx' for this adapter.");
        }

        if (!IsSafeFileName(manifest.FileName))
        {
            errors.Add("fileName must be a file name without directory components.");
        }

        if (manifest.DownloadUri is null ||
            !manifest.DownloadUri.IsAbsoluteUri ||
            manifest.DownloadUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("downloadUri must be an absolute HTTPS URI.");
        }

        try
        {
            _ = new Sha256Digest(manifest.Sha256);
        }
        catch (ArgumentException exception)
        {
            errors.Add(exception.Message);
        }

        if (manifest.SizeBytes <= 0)
        {
            errors.Add("sizeBytes must be positive.");
        }

        if (manifest.Input is null)
        {
            errors.Add("input is required.");
        }
        else
        {
            if (manifest.Input.Width <= 0 || manifest.Input.Height <= 0)
            {
                errors.Add("input width and height must be positive.");
            }

            ValidateInputShapePolicy(manifest.Input, errors);

            if (!SupportedColourOrders.Contains(manifest.Input.ColourOrder))
            {
                errors.Add("input colourOrder must be 'BGR' or 'RGB'.");
            }

            if (!SupportedDataTypes.Contains(manifest.Input.DataType))
            {
                errors.Add("input dataType must be 'float32' or 'uint8'.");
            }

            if (manifest.Input.Normalisation is null)
            {
                errors.Add("input normalisation is required.");
            }
            else
            {
                if (!double.IsFinite(manifest.Input.Normalisation.Scale) ||
                    manifest.Input.Normalisation.Scale <= 0)
                {
                    errors.Add("input normalisation scale must be finite and positive.");
                }

                if (manifest.Input.Normalisation.Mean is null ||
                    manifest.Input.Normalisation.Mean.Length != 3 ||
                    manifest.Input.Normalisation.Mean.Any(value => !double.IsFinite(value)))
                {
                    errors.Add("input normalisation mean must contain three finite values.");
                }
            }
        }

        if (manifest.Output is null)
        {
            errors.Add("output is required.");
        }
        else
        {
            Required(manifest.Output.Kind, "output.kind", errors);
            Required(manifest.Output.Normalisation, "output.normalisation", errors);
            Required(manifest.Output.Semantics, "output.semantics", errors);

            if (!SupportedOutputNormalisations.Contains(manifest.Output.Normalisation))
            {
                errors.Add("output normalisation must be 'none' or 'l2-by-adapter'.");
            }

            if (manifest.Role == "faceDetection")
            {
                if (manifest.Output.Kind != "detections")
                {
                    errors.Add("face-detection output kind must be 'detections'.");
                }

                if (manifest.Output.Dimensions is not null ||
                    manifest.Output.DistanceMetric is not null)
                {
                    errors.Add("face-detection output cannot declare vector dimensions or a distance metric.");
                }
            }

            if (manifest.Role == "faceEmbedding")
            {
                if (manifest.Output.Kind != "embedding")
                {
                    errors.Add("face-embedding output kind must be 'embedding'.");
                }

                if (manifest.Output.Dimensions <= 0)
                {
                    errors.Add("face-embedding output dimensions must be positive.");
                }

                if (manifest.Output.DistanceMetric is not ("cosine" or "euclidean"))
                {
                    errors.Add("face-embedding output distanceMetric must be 'cosine' or 'euclidean'.");
                }

                if (string.IsNullOrWhiteSpace(manifest.AlignmentProtocol))
                {
                    errors.Add("face-embedding models require alignmentProtocol.");
                }
            }
        }

        ValidateLicence(manifest.Licences?.Code, "licences.code", errors);
        ValidateLicence(manifest.Licences?.Weights, "licences.weights", errors);

        if (manifest.Licences?.TrainingData is null)
        {
            errors.Add("licences.trainingData is required.");
        }
        else
        {
            Required(manifest.Licences.TrainingData.Name, "licences.trainingData.name", errors);
            Required(manifest.Licences.TrainingData.Licence, "licences.trainingData.licence", errors);
            Required(manifest.Licences.TrainingData.Notes, "licences.trainingData.notes", errors);
        }

        if (errors.Count > 0)
        {
            throw new ModelManifestException(
                $"Model manifest '{manifest.ModelId}' is invalid:{Environment.NewLine}- " +
                string.Join($"{Environment.NewLine}- ", errors));
        }
    }

    private static void ValidateInputShapePolicy(
        ModelInputManifest input,
        ICollection<string> errors)
    {
        if (input.ShapePolicy is null)
        {
            return;
        }

        Required(input.ShapePolicy.Kind, "input.shapePolicy.kind", errors);
        switch (input.ShapePolicy.Kind)
        {
            case "fixed":
                if (input.ShapePolicy.MultipleOf is not null ||
                    input.ShapePolicy.MaximumLongEdge is not null)
                {
                    errors.Add("fixed input shapePolicy cannot declare dynamic shape parameters.");
                }

                break;
            case "dynamic-multiple-of":
                if (input.ShapePolicy.MultipleOf <= 0)
                {
                    errors.Add("dynamic-multiple-of input shapePolicy requires a positive multipleOf.");
                }

                if (input.ShapePolicy.MaximumLongEdge <= 0)
                {
                    errors.Add("dynamic-multiple-of input shapePolicy requires a positive maximumLongEdge.");
                }

                if (input.ShapePolicy.MultipleOf > 0 &&
                    (input.Width % input.ShapePolicy.MultipleOf.Value != 0 ||
                     input.Height % input.ShapePolicy.MultipleOf.Value != 0))
                {
                    errors.Add("dynamic reference input width and height must be divisible by shapePolicy.multipleOf.");
                }

                break;
            default:
                errors.Add("input shapePolicy kind must be 'fixed' or 'dynamic-multiple-of'.");
                break;
        }
    }

    private static bool IsSafeFileName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value == Path.GetFileName(value) &&
        value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;

    private static void ValidateLicence(
        LicenceRecord? licence,
        string path,
        ICollection<string> errors)
    {
        if (licence is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        Required(licence.Spdx, $"{path}.spdx", errors);
        if (licence.Source is null ||
            !licence.Source.IsAbsoluteUri ||
            licence.Source.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{path}.source must be an absolute HTTPS URI.");
        }
    }

    private static void Required(
        string? value,
        string path,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{path} is required.");
        }
    }
}
