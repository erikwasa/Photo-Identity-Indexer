using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceExperimentTests
{
    [Fact]
    public void Manifest_rejects_traversal_duplicates_bad_order_and_bulk_input()
    {
        VideoFaceFrameSetLoader.Validate(new VideoFaceFrameSet(1,
            [new VideoFaceFrame(0, "frame-01.png"), new VideoFaceFrame(1000, "frame-02.jpg")]));

        Assert.Throws<ArgumentException>(() =>
            VideoFaceFrameSetLoader.Validate(new VideoFaceFrameSet(1, [new VideoFaceFrame(0, "../private.png")])));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceFrameSetLoader.Validate(new VideoFaceFrameSet(1,
                [new VideoFaceFrame(0, "a.png"), new VideoFaceFrame(0, "b.png")])));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceFrameSetLoader.Validate(new VideoFaceFrameSet(1,
                [new VideoFaceFrame(0, "a.png"), new VideoFaceFrame(1, "A.png")])));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceFrameSetLoader.Validate(new VideoFaceFrameSet(1,
                Enumerable.Range(0, 13).Select(i => new VideoFaceFrame(i, $"frame-{i}.png")).ToList())));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceFrameSetLoader.Validate(new VideoFaceFrameSet(1,
                [new VideoFaceFrame(600_001, "frame.png")])));
    }

    [Fact]
    public void Adjacent_repeat_pairing_is_one_to_one_and_does_not_label_people()
    {
        EmbeddingVector a = new EmbeddingVector(new float[] { 1, 0, 0 });
        EmbeddingVector b = new EmbeddingVector(new float[] { 0, 1, 0 });
        Assert.Equal(2, VideoFaceExperiment.CountPossibleRepeatPairs([a, b], [a, a, b]));
        Assert.Equal(0, VideoFaceExperiment.CountPossibleRepeatPairs([a], [b]));
        Assert.Throws<ArgumentException>(() => VideoFaceExperiment.CountPossibleRepeatPairs(
            [a], [new EmbeddingVector(new float[] { 1, 0 })]));
    }

    [Fact]
    public async Task Loader_rejects_extra_json_and_oversized_manifest()
    {
        string dir = CreateTempDirectory();
        try
        {
            string manifestPath = Path.Combine(dir, "frames.json");
            await File.WriteAllTextAsync(manifestPath,
                """{"schemaVersion":1,"frames":[{"timestampMs":0,"file":"one.png","unknown":true}]}""");
            await Assert.ThrowsAsync<JsonException>(() =>
                VideoFaceFrameSetLoader.LoadAsync(manifestPath));
            await File.WriteAllTextAsync(manifestPath, new string('x', 16 * 1024 + 1));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoFaceFrameSetLoader.LoadAsync(manifestPath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Offline_pipeline_has_no_file_writes_and_only_emits_aggregate_counts()
    {
        string dir = CreateTempDirectory();
        try
        {
            // Fake decoder avoids native/model dependencies; valid PNG signatures are still required.
            await File.WriteAllBytesAsync(Path.Combine(dir, "one.png"),
                [137, 80, 78, 71, 13, 10, 26, 10, 1]);
            await File.WriteAllBytesAsync(Path.Combine(dir, "two.png"),
                [137, 80, 78, 71, 13, 10, 26, 10, 2]);
            string manifest = Path.Combine(dir, "frames.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                frames = new[]
                {
                    new { timestampMs = 1000, file = "one.png" },
                    new { timestampMs = 3000, file = "two.png" },
                },
            }));
            VideoFaceFrameSet selected = await VideoFaceFrameSetLoader.LoadAsync(manifest);
            string[] beforeFiles = Directory.GetFiles(dir).Order().ToArray();
            byte[][] beforeBytes = beforeFiles.Select(File.ReadAllBytes).ToArray();

            VideoFaceExperiment experiment = new(
                new FakeDecoder(), new FakeDetector(), new FakeAligner(), new FakeEmbedder());
            VideoFaceAggregate result = await experiment.RunAsync(selected, dir);

            Assert.Equal(2, result.FramesExamined);
            Assert.Equal(2, result.FramesWithFaces);
            Assert.Equal(2, result.FacesDetected);
            Assert.Equal(2, result.EmbeddingsCalculated);
            Assert.Equal(1, result.AdjacentPossibleRepeatPairs);
            Assert.Equal(beforeFiles, Directory.GetFiles(dir).Order().ToArray());
            for (int i = 0; i < beforeFiles.Length; i++)
            {
                Assert.Equal(beforeBytes[i], await File.ReadAllBytesAsync(beforeFiles[i]));
            }

            string report = JsonSerializer.Serialize(result);
            Assert.DoesNotContain("one.png", report, StringComparison.Ordinal);
            Assert.DoesNotContain("person", report, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"wi0155-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeDecoder : IImageDecoder
    {
        public Task<ImageFrame> DecodeAsync(
            Stream source, DecodeOptions options, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ImageFrame(
                new ImageSize(128, 128), PixelFormat.Bgr24, 384, new byte[128 * 384]));
        }
    }

    private sealed class FakeDetector : IFaceDetector
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-detector"),
            ModelRole.FaceDetection,
            ModelFormat.Onnx,
            new Sha256Digest(new string('a', 64)),
            new ImageSize(640, 640),
            "fake", "test", "test");

        public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
            ImageFrame image, CancellationToken cancellationToken)
        {
            IReadOnlyList<DetectedFaceCandidate> detections =
            [
                new DetectedFaceCandidate(
                    new NormalizedBoundingBox(0.2, 0.2, 0.5, 0.5),
                    new NormalizedFaceLandmarks(
                        new NormalizedPoint(0.6, 0.4),
                        new NormalizedPoint(0.4, 0.4),
                        new NormalizedPoint(0.5, 0.5),
                        new NormalizedPoint(0.6, 0.6),
                        new NormalizedPoint(0.4, 0.6)),
                    0.98),
            ];
            return Task.FromResult(detections);
        }
    }

    private sealed class FakeAligner : IFaceAligner
    {
        public Task<AlignedFace> AlignAsync(
            ImageFrame image,
            DetectedFaceCandidate detection,
            AlignmentProtocolId protocol,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AlignedFace(image, protocol));
    }

    private sealed class FakeEmbedder : IFaceEmbedder
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-embedder"),
            ModelRole.FaceEmbedding,
            ModelFormat.Onnx,
            new Sha256Digest(new string('b', 64)),
            new ImageSize(112, 112),
            "fake", "test", "test",
            outputDimensions: 3,
            distanceMetric: DistanceMetric.Cosine,
            alignmentProtocol: OpenCvFaceAligner.SFaceFivePointV1);

        public Task<EmbeddingVector> EmbedAsync(
            AlignedFace face, CancellationToken cancellationToken) =>
            Task.FromResult(new EmbeddingVector(new float[] { 1, 0, 0 }));
    }
}
