using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.YuNet;
using Xunit;

namespace PhotoIdentity.Recognition.Tests;

public sealed class YuNetDetectorTests
{
    [Fact]
    public async Task Detector_preprocesses_bgr_input_and_records_metadata()
    {
        ModelManifest manifest = CreateManifest();
        FakeSession session = new(CreateOutputs());
        using YuNetFaceDetector detector = new(
            manifest,
            session,
            new YuNetDetectorOptions { ConfidenceThreshold = 0.8 });
        ImageFrame image = new(
            new ImageSize(1, 1),
            PixelFormat.Bgr24,
            stride: 3,
            [10, 20, 30]);

        YuNetDetectionResult result = await detector.DetectWithMetricsAsync(image);

        Assert.Equal("yunet-test", result.Descriptor.Id.Value);
        Assert.Single(result.Faces);
        Assert.Equal([1L, 3L, 32L, 32L], session.InputShape);
        Assert.Equal(10f, session.InputData[0]);
        Assert.Equal(20f, session.InputData[32 * 32]);
        Assert.Equal(30f, session.InputData[2 * 32 * 32]);
        Assert.True(result.TotalDuration >= TimeSpan.Zero);
    }

    [Fact]
    public void Parser_returns_normalised_boxes_and_reorders_landmarks()
    {
        IReadOnlyList<DetectedFaceCandidate> detections = YuNetOutputParser.Parse(
            CreateOutputs(),
            new ImageSize(32, 32),
            new YuNetDetectorOptions { ConfidenceThreshold = 0.8 });

        DetectedFaceCandidate face = Assert.Single(detections);
        Assert.Equal(0.25, face.BoundingBox.X, 6);
        Assert.Equal(0.25, face.BoundingBox.Y, 6);
        Assert.Equal(0.25, face.BoundingBox.Width, 6);
        Assert.Equal(0.25, face.BoundingBox.Height, 6);
        Assert.Equal(14d / 32d, face.Landmarks.LeftEye.X, 6);
        Assert.Equal(10d / 32d, face.Landmarks.RightEye.X, 6);
        Assert.Equal(14d / 32d, face.Landmarks.MouthLeft.X, 6);
        Assert.Equal(10d / 32d, face.Landmarks.MouthRight.X, 6);
    }

    [Fact]
    public void Parser_rejects_invalid_output_shapes_clearly()
    {
        Dictionary<string, YuNetTensor> outputs = new(CreateOutputs(), StringComparer.Ordinal)
        {
            ["cls_8"] = new YuNetTensor("cls_8", [1, 15, 1], new float[15]),
        };

        YuNetOutputException exception = Assert.Throws<YuNetOutputException>(() =>
            YuNetOutputParser.Parse(
                outputs,
                new ImageSize(32, 32),
                new YuNetDetectorOptions { ConfidenceThreshold = 0.8 }));

        Assert.Contains("cls_8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected [1, 16, 1]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_applies_confidence_threshold()
    {
        IReadOnlyList<DetectedFaceCandidate> detections = YuNetOutputParser.Parse(
            CreateOutputs(classScore: 0.25f),
            new ImageSize(32, 32),
            new YuNetDetectorOptions { ConfidenceThreshold = 0.6 });

        Assert.Empty(detections);
    }

    private static IReadOnlyDictionary<string, YuNetTensor> CreateOutputs(float classScore = 0.81f)
    {
        Dictionary<string, YuNetTensor> outputs = new(StringComparer.Ordinal);
        foreach (int stride in new[] { 8, 16, 32 })
        {
            int columns = 32 / stride;
            int rows = 32 / stride;
            int locations = columns * rows;
            float[] classifications = new float[locations];
            float[] objectness = new float[locations];
            float[] boxes = new float[locations * 4];
            float[] landmarks = new float[locations * 10];

            if (stride == 8)
            {
                int location = 5;
                classifications[location] = classScore;
                objectness[location] = 1;
                int boxOffset = location * 4;
                boxes[boxOffset] = 0.5f;
                boxes[boxOffset + 1] = 0.5f;
                boxes[boxOffset + 2] = 0;
                boxes[boxOffset + 3] = 0;

                int landmarkOffset = location * 10;
                float[] values =
                [
                    0.25f, 0.25f,
                    0.75f, 0.25f,
                    0.5f, 0.5f,
                    0.25f, 0.75f,
                    0.75f, 0.75f,
                ];
                values.CopyTo(landmarks, landmarkOffset);
            }

            outputs.Add($"cls_{stride}", new YuNetTensor($"cls_{stride}", [1, locations, 1], classifications));
            outputs.Add($"obj_{stride}", new YuNetTensor($"obj_{stride}", [1, locations, 1], objectness));
            outputs.Add($"bbox_{stride}", new YuNetTensor($"bbox_{stride}", [1, locations, 4], boxes));
            outputs.Add($"kps_{stride}", new YuNetTensor($"kps_{stride}", [1, locations, 10], landmarks));
        }

        return outputs;
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

    private sealed class FakeSession(IReadOnlyDictionary<string, YuNetTensor> outputs)
        : IYuNetInferenceSession
    {
        public float[] InputData { get; private set; } = [];
        public long[] InputShape { get; private set; } = [];

        public IReadOnlyDictionary<string, YuNetTensor> Run(
            float[] input,
            long[] shape,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputData = (float[])input.Clone();
            InputShape = (long[])shape.Clone();
            return outputs;
        }

        public void Dispose()
        {
        }
    }
}
