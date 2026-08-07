using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;

namespace PhotoIdentity.Worker;

/// <summary>
/// Builds the canonical behavioural identity for local detector runs.
/// </summary>
public static class DetectorPipelineIdentityFactory
{
    private const double DetectorNmsThreshold = 0.30;
    private const int DetectorTopK = 5000;

    public static DetectorPipelineDefinition Create(
        ModelManifest detectorManifest,
        LocalBatchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(detectorManifest);
        ArgumentNullException.ThrowIfNull(configuration);
        ModelManifestValidator.Validate(detectorManifest);

        if (!string.Equals(detectorManifest.Role, "faceDetection", StringComparison.Ordinal))
        {
            throw new ModelManifestException(
                $"Model '{detectorManifest.ModelId}' is not a face-detection model.");
        }

        if (!string.Equals(detectorManifest.ModelId, configuration.DetectorModelId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The detector manifest does not match the configured detector model.",
                nameof(detectorManifest));
        }

        bool multiScale = string.Equals(
            configuration.DetectorPipeline,
            LocalBatchConfiguration.MultiScaleDetectorPipeline,
            StringComparison.Ordinal);
        string shapePolicy = detectorManifest.Input.ShapePolicy?.Kind ?? "fixed";
        string implementationId;
        string resizePolicy;
        if (string.Equals(detectorManifest.ModelId, "centerface-2019-fp32", StringComparison.Ordinal))
        {
            implementationId = "centerface-opencv-dnn-v1";
            resizePolicy = "direct-resize-bounded-dynamic-multiple-of";
        }
        else if (string.Equals(
                     detectorManifest.ModelId,
                     LocalBatchConfiguration.DefaultDetectorModelId,
                     StringComparison.Ordinal))
        {
            implementationId = multiScale ? "yunet-full-image-plus-tiles-v1" : "yunet-single-pass-v1";
            resizePolicy = multiScale
                ? "aspect-preserving-full-image-and-overlapping-tiles"
                : "fixed-model-input";
        }
        else
        {
            throw new ModelManifestException(
                $"No detector-pipeline identity adapter is registered for model '{detectorManifest.ModelId}'.");
        }

        return new DetectorPipelineDefinition(
            implementationId,
            new ModelId(detectorManifest.ModelId),
            new Sha256Digest(detectorManifest.Sha256),
            detectorManifest.Runtime,
            configuration.ConfidenceThreshold,
            configuration.DetectorPipeline,
            resizePolicy,
            detectorManifest.Input.Width,
            detectorManifest.Input.Height,
            shapePolicy,
            detectorManifest.Input.ShapePolicy?.MultipleOf,
            detectorManifest.Input.ShapePolicy?.MaximumLongEdge,
            detectorManifest.Input.ColourOrder,
            detectorManifest.Input.DataType,
            detectorManifest.Input.Normalisation.Scale,
            detectorManifest.Input.Normalisation.Mean,
            DetectorNmsThreshold,
            DetectorTopK,
            multiScale ? configuration.TileSize : null,
            multiScale ? configuration.TileOverlap : null,
            multiScale ? configuration.MergeNmsThreshold : null,
            rotationPolicy: "none");
    }
}
