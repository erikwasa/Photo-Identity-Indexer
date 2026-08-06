using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Recognition.Onnx.YuNet;

public enum YuNetDetectorPipelineMode
{
    SinglePass,
    MultiScale,
}

internal readonly record struct YuNetSourceRegion
{
    public YuNetSourceRegion(int x, int y, int width, int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "The source-region X coordinate must be non-negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "The source-region Y coordinate must be non-negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The source-region width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "The source-region height must be positive.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);

    public static YuNetSourceRegion FullImage(ImageSize size) => new(0, 0, size.Width, size.Height);

    public void ValidateWithin(ImageSize size)
    {
        if (Right > size.Width || Bottom > size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "The source region must fit within the source image.");
        }
    }
}

internal static class YuNetTilePlanner
{
    public static IReadOnlyList<YuNetSourceRegion> CreateTiles(
        ImageSize imageSize,
        int tileSize,
        double overlap)
    {
        if (tileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), "The tile size must be positive.");
        }

        if (!double.IsFinite(overlap) || overlap < 0 || overlap >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "The tile overlap must be at least zero and less than one.");
        }

        int tileWidth = Math.Min(tileSize, imageSize.Width);
        int tileHeight = Math.Min(tileSize, imageSize.Height);
        if (tileWidth == imageSize.Width && tileHeight == imageSize.Height)
        {
            return [];
        }

        IReadOnlyList<int> xStarts = AxisStarts(imageSize.Width, tileWidth, overlap);
        IReadOnlyList<int> yStarts = AxisStarts(imageSize.Height, tileHeight, overlap);
        List<YuNetSourceRegion> tiles = new(checked(xStarts.Count * yStarts.Count));
        foreach (int y in yStarts)
        {
            foreach (int x in xStarts)
            {
                tiles.Add(new YuNetSourceRegion(x, y, tileWidth, tileHeight));
            }
        }

        return tiles;
    }

    private static IReadOnlyList<int> AxisStarts(int length, int tileLength, double overlap)
    {
        if (tileLength >= length)
        {
            return [0];
        }

        int step = Math.Max(1, (int)Math.Floor(tileLength * (1 - overlap)));
        List<int> starts = [0];
        while (starts[^1] + tileLength < length)
        {
            int next = Math.Min(starts[^1] + step, length - tileLength);
            if (next <= starts[^1])
            {
                break;
            }

            starts.Add(next);
        }

        return starts;
    }
}

internal sealed record YuNetPreprocessingTransform(
    ImageSize SourceImageSize,
    YuNetSourceRegion SourceRegion,
    ImageSize CanvasSize,
    double ScaleX,
    double ScaleY,
    double OffsetX,
    double OffsetY)
{
    public DetectedFaceCandidate? MapToSource(DetectedFaceCandidate detection)
    {
        ArgumentNullException.ThrowIfNull(detection);

        NormalizedBoundingBox box = detection.BoundingBox;
        double canvasLeft = box.X * CanvasSize.Width;
        double canvasTop = box.Y * CanvasSize.Height;
        double canvasRight = box.Right * CanvasSize.Width;
        double canvasBottom = box.Bottom * CanvasSize.Height;

        double left = ClampSourceX(SourceRegion.X + ((canvasLeft - OffsetX) / ScaleX));
        double top = ClampSourceY(SourceRegion.Y + ((canvasTop - OffsetY) / ScaleY));
        double right = ClampSourceX(SourceRegion.X + ((canvasRight - OffsetX) / ScaleX));
        double bottom = ClampSourceY(SourceRegion.Y + ((canvasBottom - OffsetY) / ScaleY));
        if (right <= left || bottom <= top)
        {
            return null;
        }

        double normalizedLeft = Math.Clamp(left / SourceImageSize.Width, 0, 1);
        double normalizedTop = Math.Clamp(top / SourceImageSize.Height, 0, 1);
        double normalizedRight = Math.Clamp(right / SourceImageSize.Width, normalizedLeft, 1);
        double normalizedBottom = Math.Clamp(bottom / SourceImageSize.Height, normalizedTop, 1);
        double normalizedWidth = normalizedRight - normalizedLeft;
        double normalizedHeight = normalizedBottom - normalizedTop;
        if (normalizedWidth <= 0 || normalizedHeight <= 0)
        {
            return null;
        }

        NormalizedPoint MapPoint(NormalizedPoint point)
        {
            double canvasX = point.X * CanvasSize.Width;
            double canvasY = point.Y * CanvasSize.Height;
            double sourceX = ClampSourceX(SourceRegion.X + ((canvasX - OffsetX) / ScaleX));
            double sourceY = ClampSourceY(SourceRegion.Y + ((canvasY - OffsetY) / ScaleY));
            return new NormalizedPoint(
                Math.Clamp(sourceX / SourceImageSize.Width, 0, 1),
                Math.Clamp(sourceY / SourceImageSize.Height, 0, 1));
        }

        NormalizedFaceLandmarks landmarks = new(
            MapPoint(detection.Landmarks.LeftEye),
            MapPoint(detection.Landmarks.RightEye),
            MapPoint(detection.Landmarks.Nose),
            MapPoint(detection.Landmarks.MouthLeft),
            MapPoint(detection.Landmarks.MouthRight));

        return new DetectedFaceCandidate(
            new NormalizedBoundingBox(
                normalizedLeft,
                normalizedTop,
                normalizedWidth,
                normalizedHeight),
            landmarks,
            detection.Confidence);
    }

    private double ClampSourceX(double value) => Math.Clamp(value, SourceRegion.X, SourceRegion.Right);
    private double ClampSourceY(double value) => Math.Clamp(value, SourceRegion.Y, SourceRegion.Bottom);
}

internal static class YuNetDetectionMerger
{
    public static IReadOnlyList<DetectedFaceCandidate> Merge(
        IEnumerable<DetectedFaceCandidate> detections,
        double iouThreshold,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(detections);
        if (!double.IsFinite(iouThreshold) || iouThreshold < 0 || iouThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iouThreshold), "The merge NMS threshold must be between zero and one.");
        }

        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "TopK must be positive.");
        }

        IndexedDetection[] ordered = detections
            .Select((detection, index) => new IndexedDetection(detection, index))
            .OrderByDescending(value => value.Detection.Confidence)
            .ThenBy(value => value.Detection.BoundingBox.X)
            .ThenBy(value => value.Detection.BoundingBox.Y)
            .ThenBy(value => value.Detection.BoundingBox.Width)
            .ThenBy(value => value.Detection.BoundingBox.Height)
            .ThenBy(value => value.OriginalIndex)
            .Take(topK)
            .ToArray();

        List<DetectedFaceCandidate> retained = [];
        foreach (IndexedDetection value in ordered)
        {
            if (retained.Any(existing =>
                    value.Detection.BoundingBox.IntersectionOverUnion(existing.BoundingBox) >= iouThreshold))
            {
                continue;
            }

            retained.Add(value.Detection);
        }

        return retained;
    }

    private readonly record struct IndexedDetection(
        DetectedFaceCandidate Detection,
        int OriginalIndex);
}
