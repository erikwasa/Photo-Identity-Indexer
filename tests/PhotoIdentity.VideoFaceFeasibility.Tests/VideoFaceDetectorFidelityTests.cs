using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.YuNet;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceDetectorFidelityTests
{
    [Theory]
    [InlineData(null, null, YuNetDetectorPipelineMode.SinglePass, 1024)]
    [InlineData("single-pass", null, YuNetDetectorPipelineMode.SinglePass, 1024)]
    [InlineData("multi-scale", null, YuNetDetectorPipelineMode.MultiScale, 1024)]
    [InlineData("multi-scale", "640", YuNetDetectorPipelineMode.MultiScale, 640)]
    public void Opt_in_pipeline_selection_preserves_baseline_and_bounded_tiles(
        string? pipeline, string? tile, YuNetDetectorPipelineMode expectedMode, int expectedTile)
    {
        VideoFaceDetectorSelection selected = VideoFaceDetectorSelection.Parse(pipeline, tile);
        Assert.Equal(expectedMode, selected.PipelineMode);
        Assert.Equal(expectedTile, selected.TileSize);
        YuNetDetectorOptions options = selected.CreateOptions(0.5);
        Assert.Equal(0.5, options.ConfidenceThreshold);
        Assert.Equal(expectedMode, options.PipelineMode);
        Assert.Equal(expectedTile, options.TileSize);
        VideoFaceDetectorProfile privateProfile = selected.Describe(new ImageSize(640, 640));
        Assert.Equal(640, privateProfile.ModelInputWidth);
        Assert.Equal(640, privateProfile.ModelInputHeight);
        Assert.Equal(expectedMode == YuNetDetectorPipelineMode.MultiScale
            ? (int?)expectedTile : null, privateProfile.TileSize);
    }

    [Theory]
    [InlineData("multi-scale", "320")]
    [InlineData("multi-scale", "-1")]
    [InlineData("single-pass", "640")]
    [InlineData("incorrect", null)]
    [InlineData(null, "1024")]
    public void Rejects_unbounded_or_implicit_pipeline_settings(string? pipeline, string? tile)
    {
        Assert.Throws<ArgumentException>(() => VideoFaceDetectorSelection.Parse(pipeline, tile));
    }

    [Fact]
    public void Private_diagnostic_distinguishes_decoded_resolution_and_face_pixels()
    {
        var detection = new DetectedFaceCandidate(
            new NormalizedBoundingBox(0.2, 0.1, 0.1, 0.08),
            new NormalizedFaceLandmarks(
                new NormalizedPoint(0.21, 0.12),
                new NormalizedPoint(0.28, 0.12),
                new NormalizedPoint(0.25, 0.14),
                new NormalizedPoint(0.22, 0.16),
                new NormalizedPoint(0.27, 0.16)),
            0.95);
        VideoFaceFrameDetectionDiagnostic result =
            VideoFaceSensitivityDiagnostics.DescribeFrame(
                2, 1000, [detection], new ImageSize(1080, 1920));
        Assert.Equal(1080, result.DecodedWidth);
        Assert.Equal(1920, result.DecodedHeight);
        Assert.Equal(108.0, result.SmallestFaceWidthPixels);
        Assert.Equal(153.6, result.SmallestFaceHeightPixels);
        VideoFaceFrameDetectionDiagnostic empty =
            VideoFaceSensitivityDiagnostics.DescribeFrame(
                3, 2000, [], new ImageSize(1080, 1920));
        Assert.Equal(1080, empty.DecodedWidth);
        Assert.Null(empty.SmallestFaceWidthPixels);
    }

    [Fact]
    public void Native_lossless_sidecar_enforces_orientation_and_decoder_fidelity()
    {
        string folder = Path.Combine(Path.GetTempPath(), "wi0155-fidelity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Null(VideoFaceExtractionFidelity.ExpectedNativeDimensions(folder));
            string path = Path.Combine(folder, "extraction-metadata.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                frameEncoding = "NativeLosslessPng",
                codedWidth = 1920,
                codedHeight = 1080,
                rotationDegrees = -90,
                expectedDisplayWidth = 1080,
                expectedDisplayHeight = 1920,
            }));
            ImageSize expected = Assert.IsType<ImageSize>(
                VideoFaceExtractionFidelity.ExpectedNativeDimensions(folder));
            Assert.Equal(new ImageSize(1080, 1920), expected);
            VideoFaceExtractionFidelity.VerifyDecodedSize(expected, expected);
            Assert.Throws<InvalidOperationException>(() =>
                VideoFaceExtractionFidelity.VerifyDecodedSize(
                    new ImageSize(900, 1600), expected));

            // The sidecar itself must not be allowed to claim an upscaled native frame.
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                frameEncoding = "NativeLosslessPng",
                codedWidth = 1920,
                codedHeight = 1080,
                rotationDegrees = -90,
                expectedDisplayWidth = 2160,
                expectedDisplayHeight = 3840,
            }));
            Assert.Throws<ArgumentException>(() =>
                VideoFaceExtractionFidelity.ExpectedNativeDimensions(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Cli_rejects_multiscale_without_explicit_private_diagnostics()
    {
        Assert.Equal(2, await Program.Main(
            ["--manifest", "not-found.json", "--detector-pipeline", "multi-scale"]));
        Assert.Equal(2, await Program.Main(
            ["--manifest", "not-found.json", "--tile-size", "640"]));
        Assert.Equal(2, await Program.Main(
            ["--manifest", "not-found.json", "--references", "refs.json",
             "--private-report", "new.json", "--diagnostics",
             "--detector-pipeline", "multi-scale", "--tile-size", "320"]));
    }
    [Fact]
    public void Reconstructed_geometry_preserves_post_top32_face_ordinals_and_checks_evidence()
    {
        NormalizedFaceLandmarks landmarks = new(
            new NormalizedPoint(0.22, 0.32),
            new NormalizedPoint(0.28, 0.32),
            new NormalizedPoint(0.25, 0.36),
            new NormalizedPoint(0.22, 0.40),
            new NormalizedPoint(0.28, 0.40));
        DetectedFaceCandidate[] candidates = Enumerable.Range(0, 33)
            .Select(i => new DetectedFaceCandidate(
                new NormalizedBoundingBox(0.1 + i * 0.001, 0.2, 0.2, 0.3),
                landmarks,
                0.5 + i * 0.01))
            .ToArray();

        VideoFaceFrameDetectionDiagnostic expected = new(1, 1000, 32, 0.51, 0.82)
        {
            RawCandidatesDetected = 33,
            CandidatesDiscarded = 1,
        };

        IReadOnlyList<VideoFaceReviewBox> result =
            VideoFaceReviewGeometryConsistency.Reconstruct(candidates, expected);

        Assert.Equal(32, result.Count);
        Assert.Equal(1, result[0].FaceNumber);
        Assert.Equal(32, result[^1].FaceNumber);
        Assert.Equal(0.132, result[0].X, 3);
        Assert.Equal(0.101, result[^1].X, 3);
        Assert.Throws<InvalidOperationException>(() =>
            VideoFaceReviewGeometryConsistency.Reconstruct(
                candidates, expected with { CandidatesDiscarded = 2 }));
        Assert.Throws<InvalidOperationException>(() =>
            VideoFaceReviewGeometryConsistency.Reconstruct(
                candidates, expected with { LowestConfidence = 0.1 }));
    }

}
