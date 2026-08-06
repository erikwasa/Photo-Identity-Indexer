using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.YuNet;
using Xunit;

namespace PhotoIdentity.Recognition.Tests;

public sealed class YuNetMultiScaleTests
{
    [Fact]
    public void Tile_planner_covers_edges_with_deterministic_overlap()
    {
        IReadOnlyList<YuNetSourceRegion> tiles = YuNetTilePlanner.CreateTiles(
            new ImageSize(2500, 1600),
            tileSize: 1000,
            overlap: 0.2);

        Assert.Equal(
            [
                new YuNetSourceRegion(0, 0, 1000, 1000),
                new YuNetSourceRegion(800, 0, 1000, 1000),
                new YuNetSourceRegion(1500, 0, 1000, 1000),
                new YuNetSourceRegion(0, 600, 1000, 1000),
                new YuNetSourceRegion(800, 600, 1000, 1000),
                new YuNetSourceRegion(1500, 600, 1000, 1000),
            ],
            tiles);
    }

    [Fact]
    public void Tile_planner_omits_a_tile_that_would_duplicate_the_full_image_pass()
    {
        IReadOnlyList<YuNetSourceRegion> tiles = YuNetTilePlanner.CreateTiles(
            new ImageSize(800, 600),
            tileSize: 1024,
            overlap: 0.2);

        Assert.Empty(tiles);
    }

    [Fact]
    public void Preprocessor_letterboxes_without_stretching_the_source_region()
    {
        ModelManifest manifest = CreateManifest();
        ImageFrame image = new(
            new ImageSize(2, 1),
            PixelFormat.Bgr24,
            stride: 6,
            [10, 20, 30, 10, 20, 30]);

        YuNetPreprocessedInput input = YuNetPreprocessor.Preprocess(
            image,
            manifest,
            YuNetSourceRegion.FullImage(image.Size),
            preserveAspectRatio: true,
            CancellationToken.None);

        Assert.Equal(16, input.Transform.ScaleX, 6);
        Assert.Equal(16, input.Transform.ScaleY, 6);
        Assert.Equal(0, input.Transform.OffsetX, 6);
        Assert.Equal(8, input.Transform.OffsetY, 6);
        Assert.Equal(0f, input.Data[0]);
        Assert.Equal(10f, input.Data[8 * 32]);
        Assert.Equal(20f, input.Data[(32 * 32) + (8 * 32)]);
        Assert.Equal(30f, input.Data[(2 * 32 * 32) + (8 * 32)]);
    }

    [Fact]
    public void Transform_maps_tile_boxes_and_landmarks_to_original_normalised_coordinates()
    {
        YuNetPreprocessingTransform transform = new(
            new ImageSize(2000, 1000),
            new YuNetSourceRegion(1000, 100, 800, 400),
            new ImageSize(320, 320),
            ScaleX: 0.4,
            ScaleY: 0.4,
            OffsetX: 0,
            OffsetY: 80);
        NormalizedPoint centre = new(0.5, 0.5);
        DetectedFaceCandidate detection = new(
            new NormalizedBoundingBox(0.25, 0.25, 0.5, 0.5),
            new NormalizedFaceLandmarks(centre, centre, centre, centre, centre),
            confidence: 0.9);

        DetectedFaceCandidate mapped = Assert.IsType<DetectedFaceCandidate>(
            transform.MapToSource(detection));

        Assert.Equal(0.6, mapped.BoundingBox.X, 6);
        Assert.Equal(0.1, mapped.BoundingBox.Y, 6);
        Assert.Equal(0.2, mapped.BoundingBox.Width, 6);
        Assert.Equal(0.4, mapped.BoundingBox.Height, 6);
        Assert.Equal(0.7, mapped.Landmarks.Nose.X, 6);
        Assert.Equal(0.3, mapped.Landmarks.Nose.Y, 6);
    }

    [Fact]
    public void Global_merge_suppresses_cross_pass_duplicates_and_orders_ties_deterministically()
    {
        DetectedFaceCandidate left = Detection(0.1, 0.1, 0.2, 0.2, confidence: 0.9);
        DetectedFaceCandidate duplicate = Detection(0.11, 0.11, 0.2, 0.2, confidence: 0.8);
        DetectedFaceCandidate right = Detection(0.7, 0.1, 0.2, 0.2, confidence: 0.9);

        IReadOnlyList<DetectedFaceCandidate> merged = YuNetDetectionMerger.Merge(
            [right, duplicate, left],
            iouThreshold: 0.3,
            topK: 100);

        Assert.Equal(2, merged.Count);
        Assert.Same(left, merged[0]);
        Assert.Same(right, merged[1]);
    }

    private static DetectedFaceCandidate Detection(
        double x,
        double y,
        double width,
        double height,
        double confidence)
    {
        NormalizedPoint centre = new(x + (width / 2), y + (height / 2));
        return new DetectedFaceCandidate(
            new NormalizedBoundingBox(x, y, width, height),
            new NormalizedFaceLandmarks(centre, centre, centre, centre, centre),
            confidence);
    }

    private static ModelManifest CreateManifest() => new()
    {
        SchemaVersion = 1,
        ModelId = "yunet-test",
        Role = "faceDetection",
        Format = "onnx",
        FileName = "yunet-test.onnx",
        DownloadUri = new Uri("https://example.test/yunet-test.onnx"),
        Sha256 = new string('a', 64),
        SizeBytes = 1,
        Runtime = "onnxruntime",
        SourceVersion = "test@1",
        Input = new ModelInputManifest
        {
            Width = 32,
            Height = 32,
            ColourOrder = "BGR",
            DataType = "float32",
            Normalisation = new ModelNormalisationManifest
            {
                Scale = 1,
                Mean = [0, 0, 0],
            },
        },
        Output = new ModelOutputManifest
        {
            Kind = "detections",
            Dimensions = null,
            Normalisation = "none",
            DistanceMetric = null,
            Semantics = "test detections",
        },
        AlignmentProtocol = null,
        Licences = new ModelLicenceManifest
        {
            Code = new LicenceRecord
            {
                Spdx = "MIT",
                Source = new Uri("https://example.test/code-license"),
            },
            Weights = new LicenceRecord
            {
                Spdx = "MIT",
                Source = new Uri("https://example.test/weights-license"),
            },
            TrainingData = new TrainingDataRecord
            {
                Name = "test",
                Licence = "test",
                Notes = "test",
            },
        },
    };
}
