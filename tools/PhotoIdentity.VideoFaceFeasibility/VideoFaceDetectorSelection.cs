using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Recognition.Onnx.YuNet;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Explicit, bounded detector experiment selection. Single-pass is the historical
/// baseline; multiscale reuses the already-governed YuNet model and source tiles.
/// Never enables a production worker or automatic identity assignment.
/// </summary>
public sealed record VideoFaceDetectorSelection(
    YuNetDetectorPipelineMode PipelineMode,
    int TileSize)
{
    public static VideoFaceDetectorSelection Parse(
        string? requestedPipeline,
        string? requestedTileSize)
    {
        YuNetDetectorPipelineMode mode = requestedPipeline switch
        {
            null or "single-pass" => YuNetDetectorPipelineMode.SinglePass,
            "multi-scale" => YuNetDetectorPipelineMode.MultiScale,
            _ => throw new ArgumentException(
                "Detector pipeline must be single-pass or multi-scale."),
        };

        if (requestedTileSize is not null &&
            mode != YuNetDetectorPipelineMode.MultiScale)
        {
            throw new ArgumentException(
                "A tile size is allowed only when multi-scale detection is selected.");
        }

        int tileSize = requestedTileSize switch
        {
            null or "1024" => 1024,
            "640" => 640,
            _ => throw new ArgumentException("The experimental tile size must be 640 or 1024."),
        };

        return new VideoFaceDetectorSelection(mode, tileSize);
    }

    public YuNetDetectorOptions CreateOptions(double confidence) => new()
    {
        ConfidenceThreshold = confidence,
        PipelineMode = PipelineMode,
        TileSize = TileSize,
        // The governed detector retains its existing 0.2 tile overlap,
        // merge suppression, model and alignment protocol.
    };

    public VideoFaceDetectorProfile Describe(ImageSize modelInputSize) => new(
        PipelineMode == YuNetDetectorPipelineMode.MultiScale
            ? "multi-scale"
            : "single-pass",
        modelInputSize.Width,
        modelInputSize.Height,
        PipelineMode == YuNetDetectorPipelineMode.MultiScale ? TileSize : null);
}
