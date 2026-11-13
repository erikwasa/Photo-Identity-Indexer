using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceDetectorOnlyRunnerTests
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1];

    [Fact]
    public async Task Detector_only_report_is_directly_accepted_by_existing_cue_deriver()
    {
        string tmp = NewTemp();
        string repo = Path.Combine(tmp, "repo");
        string framesDirectory = Path.Combine(tmp, "frames");
        string reportDirectory = Path.Combine(tmp, "private");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(framesDirectory);
        Directory.CreateDirectory(reportDirectory);
        try
        {
            const long duration = 48_000;
            VideoFaceSamplingPlan plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(duration);
            var entries = plan.FrameTimestampsMs.Select((t, i) =>
                new VideoFaceFrame(t, $"frame-{i + 1:000}.png")).ToList();
            string manifest = Path.Combine(framesDirectory, "frames.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                frames = entries.Select(f => new { timestampMs = f.TimestampMs, file = f.File }),
            }));
            await File.WriteAllTextAsync(Path.Combine(framesDirectory, "wi0155-run.json"),
                """{"schemaVersion":1,"kind":"PhotoIdentity.WI0155.CoverageWorkspace","state":"extracted"}""");
            await File.WriteAllTextAsync(Path.Combine(framesDirectory, "extraction-metadata.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    frameEncoding = "NativeLosslessPng",
                    codedWidth = 128,
                    codedHeight = 128,
                    rotationDegrees = 0,
                    expectedDisplayWidth = 128,
                    expectedDisplayHeight = 128,
                    samplingMode = "Adaptive",
                    samplingClipDurationMs = duration,
                    samplingWindowStartMs = 0L,
                    samplingWindowEndMs = plan.WindowEndMs,
                    samplingCoarseFrameCount = plan.CoarseFrameCount,
                    samplingRefinementBudgetReached = false,
                    samplingHasMoreWindows = false,
                }));

            foreach (VideoFaceFrame entry in entries)
                await File.WriteAllBytesAsync(Path.Combine(framesDirectory, entry.File), Png);

            string[] before = Directory.GetFiles(framesDirectory).Order().ToArray();
            byte[][] original = before.Select(File.ReadAllBytes).ToArray();

            var detector = new SyntheticDetector(entries.Count, faceIndexWithOverflow: 2);
            VideoFaceDetectorOnlyReport review = await VideoFaceDetectorOnlyRunner.EvaluateAsync(
                new VideoFaceFrameSet(1, entries),
                framesDirectory, new SyntheticDecoder(), detector,
                VideoFaceOverflowPolicy.RetainTop32);

            Assert.Equal(entries.Count, review.Aggregate.FramesExamined);
            Assert.Equal(entries.Count, detector.Calls);
            Assert.Equal(32, review.Diagnostics.FrameDetections[2].FacesDetected);
            Assert.Equal(1, review.Diagnostics.FrameDetections[2].CandidatesDiscarded);
            Assert.Equal(33, review.Diagnostics.FrameDetections[2].RawCandidatesDetected);
            Assert.Equal(128, review.Diagnostics.FrameDetections[2].DecodedWidth);
            Assert.True(review.Diagnostics.FrameDetections[2].SmallestFaceWidthPixels > 0);
            Assert.Equal(before, Directory.GetFiles(framesDirectory).Order().ToArray());
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(original[i], File.ReadAllBytes(before[i]));

            string output = Path.Combine(reportDirectory, "coarse-diagnostics.json");
            await VideoFaceDetectorOnlyRunner.WritePrivateReportAsync(
                output, repo, framesDirectory, review);
            string json = await File.ReadAllTextAsync(output);
            Assert.Contains("\"frameDetections\"", json);
            Assert.Contains("\"framesExamined\"", json);
            Assert.DoesNotContain("Embedding", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PersonId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Reference", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(framesDirectory, json, StringComparison.Ordinal);
            Assert.DoesNotContain("frame-001.png", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector", json, StringComparison.OrdinalIgnoreCase);

            var cues = await VideoFaceAdaptiveCueCommand.DeriveFromPrivateFilesAsync(
                manifest, output, repo);
            Assert.NotEmpty(cues);
            Assert.All(cues, cue => Assert.Contains(
                cue.ObservedFrameTimestampMs, plan.FrameTimestampsMs));
            var refined = VideoFaceAdaptiveSamplingPlanner.Refine(plan, cues);
            Assert.True(refined.FrameTimestampsMs.Count > plan.FrameTimestampsMs.Count);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                VideoFaceDetectorOnlyRunner.WritePrivateReportAsync(
                    output, repo, framesDirectory, review));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                VideoFaceDetectorOnlyRunner.WritePrivateReportAsync(
                    Path.Combine(repo, "published.json"), repo, framesDirectory, review));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                VideoFaceDetectorOnlyRunner.WritePrivateReportAsync(
                    Path.Combine(framesDirectory, "coarse.json"), repo, framesDirectory, review));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Detector_overflow_fails_closed_by_default_without_publishing_evidence()
    {
        string tmp = NewTemp();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tmp, "one.png"), Png);
            var frames = new VideoFaceFrameSet(1, [new VideoFaceFrame(1000, "one.png")]);
            var detector = new SyntheticDetector(1, faceIndexWithOverflow: 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                VideoFaceDetectorOnlyRunner.EvaluateAsync(
                    frames, tmp, new SyntheticDecoder(), detector, VideoFaceOverflowPolicy.Fail));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Command_refuses_missing_inputs_duplicate_options_and_wrong_mode()
    {
        // Arguments fail before loading any real model, reading the database
        // or contacting a private video. The CLI never logs private inputs.
        Assert.NotEqual(0, await VideoFaceDetectorOnlyRunner.RunAsync(
            ["--detector-only", "--references", "private-references.json"]));
        Assert.NotEqual(0, await VideoFaceDetectorOnlyRunner.RunAsync(
            ["--detector-only", "--manifest", "frames.json",
             "--private-report", "report.json"]));
        Assert.NotEqual(0, await VideoFaceDetectorOnlyRunner.RunAsync(
            ["--detector-only", "--manifest", "C:\\private\\frames.json",
             "--private-report", "C:\\private\\report.json",
             "--detector-pipeline", "single-pass",
             "--face-overflow-policy", "retain-top-32"]));
        Assert.NotEqual(0, await VideoFaceDetectorOnlyRunner.RunAsync(
            ["--detector-only", "--manifest", "C:\\private\\frames.json",
             "--private-report", "C:\\private\\report.json",
             "--detector-only"]));
    }

    [Fact]
    public async Task Detector_only_preflights_every_selected_input_before_first_decode()
    {
        string tmp = NewTemp();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tmp, "one.png"), Png);
            var decoder = new CountingDecoder();
            var set = new VideoFaceFrameSet(1,
            [
                new VideoFaceFrame(1000, "one.png"),
                new VideoFaceFrame(2000, "missing.png"),
            ]);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                VideoFaceDetectorOnlyRunner.EvaluateAsync(
                    set, tmp, decoder, new SyntheticDetector(2),
                    VideoFaceOverflowPolicy.Fail));
            Assert.Equal(0, decoder.Calls);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    private static string NewTemp()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "wi0155-detector-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SyntheticDecoder : IImageDecoder
    {
        public Task<ImageFrame> DecodeAsync(
            Stream input, DecodeOptions options, CancellationToken cancellationToken)
            => Task.FromResult(new ImageFrame(new ImageSize(128, 128),
                PixelFormat.Bgr24, 384, new byte[128 * 384]));
    }

    private sealed class CountingDecoder : IImageDecoder
    {
        public int Calls { get; private set; }
        public Task<ImageFrame> DecodeAsync(
            Stream input, DecodeOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ImageFrame(new ImageSize(128, 128),
                PixelFormat.Bgr24, 384, new byte[128 * 384]));
        }
    }

    private sealed class SyntheticDetector : IFaceDetector
    {
        private readonly int _overflowFrame;
        private readonly int _frames;
        public int Calls { get; private set; }

        public SyntheticDetector(int frames, int faceIndexWithOverflow = -1)
        {
            _frames = frames;
            _overflowFrame = faceIndexWithOverflow;
        }

        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("synthetic-detector"), ModelRole.FaceDetection,
            ModelFormat.Onnx, new Sha256Digest(new string('a', 64)),
            new ImageSize(640, 640), "synthetic", "test", "test");

        public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
            ImageFrame image, CancellationToken cancellationToken)
        {
            int index = Calls++;
            int count = index == _overflowFrame ? 33 : index % 3 == 0 ? 0 : 1;
            var one = new DetectedFaceCandidate(
                new NormalizedBoundingBox(.2, .2, .5, .5),
                new NormalizedFaceLandmarks(
                    new NormalizedPoint(.6, .4), new NormalizedPoint(.4, .4),
                    new NormalizedPoint(.5, .5), new NormalizedPoint(.6, .6),
                    new NormalizedPoint(.4, .6)), .65);
            return Task.FromResult<IReadOnlyList<DetectedFaceCandidate>>(
                Enumerable.Repeat(one, count).ToArray());
        }
    }
}
