using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class PrivateReferenceReviewTests
{
    [Fact]
    public void Reference_selection_rejects_names_duplicates_traversal_and_unbounded_inputs()
    {
        VideoFaceReferenceSetLoader.Validate(new VideoFaceReferenceSet(
            1, [new VideoFaceReference("R01", "reference-a.png")]));

        Assert.Throws<ArgumentException>(() => VideoFaceReferenceSetLoader.Validate(
            new VideoFaceReferenceSet(1, [new VideoFaceReference("Alice", "a.png")])));
        Assert.Throws<ArgumentException>(() => VideoFaceReferenceSetLoader.Validate(
            new VideoFaceReferenceSet(1, [new VideoFaceReference("R00", "a.png")])));
        Assert.Throws<ArgumentException>(() => VideoFaceReferenceSetLoader.Validate(
            new VideoFaceReferenceSet(1, [new VideoFaceReference("R01", "../a.png")])));
        Assert.Throws<ArgumentException>(() => VideoFaceReferenceSetLoader.Validate(
            new VideoFaceReferenceSet(1,
                [new VideoFaceReference("R01", "a.png"), new VideoFaceReference("R01", "b.png")])));
        Assert.Throws<ArgumentException>(() => VideoFaceReferenceSetLoader.Validate(
            new VideoFaceReferenceSet(1,
                Enumerable.Range(1, 7).Select(i => new VideoFaceReference($"R{i:00}", $"a{i}.png")).ToList())));
    }

    [Fact]
    public void Matcher_returns_uncertainty_and_never_assigns_canonical_id()
    {
        EmbeddingVector first = new(new float[] { 1, 0, 0 });
        EmbeddingVector second = new(new float[] { 0, 1, 0 });
        VideoFaceReferenceMatcher matcher = new(
            [new VideoFaceReferenceEmbedding("R02", second),
             new VideoFaceReferenceEmbedding("R01", first)]);

        VideoFaceReferenceFinding winner = matcher.FindCandidate(1, 1500, 1, first);
        Assert.Equal("R01", winner.CandidateAlias);
        Assert.Equal("review-candidate", winner.ReviewStatus);
        Assert.Equal(1, winner.Similarity);
        Assert.Equal(0, winner.RunnerUpSimilarity);

        VideoFaceReferenceFinding below = matcher.FindCandidate(
            2, 2000, 1, new EmbeddingVector(new float[] { 1, 1, 0 }).Normalize());
        Assert.Null(below.CandidateAlias);
        Assert.Equal("below-exploratory-threshold", below.ReviewStatus);

        VideoFaceReferenceMatcher one = new(
            [new VideoFaceReferenceEmbedding("R01", first)]);
        Assert.Equal("single-reference-uncertain",
            one.FindCandidate(1, 1000, 1, first).ReviewStatus);

        VideoFaceReferenceMatcher ambiguous = new(
            [new VideoFaceReferenceEmbedding("R01", first),
             new VideoFaceReferenceEmbedding("R02",
                new EmbeddingVector(new float[] { 0.99f, 0.141f, 0 }).Normalize())]);
        Assert.Equal("ambiguous",
            ambiguous.FindCandidate(1, 1000, 1, first).ReviewStatus);
    }

    [Fact]
    public async Task End_to_end_fake_models_compare_disjoint_approved_sources_without_writes()
    {
        string root = TempDir();
        try
        {
            string frames = Directory.CreateDirectory(Path.Combine(root, "frames")).FullName;
            string refs = Directory.CreateDirectory(Path.Combine(root, "refs")).FullName;
            string reviews = Directory.CreateDirectory(Path.Combine(root, "reviews")).FullName;
            string repo = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;
            await WritePngStub(frames, "frame-one.png", 1);
            await WritePngStub(frames, "frame-two.png", 2);
            await WritePngStub(refs, "ref-one.png", 3);
            await WritePngStub(refs, "ref-two.png", 4);

            VideoFaceFrameSet selection = new(1,
                [new VideoFaceFrame(1000, "frame-one.png"),
                 new VideoFaceFrame(3000, "frame-two.png")]);
            VideoFaceReferenceSet referenceSet = new(1,
                [new VideoFaceReference("R01", "ref-one.png"),
                 new VideoFaceReference("R02", "ref-two.png")]);
            string[] before = Directory.GetFiles(frames).Concat(Directory.GetFiles(refs))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            byte[][] contents = before.Select(File.ReadAllBytes).ToArray();

            VideoFaceExperiment experiment = new(
                new MarkedDecoder(), new OneFaceDetector(), new PassAligner(), new MarkedEmbedder());
            VideoFacePrivateReview evaluated = await experiment.RunWithReferencesAsync(
                selection, frames, referenceSet, refs);

            Assert.Equal(2, evaluated.ReferenceCount);
            Assert.Equal("fake-detector", evaluated.DetectorModelId);
            Assert.Equal(new string('a', 64), evaluated.DetectorSha256);
            Assert.Equal("fake-embedder", evaluated.EmbedderModelId);
            Assert.Equal(new string('b', 64), evaluated.EmbedderSha256);
            Assert.True(evaluated.Aggregate.ElapsedMilliseconds >= 0);
            Assert.Equal(2, evaluated.Aggregate.FramesExamined);
            Assert.Equal(2, evaluated.Aggregate.EmbeddingsCalculated);
            Assert.Equal(0, evaluated.Aggregate.AdjacentPossibleRepeatPairs);
            Assert.Collection(evaluated.Findings,
                finding =>
                {
                    Assert.Equal(1000, finding.TimestampMs);
                    Assert.Equal("R01", finding.CandidateAlias);
                    Assert.Equal("review-candidate", finding.ReviewStatus);
                },
                finding =>
                {
                    Assert.Equal(3000, finding.TimestampMs);
                    Assert.Equal("R02", finding.CandidateAlias);
                    Assert.Equal("review-candidate", finding.ReviewStatus);
                });

            string outputPath = Path.Combine(reviews, "private-result.json");
            await VideoFacePrivateReportWriter.WriteAsync(
                outputPath, repo, frames, refs, evaluated);

            using JsonDocument report = JsonDocument.Parse(await File.ReadAllBytesAsync(outputPath));
            Assert.Equal("R01",
                report.RootElement.GetProperty("findings")[0].GetProperty("candidateAlias").GetString());
            string json = await File.ReadAllTextAsync(outputPath);
            Assert.DoesNotContain("ref-one.png", json, StringComparison.Ordinal);
            Assert.DoesNotContain("frame-one.png", json, StringComparison.Ordinal);
            Assert.DoesNotContain(frames, json, StringComparison.Ordinal);
            Assert.DoesNotContain(refs, json, StringComparison.Ordinal);

            // FileMode.CreateNew must not silently replace an operator's review file.
            await Assert.ThrowsAsync<ArgumentException>(() => VideoFacePrivateReportWriter.WriteAsync(
                outputPath, repo, frames, refs, evaluated));
            await Assert.ThrowsAsync<ArgumentException>(() => VideoFacePrivateReportWriter.WriteAsync(
                Path.Combine(repo, "leak.json"), repo, frames, refs, evaluated));
            await Assert.ThrowsAsync<ArgumentException>(() => VideoFacePrivateReportWriter.WriteAsync(
                Path.Combine(frames, "leak.json"), repo, frames, refs, evaluated));
            await Assert.ThrowsAsync<ArgumentException>(() => VideoFacePrivateReportWriter.WriteAsync(
                Path.Combine(refs, "leak.json"), repo, frames, refs, evaluated));

            Assert.Equal(before, Directory.GetFiles(frames).Concat(Directory.GetFiles(refs))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray());
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(contents[i], await File.ReadAllBytesAsync(before[i]));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Identical_reference_and_frame_are_rejected_before_recognition()
    {
        string root = TempDir();
        try
        {
            string frames = Directory.CreateDirectory(Path.Combine(root, "frames")).FullName;
            string refs = Directory.CreateDirectory(Path.Combine(root, "refs")).FullName;
            byte[] bytes = await WritePngStub(frames, "frame.png", 1);
            await File.WriteAllBytesAsync(Path.Combine(refs, "copy.png"), bytes);
            VideoFaceExperiment experiment = new(
                new MarkedDecoder(), new OneFaceDetector(), new PassAligner(), new MarkedEmbedder());
            await Assert.ThrowsAsync<ArgumentException>(() => experiment.RunWithReferencesAsync(
                new VideoFaceFrameSet(1, [new VideoFaceFrame(0, "frame.png")]),
                frames,
                new VideoFaceReferenceSet(1, [new VideoFaceReference("R01", "copy.png")]),
                refs));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Byte_identical_references_are_rejected_before_frame_evaluation()
    {
        string root = TempDir();
        try
        {
            string frames = Directory.CreateDirectory(Path.Combine(root, "frames")).FullName;
            string refs = Directory.CreateDirectory(Path.Combine(root, "refs")).FullName;
            await WritePngStub(frames, "frame.png", 1);
            byte[] copy = await WritePngStub(refs, "one.png", 3);
            await File.WriteAllBytesAsync(Path.Combine(refs, "two.png"), copy);

            VideoFaceExperiment experiment = new(
                new MarkedDecoder(), new OneFaceDetector(), new PassAligner(), new MarkedEmbedder());
            await Assert.ThrowsAsync<ArgumentException>(() => experiment.RunWithReferencesAsync(
                new VideoFaceFrameSet(1, [new VideoFaceFrame(1000, "frame.png")]),
                frames,
                new VideoFaceReferenceSet(1,
                    [new VideoFaceReference("R01", "one.png"),
                     new VideoFaceReference("R02", "two.png")]),
                refs));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task<byte[]> WritePngStub(string root, string name, byte marker)
    {
        byte[] content = [137, 80, 78, 71, 13, 10, 26, 10, marker];
        await File.WriteAllBytesAsync(Path.Combine(root, name), content);
        return content;
    }

    private static string TempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wi0155-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class MarkedDecoder : IImageDecoder
    {
        public async Task<ImageFrame> DecodeAsync(
            Stream source,
            DecodeOptions options,
            CancellationToken cancellationToken)
        {
            using MemoryStream buffer = new();
            await source.CopyToAsync(buffer, cancellationToken);
            byte marker = buffer.ToArray()[^1];
            byte[] pixels = new byte[128 * 128 * 3];
            pixels[0] = marker;
            return new ImageFrame(
                new ImageSize(128, 128), PixelFormat.Bgr24, 128 * 3, pixels);
        }
    }

    private sealed class OneFaceDetector : IFaceDetector
    {
        public ModelDescriptor Descriptor { get; } = new(
            new ModelId("fake-detector"),
            ModelRole.FaceDetection,
            ModelFormat.Onnx,
            new Sha256Digest(new string('a', 64)),
            new ImageSize(640, 640),
            "fake", "test", "test");

        public Task<IReadOnlyList<DetectedFaceCandidate>> DetectAsync(
            ImageFrame frame, CancellationToken cancellationToken)
        {
            IReadOnlyList<DetectedFaceCandidate> faces =
            [
                new DetectedFaceCandidate(
                    new NormalizedBoundingBox(0.2, 0.2, 0.5, 0.5),
                    new NormalizedFaceLandmarks(
                        new NormalizedPoint(0.6, 0.4),
                        new NormalizedPoint(0.4, 0.4),
                        new NormalizedPoint(0.5, 0.5),
                        new NormalizedPoint(0.6, 0.6),
                        new NormalizedPoint(0.4, 0.6)),
                    0.95),
            ];
            return Task.FromResult(faces);
        }
    }

    private sealed class PassAligner : IFaceAligner
    {
        public Task<AlignedFace> AlignAsync(
            ImageFrame image,
            DetectedFaceCandidate candidate,
            AlignmentProtocolId protocol,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AlignedFace(image, protocol));
    }

    private sealed class MarkedEmbedder : IFaceEmbedder
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
            AlignedFace face,
            CancellationToken cancellationToken)
        {
            byte marker = face.Image.Data[0];
            float[] values = marker is 1 or 3 ? [1, 0, 0] : [0, 1, 0];
            return Task.FromResult(new EmbeddingVector(values));
        }
    }
}
