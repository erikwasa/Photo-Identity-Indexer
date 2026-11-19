using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>Fully synthetic native pixels, detector and embeddings. No video/model files or DB.</summary>
public sealed class VideoFaceNativeFrameTests
{
    private static ModelDescriptor DetectorDescriptor => new(
        new ModelId("yunet-2023mar-fp32"), ModelRole.FaceDetection,
        ModelFormat.Onnx,
        new Sha256Digest("8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4"),
        new ImageSize(640, 640), "onnxruntime", "MIT", "synthetic");

    private static ModelDescriptor EmbedderDescriptor => new(
        new ModelId("sface-2021dec-fp32"), ModelRole.FaceEmbedding,
        ModelFormat.Onnx,
        new Sha256Digest("0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79"),
        new ImageSize(112, 112), "onnxruntime", "Apache-2.0", "synthetic",
        outputDimensions: 128, distanceMetric: DistanceMetric.Cosine,
        alignmentProtocol: new AlignmentProtocolId("sface-five-point-v1"));

    private static ImageFrame Frame(int width = 1600, int height = 900) =>
        new(new ImageSize(width, height), PixelFormat.Bgr24,
            width * 3, new byte[width * height * 3]);

    private static DetectedFaceCandidate Face() =>
        new(new NormalizedBoundingBox(.15, .2, .04, .08),
            new NormalizedFaceLandmarks(
                new NormalizedPoint(.16, .22), new NormalizedPoint(.18, .22),
                new NormalizedPoint(.17, .24),
                new NormalizedPoint(.16, .26), new NormalizedPoint(.18, .26)), .99);

    [Fact]
    public void Native_frame_profile_never_downscales_or_upsamples_and_preserves_rotation()
    {
        string root = Path.GetFullPath(Path.GetTempPath());
        string staged = Path.Combine(root, "verified-input.mp4");
        IReadOnlyList<string> args = VideoFaceNativeFrameProfile.BuildArguments(
            staged, root, new VideoFrameSample(1250, null), 2500);
        Assert.Contains("-autorotate", args);
        Assert.Contains("1.250", args);
        Assert.Equal("format=yuv420p", args[args.ToList().IndexOf("-vf") + 1]);
        Assert.DoesNotContain(args, s => s.Contains("scale=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, s => s.Contains("pad=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, s => s.Contains("crop=", StringComparison.Ordinal));
        Assert.DoesNotContain("-s", args);
        Assert.Equal(Path.Combine(root, VideoFaceNativeFrameProfile.OutputName), args[^1]);
        Assert.True(args.ToList().IndexOf("-i") < args.ToList().IndexOf("-ss"));
        Assert.Contains("-protocol_whitelist", args);
        Assert.Equal(1, args.Count(a => a == "-i"));

        foreach (long invalid in new long[] { -1, 2500, 3000 })
            Assert.Throws<ArgumentException>(() =>
                VideoFaceNativeFrameProfile.BuildArguments(
                    staged, root, new VideoFrameSample(invalid, null), 2500));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceNativeFrameProfile.BuildArguments(
                "relative.mov", root, new VideoFrameSample(1000, null), 2500));

        VideoFaceNativeFrameProfile.ValidateDecodedImage(Frame(1600, 900));
        Assert.Throws<InvalidDataException>(() =>
            VideoFaceNativeFrameProfile.ValidateDecodedImage(Frame(3841, 1)));
        Assert.Throws<InvalidDataException>(() =>
            VideoFaceNativeFrameProfile.ValidateDecodedImage(
                new ImageFrame(new ImageSize(1, 1), PixelFormat.Rgb24, 3,
                    new byte[3])));
    }

