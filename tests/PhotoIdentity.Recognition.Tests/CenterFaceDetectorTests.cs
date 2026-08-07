using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Recognition.Onnx.CenterFace;
using PhotoIdentity.Recognition.Onnx.Models;
using Xunit;

namespace PhotoIdentity.Recognition.Tests;

public sealed class CenterFaceDetectorTests
{
    [Fact]
    public async Task Detector_rounds_dynamic_input_and_converts_bgr_to_rgb()
    {
        ModelManifest manifest = CreateManifest(maximumLongEdge: 64);
        FakeSession session = new(CreateOutputs(new ImageSize(32, 32)));
        using CenterFaceFaceDetector detector = new(
            manifest,
            session,
            new CenterFaceDetectorOptions { ConfidenceThreshold = 0.5 });
        ImageFrame image = new(
            new ImageSize(1, 1),
            PixelFormat.Bgr24,
            stride: 3,
            [10, 20, 30]);

        CenterFaceDetectionResult result = await detector.DetectWithMetricsAsync(image);

        Assert.Equal("centerface-test", result.Descriptor.Id.Value);
        Assert.Equal(new ImageSize(32, 32), result.TensorSize);
        Assert.Equal([1L, 3L, 32L, 32L], session.InputShape);
        Assert.Equal(30f, session.InputData[0]);
        Assert.Equal(20f, session.InputData[32 * 32]);
        Assert.Equal(10f, session.InputData[2 * 32 * 32]);
        Assert.True(result.TotalDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Detector_invokes_inference_session_once_per_image()
    {
        ModelManifest manifest = CreateManifest(maximumLongEdge: 64);
        CountingSession session = new(CreateOutputs(new ImageSize(32, 32)));
        using CenterFaceFaceDetector detector = new(
            manifest,
            session,
            new CenterFaceDetectorOptions { ConfidenceThreshold = 0.5 });
        ImageFrame image = new(
            new ImageSize(1, 1),
            PixelFormat.Bgr24,
            stride: 3,
            [10, 20, 30]);

        await detector.DetectAsync(image, CancellationToken.None);
        await detector.DetectAsync(image, CancellationToken.None);

        Assert.Equal(2, session.RunCount);
    }

    [Fact]
    public void Preprocessor_bounds_long_edge_before_rounding_to_multiple_of_32()
    {
        ModelManifest manifest = CreateManifest(maximumLongEdge: 64);
        byte[] pixels = new byte[100 * 50 * 3];
        ImageFrame image = new(
            new ImageSize(100, 50),
            PixelFormat.Bgr24,
            stride: 300,
            pixels);

        CenterFacePreprocessedInput result = CenterFacePreprocessor.Preprocess(
            image,
            manifest,
            multipleOf: 32,
            maxLongEdge: 64,
            CancellationToken.None);

        Assert.Equal(new ImageSize(64, 32), result.TensorSize);
        Assert.Equal([1L, 3L, 32L, 64L], result.Shape);
    }

    [Fact]
    public void Parser_decodes_box_and_reorders_five_landmarks()
    {
        ImageSize inputSize = new(32, 32);
        IReadOnlyDictionary<string, CenterFaceTensor> outputs = CreateOutputs(
            inputSize,
            firstScore: 0.9f,
            includeFirstDetection: true);

        IReadOnlyList<DetectedFaceCandidate> detections = CenterFaceOutputParser.Parse(
            outputs,
            inputSize,
            new CenterFaceDetectorOptions { ConfidenceThreshold = 0.5 });

        DetectedFaceCandidate face = Assert.Single(detections);
        Assert.Equal(10d / 32d, face.BoundingBox.X, 6);
        Assert.Equal(6d / 32d, face.BoundingBox.Y, 6);
        Assert.Equal(8d / 32d, face.BoundingBox.Width, 6);
        Assert.Equal(8d / 32d, face.BoundingBox.Height, 6);

        Assert.Equal(16d / 32d, face.Landmarks.LeftEye.X, 6);
        Assert.Equal(8d / 32d, face.Landmarks.LeftEye.Y, 6);
        Assert.Equal(12d / 32d, face.Landmarks.RightEye.X, 6);
        Assert.Equal(8d / 32d, face.Landmarks.RightEye.Y, 6);
        Assert.Equal(14d / 32d, face.Landmarks.Nose.X, 6);
        Assert.Equal(10d / 32d, face.Landmarks.Nose.Y, 6);
        Assert.Equal(16d / 32d, face.Landmarks.MouthLeft.X, 6);
        Assert.Equal(12d / 32d, face.Landmarks.MouthLeft.Y, 6);
        Assert.Equal(12d / 32d, face.Landmarks.MouthRight.X, 6);
        Assert.Equal(12d / 32d, face.Landmarks.MouthRight.Y, 6);
    }

    [Fact]
    public void Parser_uses_strict_upstream_confidence_threshold()
    {
        ImageSize inputSize = new(32, 32);
        IReadOnlyList<DetectedFaceCandidate> detections = CenterFaceOutputParser.Parse(
            CreateOutputs(inputSize, firstScore: 0.5f, includeFirstDetection: true),
            inputSize,
            new CenterFaceDetectorOptions { ConfidenceThreshold = 0.5 });

        Assert.Empty(detections);
    }

    [Fact]
    public void Parser_suppresses_overlapping_candidates_deterministically()
    {
        ImageSize inputSize = new(32, 32);
        IReadOnlyDictionary<string, CenterFaceTensor> outputs = CreateOutputs(
            inputSize,
            firstScore: 0.9f,
            includeFirstDetection: true,
            secondScore: 0.8f,
            includeSecondDetection: true);

        IReadOnlyList<DetectedFaceCandidate> detections = CenterFaceOutputParser.Parse(
            outputs,
            inputSize,
            new CenterFaceDetectorOptions
            {
                ConfidenceThreshold = 0.5,
                NmsThreshold = 0.3,
            });

        DetectedFaceCandidate face = Assert.Single(detections);
        Assert.Equal(0.9, face.Confidence, 6);
    }

    [Fact]
    public void Parser_rejects_invalid_output_shapes_clearly()
    {
        ImageSize inputSize = new(32, 32);
        Dictionary<string, CenterFaceTensor> outputs = new(
            CreateOutputs(inputSize),
            StringComparer.Ordinal)
        {
            ["537"] = new CenterFaceTensor("537", [1, 1, 7, 8], new float[56]),
        };

        CenterFaceOutputException exception = Assert.Throws<CenterFaceOutputException>(() =>
            CenterFaceOutputParser.Parse(
                outputs,
                inputSize,
                new CenterFaceDetectorOptions()));

        Assert.Contains("537", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected [1, 1, 8, 8]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenCv_output_reader_preserves_four_dimensional_shape_and_values()
    {
        int[] shape = [1, 2, 2, 3];
        float[] values = Enumerable.Range(0, 12)
            .Select(value => value + 0.25f)
            .ToArray();

        using OpenCvSharp.Mat tensor = OpenCvSharp.Mat.FromPixelData(
            shape,
            OpenCvSharp.MatType.CV_32FC1,
            values);

        CenterFaceTensor result = OpenCvDnnCenterFaceInferenceSession.ReadOutputTensor("test", tensor);

        Assert.Equal([1L, 2L, 2L, 3L], result.Shape);
        Assert.Equal(values, result.Data.ToArray());
    }

    private static IReadOnlyDictionary<string, CenterFaceTensor> CreateOutputs(
        ImageSize inputSize,
        float firstScore = 0,
        bool includeFirstDetection = false,
        float secondScore = 0,
        bool includeSecondDetection = false)
    {
        int rows = inputSize.Height / 4;
        int columns = inputSize.Width / 4;
        float[] heatmap = new float[rows * columns];
        float[] scale = new float[2 * rows * columns];
        float[] offset = new float[2 * rows * columns];
        float[] landmarks = new float[10 * rows * columns];

        if (includeFirstDetection)
        {
            SetCandidate(
                heatmap,
                scale,
                offset,
                landmarks,
                rows,
                columns,
                row: 2,
                column: 3,
                score: firstScore,
                offsetX: 0);
        }

        if (includeSecondDetection)
        {
            SetCandidate(
                heatmap,
                scale,
                offset,
                landmarks,
                rows,
                columns,
                row: 2,
                column: 4,
                score: secondScore,
                offsetX: -1);
        }

        return new Dictionary<string, CenterFaceTensor>(StringComparer.Ordinal)
        {
            ["537"] = new CenterFaceTensor("537", [1, 1, rows, columns], heatmap),
            ["538"] = new CenterFaceTensor("538", [1, 2, rows, columns], scale),
            ["539"] = new CenterFaceTensor("539", [1, 2, rows, columns], offset),
            ["540"] = new CenterFaceTensor("540", [1, 10, rows, columns], landmarks),
        };
    }

    private static void SetCandidate(
        float[] heatmap,
        float[] scale,
        float[] offset,
        float[] landmarks,
        int rows,
        int columns,
        int row,
        int column,
        float score,
        float offsetX)
    {
        int location = (row * columns) + column;
        heatmap[location] = score;

        float logTwo = (float)Math.Log(2);
        SetChannel(scale, 0, row, column, rows, columns, logTwo);
        SetChannel(scale, 1, row, column, rows, columns, logTwo);
        SetChannel(offset, 0, row, column, rows, columns, 0);
        SetChannel(offset, 1, row, column, rows, columns, offsetX);

        float[] points =
        [
            0.25f, 0.25f,
            0.75f, 0.25f,
            0.5f, 0.5f,
            0.25f, 0.75f,
            0.75f, 0.75f,
        ];
        for (int point = 0; point < 5; point++)
        {
            SetChannel(landmarks, point * 2, row, column, rows, columns, points[(point * 2) + 1]);
            SetChannel(landmarks, (point * 2) + 1, row, column, rows, columns, points[point * 2]);
        }
    }

    private static void SetChannel(
        float[] data,
        int channel,
        int row,
        int column,
        int rows,
        int columns,
        float value)
    {
        int index = ((channel * rows) + row) * columns + column;
        data[index] = value;
    }

    private static ModelManifest CreateManifest(int maximumLongEdge) => new()
    {
        SchemaVersion = 1,
        ModelId = "centerface-test",
        Role = "faceDetection",
        Format = "onnx",
        FileName = "centerface-test.onnx",
        DownloadUri = new Uri("https://example.test/centerface-test.onnx"),
        Sha256 = new string('a', 64),
        SizeBytes = 1,
        Runtime = "opencv-dnn",
        SourceVersion = "test@1",
        Input = new ModelInputManifest
        {
            Width = 32,
            Height = 32,
            ColourOrder = "RGB",
            DataType = "float32",
            Normalisation = new ModelNormalisationManifest
            {
                Scale = 1,
                Mean = [0, 0, 0],
            },
            ShapePolicy = new ModelInputShapeManifest
            {
                Kind = "dynamic-multiple-of",
                MultipleOf = 32,
                MaximumLongEdge = maximumLongEdge,
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

    private sealed class FakeSession(IReadOnlyDictionary<string, CenterFaceTensor> outputs)
        : ICenterFaceInferenceSession
    {
        public float[] InputData { get; private set; } = [];
        public long[] InputShape { get; private set; } = [];

        public IReadOnlyDictionary<string, CenterFaceTensor> Run(
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

    private sealed class CountingSession(IReadOnlyDictionary<string, CenterFaceTensor> outputs)
        : ICenterFaceInferenceSession
    {
        public int RunCount { get; private set; }

        public IReadOnlyDictionary<string, CenterFaceTensor> Run(
            float[] input,
            long[] shape,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunCount++;
            return outputs;
        }

        public void Dispose()
        {
        }
    }
}
