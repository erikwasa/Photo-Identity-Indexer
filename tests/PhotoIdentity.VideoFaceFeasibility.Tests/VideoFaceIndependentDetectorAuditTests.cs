using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceIndependentDetectorAuditTests
{
    private static readonly NormalizedFaceLandmarks Points = new(
        new NormalizedPoint(.25, .25), new NormalizedPoint(.35, .25),
        new NormalizedPoint(.30, .30), new NormalizedPoint(.26, .36),
        new NormalizedPoint(.34, .36));

    private static VideoFaceGroupCrop Crop(
        int frame, int face = 1, double x = .2, double y = .2) =>
        new(frame, frame * 1000L, face, x, y, .2, .2, .8, .03);

    private static DetectedFaceCandidate IndependentFace(
        double confidence, double x = .2, double y = .2) =>
        new(new NormalizedBoundingBox(x, y, .2, .2), Points, confidence);

    private static VideoFaceFrameSet Frames() =>
        new(1, [new(1000, "one.png"), new(2000, "two.png")]);

    private static VideoFaceShadowDbscanReview Shadow()
    {
        var audit = new VideoFaceUnassignedAudit(
            1, 3, 96, 12, 1, [Crop(2, 2, .65, .65)]);
        return new VideoFaceShadowDbscanReview(
            1, "wi0155-video-shadow-dbscan-e018-min2-v1", .18,
            2, 2, 5, 1, 2, 3, 0, 1, 0, 0, 0,
            [
                new VideoFaceShadowDbscanGroup("D001", 2,
                    [Crop(1), Crop(2)], [])
            ])
        {
            NoiseAudit = audit,
        };
    }

    [Fact]
    public void Selection_preserves_source_type_and_verifies_frame_provenance()
    {
        var selected = VideoFaceIndependentDetectorAudit.Select(
            Shadow(), Frames(), 20, 24);
        Assert.Equal(3, selected.Count);
        Assert.Equal(2, selected.Count(s => s.Source == "group"));
        Assert.Single(selected, s => s.Source == "noise");
        Assert.Equal("D001", selected[0].GroupAlias);
        Assert.Equal(1, selected[0].Crop.FrameNumber);
        Assert.Null(selected[^1].GroupAlias);

        var bad = Shadow() with
        {
            Groups =
            [
                new VideoFaceShadowDbscanGroup("D001", 2,
                    [Crop(1) with { TimestampMs = 999 }, Crop(2)], [])
            ],
        };
        Assert.Throws<ArgumentException>(() =>
            VideoFaceIndependentDetectorAudit.Select(bad, Frames(), 20, 24));

        var colliding = Shadow() with { SameFrameCollisions = 1 };
        Assert.Throws<ArgumentException>(() =>
            VideoFaceIndependentDetectorAudit.Select(colliding, Frames(), 20, 24));

        var wrongNoise = Shadow() with
        {
            NoiseAudit = Shadow().NoiseAudit! with { TotalUnassignedObservations = 4 },
        };
        Assert.Throws<ArgumentException>(() =>
            VideoFaceIndependentDetectorAudit.Select(wrongNoise, Frames(), 20, 24));
    }

    [Fact]
    public void Overlap_is_diagnostic_only_and_high_confidence_is_nested()
    {
        var crop = Crop(1);
        Assert.Equal(1d, VideoFaceIndependentDetectorAudit.BestOverlap(
            crop, [IndependentFace(.4)], .3));
        Assert.Equal(0d, VideoFaceIndependentDetectorAudit.BestOverlap(
            crop, [IndependentFace(.4)], .5));
        Assert.Equal(0d, VideoFaceIndependentDetectorAudit.BestOverlap(
            crop, [IndependentFace(.9, .65, .65)], .3));

        var partial = VideoFaceIndependentDetectorAudit.BestOverlap(
            crop, [IndependentFace(.9, .3, .2)], .3);
        Assert.InRange(partial, .33, .34);
    }

    [Fact]
    public async Task Independent_check_reuses_existing_images_and_never_changes_inputs()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"wi0155-consensus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            byte[] first = [137, 80, 78, 71, 13, 10, 26, 10, 1];
            byte[] second = [137, 80, 78, 71, 13, 10, 26, 10, 2];
            await File.WriteAllBytesAsync(Path.Combine(folder, "one.png"), first);
            await File.WriteAllBytesAsync(Path.Combine(folder, "two.png"), second);
            string[] files = Directory.GetFiles(folder).Order().ToArray();
            byte[][] initial = files.Select(File.ReadAllBytes).ToArray();

            IReadOnlyList<VideoFaceIndependentCandidate> selected =
                VideoFaceIndependentDetectorAudit.Select(Shadow(), Frames(), 20, 24);
            var detector = new FakeDetector();
            VideoFaceIndependentAudit report =
                await VideoFaceIndependentDetectorAudit.EvaluateAsync(
                    "V03", new string('a', 64), Frames(), folder,
                    selected, new FakeDecoder(), detector);

            Assert.True(report.FullyAccounted);
            Assert.Equal(2, report.FramesChecked);
            Assert.Equal(2, detector.Invocations);
            Assert.Equal(2, report.GroupCropsChecked);
            Assert.Equal(2, report.GroupCropsAt030);
            Assert.Equal(0, report.GroupCropsAt050);
            Assert.Equal(1, report.NoiseCropsChecked);
            Assert.Equal(0, report.NoiseCropsAt030);
            Assert.Equal(0, report.NoiseCropsAt050);
            Assert.Equal("V03", report.ClipAlias);

            Assert.Equal(files, Directory.GetFiles(folder).Order());
            for (int i = 0; i < files.Length; i++)
                Assert.Equal(initial[i], await File.ReadAllBytesAsync(files[i]));
            string json = JsonSerializer.Serialize(report);
            Assert.DoesNotContain("PersonId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Embedding", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(folder, json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class FakeDecoder : IImageDecoder
    {
        public Task<ImageFrame> DecodeAsync(Stream stream, DecodeOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ImageFrame(
                new ImageSize(128, 128), PixelFormat.Bgr24, 384, new byte[128 * 384]));
    }

    private sealed class FakeDetector : IFaceDetector
    {
        public int Invocations { get; private set; }

        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-independent-detector"), ModelRole.FaceDetection,
            ModelFormat.Onnx, new Sha256Digest(new string('b', 64)),
            new ImageSize(640, 640), "fake", "test", "test");

        public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
            ImageFrame image, CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult<IReadOnlyList<DetectedFaceCandidate>>(
                [IndependentFace(.4)]);
        }
    }
}