    [Fact]
    public async Task Native_YuNet_SFace_observation_uses_original_dimensions_and_independent_faces()
    {
        var detector = new FakeDetector([Face(), Face()]);
        var embedder = new FakeEmbedder();
        var aligner = new FakeAligner();
        var analyzer = new VideoFaceNativeFrameAnalyzer(detector, aligner, embedder);

        IReadOnlyList<PhotoIdentity.Persistence.Postgres.VideoFaceEmbeddingObservation> found =
            await analyzer.AnalyzeAsync(Frame(1600, 900), 1, 1250);

        Assert.Equal(2, found.Count);
        Assert.Equal([0, 1], found.Select(f => f.FaceOrdinal).ToArray());
        Assert.All(found, f =>
        {
            Assert.Equal(1600, f.FrameWidth);
            Assert.Equal(900, f.FrameHeight);
            Assert.Equal(1250, f.TimestampMs);
            Assert.Equal(1, f.FrameOrdinal);
            Assert.Equal(.04, f.Width);
            Assert.Equal(128, f.Embedding.Dimensions);
        });
        Assert.Equal(2, aligner.Calls);
        Assert.Equal(2, embedder.Calls);
    }

    [Fact]
    public async Task Oversized_detection_batch_and_wrong_model_fail_before_inference()
    {
        var huge = new VideoFaceNativeFrameAnalyzer(
            new FakeDetector(Enumerable.Repeat(Face(), 33).ToArray()),
            new FakeAligner(), new FakeEmbedder());
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            huge.AnalyzeAsync(Frame(64, 64), 0, 1000));

        var wrong = new VideoFaceNativeFrameAnalyzer(
            new FakeDetector([Face()]),
            new FakeAligner(), new FakeEmbedder(
                new ModelDescriptor(new ModelId("sface-2021dec-fp32"),
                    ModelRole.FaceEmbedding, ModelFormat.Onnx,
                    new Sha256Digest(new string('a', 64)),
                    new ImageSize(112, 112), "onnxruntime", "Apache-2.0", "synthetic",
                    outputDimensions: 128, distanceMetric: DistanceMetric.Cosine,
                    alignmentProtocol: new AlignmentProtocolId("sface-five-point-v1"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wrong.AnalyzeAsync(Frame(64, 64), 0, 1000));

        var normal = new VideoFaceNativeFrameAnalyzer(
            new FakeDetector([]), new FakeAligner(), new FakeEmbedder());
        Assert.Empty(await normal.AnalyzeAsync(Frame(64, 64), 0, 1000));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            normal.AnalyzeAsync(Frame(64, 64), 4, 1000));
    }

    [Fact]
    public async Task CLIP_frame_evidence_lease_cannot_authorize_native_face_processing()
    {
        // Refusal occurs before opening a real DB, original or model file.
        await using PostgresCatalogueDatabase database = new(
            "Host=localhost;Database=unreachable_synthetic;Username=synthetic;Password=synthetic");
        string root = Path.GetFullPath(Path.GetTempPath());
        var producer = new VideoFaceNativeEvidenceProducer(database,
            new VideoFaceNativeProducerOptions(
                root, Path.Combine(root, "not-ffmpeg.exe"),
                new string('a', 64), root, root));
        var leased = new VideoProcessingJobLease(
            Guid.NewGuid(), AssetRevisionId.New(), Guid.NewGuid(),
            "FrameEvidence", VideoFrameSamplingPlanner.PolicyVersion,
            VideoFaceNativeFrameProfile.Version, new string('a', 64),
            1000, 100000000, 2, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            producer.ProduceAsync(leased));
    }

    private sealed class FakeDetector(IReadOnlyList<DetectedFaceCandidate> faces)
        : IFaceDetector
    {
        public ModelDescriptor Descriptor => DetectorDescriptor;
        public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
            ImageFrame image, CancellationToken token) =>
            Task.FromResult(faces);
    }

    private sealed class FakeAligner : IFaceAligner
    {
        public int Calls { get; private set; }
        public Task<AlignedFace> AlignAsync(ImageFrame frame, DetectedFaceCandidate face,
            AlignmentProtocolId protocol, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new AlignedFace(frame, protocol));
        }
    }

    private sealed class FakeEmbedder(ModelDescriptor? descriptor = null)
        : IFaceEmbedder
    {
        public int Calls { get; private set; }
        public ModelDescriptor Descriptor => descriptor ?? EmbedderDescriptor;
        public Task<EmbeddingVector> EmbedAsync(AlignedFace face, CancellationToken token)
        {
            Calls++;
            float[] data = new float[128];
            data[0] = 1f;
            return Task.FromResult(new EmbeddingVector(data));
        }
    }
}
