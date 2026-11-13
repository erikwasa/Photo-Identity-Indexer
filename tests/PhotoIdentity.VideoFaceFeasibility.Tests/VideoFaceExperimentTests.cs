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

    [Theory]
    [InlineData(false, "face-count-limit", "frame-face-limit")]
    [InlineData(true, "face-detection", "invalid-state")]
    public async Task Tiled_like_frame_failure_reports_only_safe_stage_and_category(
        bool throwDuringDetection, string expectedStage, string expectedCategory)
    {
        string dir = CreateTempDirectory();
        try
        {
            byte[] selectedFrame = [137, 80, 78, 71, 13, 10, 26, 10, 1];
            await File.WriteAllBytesAsync(Path.Combine(dir, "one.png"), selectedFrame);
            VideoFaceExperiment experiment = new(
                new FakeDecoder(),
                new FakeDetector(faceCount: 33, throwDuringDetection: throwDuringDetection),
                new FakeAligner(), new FakeEmbedder());
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => experiment.RunAsync(
                    new VideoFaceFrameSet(1, [new VideoFaceFrame(1000, "one.png")]), dir));

            string diagnostic = VideoFaceFailureDiagnostics.Describe(exception, "clip-analysis");
            Assert.Contains($"stage: {expectedStage}", diagnostic);
            Assert.Contains($"category: {expectedCategory}", diagnostic);
            Assert.DoesNotContain("C:\\Private", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-person", diagnostic, StringComparison.Ordinal);
            Assert.Equal(selectedFrame, await File.ReadAllBytesAsync(Path.Combine(dir, "one.png")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Diagnostic_never_echoes_arbitrary_exception_text_or_untrusted_stage()
    {
        Exception exception = new IOException("private path C:\\Private\\secret-person.json");
        string diagnostic = VideoFaceFailureDiagnostics.Describe(exception, "model-verification");
        Assert.Contains("stage: model-verification", diagnostic);
        Assert.Contains("category: file-io", diagnostic);
        Assert.DoesNotContain("Private", diagnostic, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() =>
            VideoFaceFailureDiagnostics.Mark(exception, "C:\\Private\\secret-person.json"));
    }

    [Fact]
    public void Candidate_limiter_is_deterministic_and_never_exceeds_32_embeddings()
    {
        // Distinct confidences make the retained subset check unambiguous.
        IReadOnlyList<DetectedFaceCandidate> proposed = Enumerable.Range(0, 40)
            .Select(i => new DetectedFaceCandidate(
                new NormalizedBoundingBox(0.2, 0.2, 0.5, 0.5),
                new NormalizedFaceLandmarks(
                    new NormalizedPoint(0.6, 0.4),
                    new NormalizedPoint(0.4, 0.4),
                    new NormalizedPoint(0.5, 0.5),
                    new NormalizedPoint(0.6, 0.6),
                    new NormalizedPoint(0.4, 0.6)),
                0.6 + (i * 0.01)))
            .Reverse()
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            VideoFaceCandidateLimiter.Apply(proposed, VideoFaceOverflowPolicy.Fail));

        VideoFaceCandidateSelection selected = VideoFaceCandidateLimiter.Apply(
            proposed, VideoFaceOverflowPolicy.RetainTop32);
        Assert.Equal(40, selected.RawCandidateCount);
        Assert.Equal(8, selected.DiscardedCandidates);
        Assert.Equal(32, selected.Candidates.Count);
        Assert.Equal(0.99, selected.Candidates[0].Confidence, 8);
        Assert.Equal(0.68, selected.Candidates[^1].Confidence, 8);
        Assert.Equal(selected.Candidates.Select(f => f.Confidence),
            VideoFaceCandidateLimiter.Apply(
                proposed, VideoFaceOverflowPolicy.RetainTop32)
                .Candidates.Select(f => f.Confidence));
    }

    [Fact]
    public async Task Explicit_overflow_retains_32_and_records_losses_in_private_report()
    {
        string dir = CreateTempDirectory();
        try
        {
            byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10, 1];
            await File.WriteAllBytesAsync(Path.Combine(dir, "one.png"), bytes);

            var matcher = new VideoFaceCataloguePersonMatcher(
            [
                new VideoFaceCatalogueExemplar(
                    "P01", new string('a', 64),
                    new EmbeddingVector(new float[] { 1, 0, 0 })),
            ]);
            VideoFaceFrameSet frames = new(1,
                [new VideoFaceFrame(1000, "one.png")]);
            var experiment = new VideoFaceExperiment(
                new FakeDecoder(), new FakeDetector(faceCount: 33),
                new FakeAligner(), new FakeEmbedder(),
                overflowPolicy: VideoFaceOverflowPolicy.RetainTop32);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                experiment.RunAsync(frames, dir));

            VideoFacePrivateReview review = await experiment.RunWithPreparedMatcherAsync(
                frames, dir, matcher, detectorConfidence: 0.5);

            Assert.Equal(32, review.Aggregate.FacesDetected);
            Assert.Equal(32, review.Aggregate.EmbeddingsCalculated);
            Assert.Equal(32, review.Findings.Count);
            VideoFaceFrameDetectionDiagnostic diagnostic =
                Assert.Single(review.Diagnostics!.FrameDetections);
            Assert.Equal(33, diagnostic.RawCandidatesDetected);
            Assert.Equal(1, diagnostic.CandidatesDiscarded);
            Assert.Equal(32, diagnostic.FacesDetected);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(dir, "one.png")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Opt_in_shadow_dbscan_preserves_original_grouping_and_source_integrity()
    {
        string dir = CreateTempDirectory();
        try
        {
            byte[] first = [137, 80, 78, 71, 13, 10, 26, 10, 1];
            byte[] second = [137, 80, 78, 71, 13, 10, 26, 10, 2];
            await File.WriteAllBytesAsync(Path.Combine(dir, "one.png"), first);
            await File.WriteAllBytesAsync(Path.Combine(dir, "two.png"), second);

            var matcher = new VideoFaceCataloguePersonMatcher(
            [
                new("P01", new string('a', 64),
                    new EmbeddingVector(new float[] { 1, 0, 0 })),
            ]);
            var manifest = new VideoFaceFrameSet(1,
            [
                new VideoFaceFrame(1000, "one.png"),
                new VideoFaceFrame(2000, "two.png"),
            ]);
            var experiment = new VideoFaceExperiment(
                new FakeDecoder(), new FakeDetector(), new FakeAligner(),
                new FakeEmbedder());

            VideoFacePrivateReview baseline = await experiment.RunWithPreparedMatcherAsync(
                manifest, dir, matcher, detectorConfidence: 0.5,
                provisionalGroups: true, unassignedAudit: true);
            VideoFacePrivateReview shadow = await experiment.RunWithPreparedMatcherAsync(
                manifest, dir, matcher, detectorConfidence: 0.5,
                provisionalGroups: true, unassignedAudit: true,
                shadowDbscan: true);

            Assert.Null(baseline.Diagnostics!.ShadowDbscan);
            Assert.Equal(
                JsonSerializer.Serialize(baseline.Diagnostics.ProvisionalGroups),
                JsonSerializer.Serialize(shadow.Diagnostics!.ProvisionalGroups));
            Assert.Equal(
                JsonSerializer.Serialize(baseline.Diagnostics.GroupSuggestions),
                JsonSerializer.Serialize(shadow.Diagnostics.GroupSuggestions));
            Assert.Equal(
                JsonSerializer.Serialize(baseline.Findings),
                JsonSerializer.Serialize(shadow.Findings));
            Assert.Equal(1, shadow.Diagnostics.ShadowDbscan!.ClusterCount);
            Assert.Equal(2, shadow.Diagnostics.ShadowDbscan.ClusterMembers);
            Assert.Equal(0, shadow.Diagnostics.ShadowDbscan.NoiseObservations);
            Assert.True(shadow.Diagnostics.ShadowDbscan.AllObservationsAccountedFor);
            Assert.Equal("P01", Assert.Single(
                shadow.Diagnostics.ShadowDbscan.Groups).TopCandidates[0].Alias);
            Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(dir, "one.png")));
            Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(dir, "two.png")));

            // The comparator is *not* a fallback for obsolete still-reference
            // experiments and it cannot hide missing baseline/audit evidence.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                experiment.RunWithPreparedMatcherAsync(
                    manifest, dir, matcher, detectorConfidence: 0.5,
                    shadowDbscan: true));
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
        private readonly int _faceCount;
        private readonly bool _throwDuringDetection;

        public FakeDetector(int faceCount = 1, bool throwDuringDetection = false)
        {
            _faceCount = faceCount;
            _throwDuringDetection = throwDuringDetection;
        }

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
            if (_throwDuringDetection)
            {
                throw new InvalidOperationException("private path C:\\Private\\secret-person.json");
            }

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
            return Task.FromResult<IReadOnlyList<DetectedFaceCandidate>>(
                Enumerable.Repeat(detections[0], _faceCount).ToArray());
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
