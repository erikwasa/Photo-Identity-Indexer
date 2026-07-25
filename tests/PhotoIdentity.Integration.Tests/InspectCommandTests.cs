using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class InspectCommandTests
{
    [Fact]
    public void Options_parse_positional_input_and_reproducible_defaults()
    {
        InspectCommandOptions options = InspectCommandOptions.Parse(
            ["family-photo.jpg", "--confidence", "0.8", "--padding", "0.3"]);

        Assert.Equal("family-photo.jpg", options.InputPath);
        Assert.Equal(Path.Combine(".artifacts", "inspect", "family-photo"), options.OutputDirectory);
        Assert.Equal(0.8, options.ConfidenceThreshold);
        Assert.Equal(0.3, options.PaddingRatio);
        Assert.False(options.Overwrite);
    }

    [Fact]
    public async Task Pipeline_writes_visual_and_reproducible_outputs_without_modifying_source()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-inspect-{Guid.NewGuid():N}");
        string inputPath = Path.Combine(directory, "source.png");
        string outputPath = Path.Combine(directory, "output");
        Directory.CreateDirectory(directory);

        try
        {
            ImageFrame source = CreateFrame(160, 160);
            OpenCvPngEncoder encoder = new();
            await using (FileStream stream = File.Create(inputPath))
            {
                await encoder.EncodeAsync(source, stream, CancellationToken.None);
            }

            byte[] sourceBefore = await File.ReadAllBytesAsync(inputPath);
            InspectPipeline pipeline = new(
                new OpenCvImageDecoder(),
                encoder,
                new OpenCvFaceCropper(),
                new OpenCvFaceAligner(),
                new FakeDetector(),
                new FakeEmbedder());

            InspectRunSummary first = await pipeline.RunAsync(
                inputPath,
                outputPath,
                overwrite: false,
                paddingRatio: 0.25,
                CancellationToken.None);

            Assert.True(first.InputUnchanged);
            Assert.Equal(1, first.FaceCount);
            Assert.Equal(160, first.Width);
            Assert.True(File.Exists(Path.Combine(outputPath, "normalised.png")));
            Assert.True(File.Exists(Path.Combine(outputPath, "annotated.svg")));
            Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "timings.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "faces", "face-001", "crop.png")));
            Assert.True(File.Exists(Path.Combine(outputPath, "faces", "face-001", "aligned.png")));
            Assert.True(File.Exists(Path.Combine(outputPath, "faces", "face-001", "embedding.json")));
            Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(inputPath));

            using JsonDocument manifest = JsonDocument.Parse(
                await File.ReadAllBytesAsync(Path.Combine(outputPath, "manifest.json")));
            Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(1, manifest.RootElement.GetProperty("faceCount").GetInt32());
            Assert.True(manifest.RootElement.GetProperty("source").GetProperty("inputUnchanged").GetBoolean());
            Assert.Equal(
                "sface-five-point-v1",
                manifest.RootElement.GetProperty("faces")[0]
                    .GetProperty("aligned")
                    .GetProperty("protocol")
                    .GetString());

            using JsonDocument embedding = JsonDocument.Parse(
                await File.ReadAllBytesAsync(
                    Path.Combine(outputPath, "faces", "face-001", "embedding.json")));
            Assert.Equal(128, embedding.RootElement.GetProperty("dimensions").GetInt32());
            Assert.InRange(embedding.RootElement.GetProperty("l2Norm").GetDouble(), 0.999999, 1.000001);
            Assert.Equal(128, embedding.RootElement.GetProperty("values").GetArrayLength());

            string annotation = await File.ReadAllTextAsync(Path.Combine(outputPath, "annotated.svg"));
            Assert.Contains("data:image/png;base64,", annotation, StringComparison.Ordinal);
            Assert.Contains("face 1", annotation, StringComparison.Ordinal);
            Assert.Contains("LE", annotation, StringComparison.Ordinal);
            Assert.Contains("MR", annotation, StringComparison.Ordinal);

            Dictionary<string, string> stableHashes = StableOutputHashes(outputPath);
            InspectRunSummary second = await pipeline.RunAsync(
                inputPath,
                outputPath,
                overwrite: true,
                paddingRatio: 0.25,
                CancellationToken.None);

            Assert.True(second.InputUnchanged);
            Assert.Equal(stableHashes, StableOutputHashes(outputPath));
            Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(inputPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static Dictionary<string, string> StableOutputHashes(string outputPath)
    {
        string[] relativePaths =
        [
            "normalised.png",
            "annotated.svg",
            "faces/face-001/crop.png",
            "faces/face-001/aligned.png",
            "faces/face-001/embedding.json",
        ];

        return relativePaths.ToDictionary(
            path => path,
            path => Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(outputPath, path.Replace('/', Path.DirectorySeparatorChar))))),
            StringComparer.Ordinal);
    }

    private static ImageFrame CreateFrame(int width, int height)
    {
        int stride = width * 3;
        byte[] data = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 3);
                data[offset] = (byte)((x * 3 + y) % 256);
                data[offset + 1] = (byte)((x + y * 5) % 256);
                data[offset + 2] = (byte)((x * 7 + y * 2) % 256);
            }
        }

        return new ImageFrame(new ImageSize(width, height), PixelFormat.Bgr24, stride, data);
    }

    private sealed class FakeDetector : IInspectDetector
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-yunet"),
            ModelRole.FaceDetection,
            ModelFormat.Onnx,
            new Sha256Digest(new string('a', 64)),
            new ImageSize(640, 640),
            "fake-runtime",
            "Apache-2.0",
            "test");

        public InspectModelReport Model { get; } = CreateModel(
            "fake-yunet",
            "faceDetection",
            "fake-yunet.onnx",
            new string('a', 64),
            640,
            640,
            "BGR",
            null,
            null);

        public Task<InspectDetectionStage> DetectAsync(
            ImageFrame image,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetectedFaceCandidate face = new(
                new NormalizedBoundingBox(0.2, 0.15, 0.6, 0.72),
                new NormalizedFaceLandmarks(
                    LeftEye: new NormalizedPoint(0.62, 0.38),
                    RightEye: new NormalizedPoint(0.38, 0.38),
                    Nose: new NormalizedPoint(0.5, 0.52),
                    MouthLeft: new NormalizedPoint(0.6, 0.68),
                    MouthRight: new NormalizedPoint(0.4, 0.68)),
                confidence: 0.97);

            return Task.FromResult(new InspectDetectionStage(
                Descriptor,
                [face],
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(1)));
        }
    }

    private sealed class FakeEmbedder : IInspectEmbedder
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-sface"),
            ModelRole.FaceEmbedding,
            ModelFormat.Onnx,
            new Sha256Digest(new string('b', 64)),
            new ImageSize(112, 112),
            "fake-runtime",
            "Apache-2.0",
            "test",
            outputDimensions: 128,
            distanceMetric: DistanceMetric.Cosine,
            alignmentProtocol: OpenCvFaceAligner.SFaceFivePointV1);

        public InspectModelReport Model { get; } = CreateModel(
            "fake-sface",
            "faceEmbedding",
            "fake-sface.onnx",
            new string('b', 64),
            112,
            112,
            "RGB",
            128,
            OpenCvFaceAligner.SFaceFivePointV1.Value);

        public Task<InspectEmbeddingStage> EmbedAsync(
            AlignedFace face,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(new ImageSize(112, 112), face.Image.Size);
            Assert.Equal(OpenCvFaceAligner.SFaceFivePointV1, face.Protocol);

            float[] values = Enumerable.Range(1, 128).Select(value => (float)value).ToArray();
            EmbeddingVector embedding = new EmbeddingVector(values).Normalize();
            return Task.FromResult(new InspectEmbeddingStage(
                Descriptor,
                embedding,
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(1)));
        }
    }

    private static InspectModelReport CreateModel(
        string id,
        string role,
        string fileName,
        string hash,
        int width,
        int height,
        string colourOrder,
        int? dimensions,
        string? alignmentProtocol) =>
        new(
            id,
            role,
            "onnx",
            fileName,
            hash,
            1,
            "fake-runtime",
            "test",
            new InspectModelInput(width, height, colourOrder, "float32", 1, [0, 0, 0]),
            new InspectModelOutput(
                role == "faceDetection" ? "detections" : "embedding",
                dimensions,
                role == "faceDetection" ? "none" : "l2-by-adapter",
                role == "faceDetection" ? null : "cosine"),
            alignmentProtocol);
}