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
            Assert.Null(evaluated.Diagnostics); // No diagnostic data without explicit opt-in.
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

    [Fact]
    public void Diagnostic_sensitivity_shows_suppressed_top_rank_without_changing_default_matching()
    {
        EmbeddingVector reference = new(new float[] { 1, 0, 0 });
        EmbeddingVector unrelated = new(new float[] { -1, 0, 0 });
        EmbeddingVector observed = new EmbeddingVector(
            new float[] { 0.54f, 0.8417f, 0 }).Normalize();
        VideoFaceReferenceMatcher matcher = new(
            [new VideoFaceReferenceEmbedding("R01", reference),
             new VideoFaceReferenceEmbedding("R02", unrelated)]);

        VideoFaceReferenceFinding baseline = matcher.FindCandidate(1, 1000, 1, observed);
        Assert.Null(baseline.CandidateAlias);
        Assert.Equal("below-exploratory-threshold", baseline.ReviewStatus);

        VideoFaceRankedDiagnostic diagnostic = matcher.Diagnose(1, 1000, 1, observed);
        Assert.Equal("R01", diagnostic.TopReferenceAlias);
        Assert.InRange(diagnostic.TopSimilarity, 0.53, 0.55);
        Assert.Collection(diagnostic.ThresholdChecks,
            result =>
            {
                Assert.Equal(0.363, result.SimilarityThreshold);
                Assert.Equal("would-suggest-for-review", result.Outcome);
            },
            result =>
            {
                Assert.Equal(0.45, result.SimilarityThreshold);
                Assert.Equal("would-suggest-for-review", result.Outcome);
            },
            result =>
            {
                Assert.Equal(0.60, result.SimilarityThreshold);
                Assert.Equal("below-threshold", result.Outcome);
            },
            result =>
            {
                Assert.Equal(0.80, result.SimilarityThreshold);
                Assert.Equal("below-threshold", result.Outcome);
            });
        Assert.Throws<ArgumentException>(() =>
            VideoFaceSensitivityDiagnostics.Compare(double.NaN, 0));
    }

    [Fact]
    public void Diagnostic_threshold_sweep_expresses_single_reference_and_ambiguity()
    {
        Assert.All(VideoFaceSensitivityDiagnostics.Compare(0.9, null),
            outcome => Assert.Equal("single-reference-uncertain", outcome.Outcome));
        Assert.All(VideoFaceSensitivityDiagnostics.Compare(0.95, 0.94),
            outcome => Assert.Equal("ambiguous", outcome.Outcome));
        Assert.All(VideoFaceSensitivityDiagnostics.Compare(0.1, 0.01),
            outcome => Assert.Equal("below-threshold", outcome.Outcome));
    }

    [Fact]
    public async Task Private_diagnostics_include_zero_detection_frames_without_changing_media()
    {
        string root = TempDir();
        try
        {
            string frames = Directory.CreateDirectory(Path.Combine(root, "frames")).FullName;
            string refs = Directory.CreateDirectory(Path.Combine(root, "refs")).FullName;
            string reviews = Directory.CreateDirectory(Path.Combine(root, "reviews")).FullName;
            string repo = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;
            await WritePngStub(frames, "one.png", 1);
            await WritePngStub(frames, "two.png", 2);
            await WritePngStub(refs, "ref-one.png", 3);
            await WritePngStub(refs, "ref-two.png", 4);
            string[] files = Directory.GetFiles(frames).Concat(Directory.GetFiles(refs)).ToArray();
            byte[][] original = files.Select(File.ReadAllBytes).ToArray();

            VideoFaceFrameSet selection = new(1,
                [new VideoFaceFrame(1000, "one.png"), new VideoFaceFrame(3000, "two.png")]);
            VideoFaceReferenceSet references = new(1,
                [new VideoFaceReference("R01", "ref-one.png"),
                 new VideoFaceReference("R02", "ref-two.png")]);

            VideoFaceExperiment experiment = new(
                new MarkedDecoder(), new OneFaceDetector(missSecondFrame: true),
                new PassAligner(), new MarkedEmbedder());
            VideoFacePrivateReview evaluation = await experiment.RunWithReferencesAsync(
                selection, frames, references, refs,
                diagnostics: true, requestedDetectorConfidence: 0.7);

            Assert.Equal(1, evaluation.Aggregate.FacesDetected);
            VideoFaceSensitivityDiagnostics diag = Assert.IsType<VideoFaceSensitivityDiagnostics>(
                evaluation.Diagnostics);
            Assert.Equal(0.7, diag.RequestedDetectorConfidenceThreshold);
            Assert.Collection(diag.FrameDetections,
                first =>
                {
                    Assert.Equal(1, first.FrameNumber);
                    Assert.Equal(1, first.FacesDetected);
                    Assert.Equal(0.95, first.HighestConfidence);
                },
                second =>
                {
                    Assert.Equal(2, second.FrameNumber);
                    Assert.Equal(3000, second.TimestampMs);
                    Assert.Equal(0, second.FacesDetected);
                    Assert.Null(second.LowestConfidence);
                    Assert.Null(second.HighestConfidence);
                });
            Assert.Single(diag.RankedFaces);
            Assert.Equal("R01", diag.RankedFaces[0].TopReferenceAlias);

            string output = Path.Combine(reviews, "sensitivity.json");
            await VideoFacePrivateReportWriter.WriteAsync(
                output, repo, frames, refs, evaluation);
            using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(output));
            Assert.Equal(2,
                json.RootElement.GetProperty("diagnostics")
                    .GetProperty("frameDetections").GetArrayLength());
            Assert.Equal(1,
                json.RootElement.GetProperty("diagnostics")
                    .GetProperty("rankedFaces").GetArrayLength());

            for (int i = 0; i < files.Length; i++)
            {
                Assert.Equal(original[i], await File.ReadAllBytesAsync(files[i]));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Cli_rejects_implicit_diagnostics_duplicate_flags_and_unapproved_thresholds()
    {
        string[][] invalid =
        [
            ["--manifest", "does-not-exist.json", "--diagnostics"],
            ["--manifest", "does-not-exist.json", "--detector-confidence", "0.7"],
            ["--manifest", "does-not-exist.json", "--diagnostics", "--diagnostics"],
            ["--manifest", "does-not-exist.json", "--references", "ref.json",
             "--private-report", "new.json", "--detector-confidence", "0.7"],
            ["--manifest", "does-not-exist.json", "--references", "ref.json",
             "--private-report", "new.json", "--diagnostics", "--detector-confidence", "0.2"],
        ];

        foreach (string[] arguments in invalid)
        {
            Assert.Equal(2, await Program.Main(arguments));
        }
    }

    [Fact]
    public async Task Non_default_detector_confidence_requires_diagnostics()
    {
        VideoFaceExperiment experiment = new(
            new MarkedDecoder(), new OneFaceDetector(), new PassAligner(), new MarkedEmbedder());
        await Assert.ThrowsAsync<ArgumentException>(() => experiment.RunWithReferencesAsync(
            new VideoFaceFrameSet(1, [new VideoFaceFrame(0, "a.png")]),
            "not-read", new VideoFaceReferenceSet(1, [new VideoFaceReference("R01", "b.png")]),
            "not-read", requestedDetectorConfidence: 0.7));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => experiment.RunWithReferencesAsync(
            new VideoFaceFrameSet(1, [new VideoFaceFrame(0, "a.png")]),
            "not-read", new VideoFaceReferenceSet(1, [new VideoFaceReference("R01", "b.png")]),
            "not-read", diagnostics: true, requestedDetectorConfidence: 0.123));
    }

    [Fact]
    public void Coverage_grouper_requires_human_review_and_keeps_simultaneous_faces_separate()
    {
        VideoFaceCoverageGrouper grouper = new();
        EmbeddingVector a = new(new float[] { 1, 0, 0 });
        EmbeddingVector b = new(new float[] { 0, 1, 0 });

        VideoFaceCoverageAssignment first = grouper.Observe(1, 1000, 1, a);
        VideoFaceCoverageAssignment simultaneous = grouper.Observe(1, 1000, 2, a);
        VideoFaceCoverageAssignment other = grouper.Observe(2, 2000, 1, b);
        VideoFaceCoverageAssignment repeated = grouper.Observe(3, 3000, 1, a);

        Assert.Equal("G0001", first.GroupAlias);
        Assert.Equal("needs-distinct-face-review", first.ReviewStatus);
        Assert.Equal("G0002", simultaneous.GroupAlias);
        Assert.Equal("G0003", other.GroupAlias);
        Assert.Equal("G0001", repeated.GroupAlias);
        Assert.Equal("possible-repeat", repeated.ReviewStatus);
        Assert.Equal(3, grouper.Groups.Count);
        Assert.Equal(2, grouper.Groups[0].Observations);
        Assert.Throws<ArgumentException>(() => grouper.Observe(3, 3000, 1, a));
        Assert.Equal(4, grouper.Assignments.Count);
    }

    [Fact]
    public async Task Coverage_is_explicitly_limited_and_preserves_default_frame_budget()
    {
        string root = TempDir();
        try
        {
            string manifest = Path.Combine(root, "frames.json");
            VideoFaceFrameSet selected = new(1,
                Enumerable.Range(1, 36)
                    .Select(i => new VideoFaceFrame(i * 1000, $"f{i:00}.png")).ToList());
            VideoFaceFrameSetLoader.Validate(selected, coverage: true);
            Assert.Throws<ArgumentException>(() => VideoFaceFrameSetLoader.Validate(selected));

            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                frames = selected.Frames!.Select(f => new
                {
                    timestampMs = f.TimestampMs,
                    file = f.File,
                }),
            }));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoFaceFrameSetLoader.LoadAsync(manifest));

            VideoFaceFrameSet actual = await VideoFaceFrameSetLoader.LoadAsync(
                manifest, coverage: true);
            Assert.Equal(36, actual.Frames!.Count);
            VideoFaceFrameSet expanded = new(1,
                Enumerable.Range(1, 180).Select(i =>
                    new VideoFaceFrame(i * 1000, $"f{i:000}.png")).ToList());
            VideoFaceFrameSetLoader.Validate(expanded, coverage: true);
            // The new lossless sampler uses PNG names. Coverage validation must
            // accept all 180 frames without relaxing the ordinary 12-frame budget.
            VideoFaceFrameSet lossless = new(1,
                Enumerable.Range(1, 180).Select(i =>
                    new VideoFaceFrame(i * 1000, $"frame-{i:000}.png")).ToList());
            VideoFaceFrameSetLoader.Validate(lossless, coverage: true);
            Assert.Throws<ArgumentException>(() => VideoFaceFrameSetLoader.Validate(lossless));
            Assert.Throws<ArgumentException>(() => VideoFaceFrameSetLoader.Validate(expanded));
            Assert.Throws<ArgumentException>(() => VideoFaceFrameSetLoader.Validate(
                new VideoFaceFrameSet(1,
                    Enumerable.Range(1, 181).Select(i =>
                        new VideoFaceFrame(i * 1000, $"f{i:000}.png")).ToList()),
                coverage: true));
            Assert.Equal(2, await Program.Main(
                ["--manifest", "absent.json", "--coverage"]));
            Assert.Equal(2, await Program.Main(
                ["--manifest", "absent.json", "--coverage", "--diagnostics"]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Private_24_frame_coverage_groups_are_in_memory_and_do_not_change_images()
    {
        string root = TempDir();
        try
        {
            string frames = Directory.CreateDirectory(Path.Combine(root, "frames")).FullName;
            string refs = Directory.CreateDirectory(Path.Combine(root, "refs")).FullName;
            string reviews = Directory.CreateDirectory(Path.Combine(root, "reviews")).FullName;
            string repo = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;

            List<VideoFaceFrame> samples = [];
            for (int i = 0; i < 24; i++)
            {
                string file = $"sample-{i + 1:00}.png";
                await WritePngStub(frames, file, (byte)(i % 2 == 0 ? 1 : 2));
                samples.Add(new VideoFaceFrame((i + 1) * 1000, file));
            }
            await WritePngStub(refs, "reference-01.png", 3);
            await WritePngStub(refs, "reference-02.png", 4);

            string[] files = Directory.GetFiles(frames).Concat(Directory.GetFiles(refs))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            byte[][] original = files.Select(File.ReadAllBytes).ToArray();
            VideoFaceExperiment experiment = new(
                new MarkedDecoder(), new OneFaceDetector(),
                new PassAligner(), new MarkedEmbedder());

            VideoFacePrivateReview result = await experiment.RunWithReferencesAsync(
                new VideoFaceFrameSet(1, samples), frames,
                new VideoFaceReferenceSet(1,
                    [new VideoFaceReference("R01", "reference-01.png"),
                     new VideoFaceReference("R02", "reference-02.png")]),
                refs, diagnostics: true, requestedDetectorConfidence: 0.7,
                coverage: true);

            Assert.Equal(24, result.Aggregate.FramesExamined);
            Assert.Equal(24, result.Aggregate.EmbeddingsCalculated);
            Assert.Equal(24, result.Findings.Count);
            VideoFaceCoverageReview coverage = Assert.IsType<VideoFaceCoverageReview>(
                result.Diagnostics!.Coverage);
            Assert.Equal(2, coverage.PossibleAppearanceGroups);
            Assert.Equal(24, coverage.Assignments.Count);
            Assert.Equal(2, coverage.Groups.Count);
            Assert.Equal(12, coverage.Groups[0].Observations);
            Assert.Equal(12, coverage.Groups[1].Observations);
            Assert.Equal(3, coverage.GroupingComparisons!.Count);
            Assert.Empty(coverage.SameFramePairs!);
            Assert.All(coverage.GroupingComparisons!, comparison =>
                Assert.Equal(24, comparison.Assignments.Count));

            string reportPath = Path.Combine(reviews, "coverage-private.json");
            await VideoFacePrivateReportWriter.WriteAsync(
                reportPath, repo, frames, refs, result);
            string reportText = await File.ReadAllTextAsync(reportPath);
            Assert.DoesNotContain("reference-01.png", reportText);
            Assert.DoesNotContain("sample-01.png", reportText);
            Assert.DoesNotContain("\"embedding\"", reportText, StringComparison.OrdinalIgnoreCase);
            using JsonDocument json = JsonDocument.Parse(reportText);
            Assert.Equal(2, json.RootElement.GetProperty("diagnostics")
                .GetProperty("coverage").GetProperty("possibleAppearanceGroups").GetInt32());
            Assert.Equal(3, json.RootElement.GetProperty("diagnostics")
                .GetProperty("coverage").GetProperty("groupingComparisons").GetArrayLength());
            Assert.Equal(0, json.RootElement.GetProperty("diagnostics")
                .GetProperty("coverage").GetProperty("sameFramePairs").GetArrayLength());

            for (int i = 0; i < files.Length; i++)
            {
                Assert.Equal(original[i], await File.ReadAllBytesAsync(files[i]));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Multi_observation_grouping_can_bridge_view_variation_without_rewriting_baseline()
    {
        EmbeddingVector frontal = new(new float[] { 1, 0, 0 });
        EmbeddingVector intermediate = new(new float[] { 0.85f, 0.5267827f, 0 });
        EmbeddingVector angled = new(new float[] { 0.5f, 0.8660254f, 0 });

        VideoFaceCoverageGrouper baseline = new();
        VideoFaceCoverageGrouper multi = new(
            repeatSimilarityThreshold: 0.80, useMultipleObservations: true);
        foreach ((int frame, EmbeddingVector embedding) in new[]
        {
            (1, frontal), (2, intermediate), (3, angled),
        })
        {
            baseline.Observe(frame, frame * 1000, 1, embedding);
            multi.Observe(frame, frame * 1000, 1, embedding);
        }

        Assert.Equal(2, baseline.Groups.Count);
        Assert.Single(multi.Groups);
        Assert.Equal(3, multi.Groups[0].Observations);
        // Multiple observations never become canonical identity evidence.
        Assert.All(multi.Assignments, assignment =>
            Assert.StartsWith("G", assignment.GroupAlias));
    }

    [Fact]
    public void Threshold_comparisons_expose_low_threshold_false_merge_risk()
    {
        EmbeddingVector first = new(new float[] { 1, 0, 0 });
        EmbeddingVector second = new(new float[] { 0.62f, 0.7846018f, 0 });
        VideoFaceCoverageObservation[] observations =
        [
            new(1, 1000, 1, first),
            new(2, 2000, 1, second),
        ];
        IReadOnlyList<VideoFaceCoverageComparison> comparisons =
            VideoFaceCoverageSensitivity.Compare(observations);
        Assert.Equal(new[] { 0.60, 0.70, 0.80 },
            comparisons.Select(c => c.RepeatSimilarityThreshold).ToArray());
        Assert.Equal(1, comparisons[0].PossibleAppearanceGroups);
        Assert.Equal(2, comparisons[1].PossibleAppearanceGroups);
        Assert.Equal(2, comparisons[2].PossibleAppearanceGroups);
        Assert.All(comparisons, comparison =>
        {
            Assert.Equal(2, comparison.Assignments.Count);
            Assert.Equal("multi-observation-max", comparison.Strategy);
        });
        Assert.Throws<ArgumentException>(() =>
            VideoFaceCoverageSensitivity.Compare(
                Enumerable.Repeat(observations[0], 257).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VideoFaceCoverageGrouper(repeatSimilarityThreshold: 0.1));
    }

    [Fact]
    public void Same_frame_pairs_are_review_hints_and_never_collapse_detections()
    {
        DetectedFaceCandidate first = new(
            new NormalizedBoundingBox(0.2, 0.2, 0.3, 0.3),
            new NormalizedFaceLandmarks(
                new NormalizedPoint(0.3, 0.3),
                new NormalizedPoint(0.4, 0.3),
                new NormalizedPoint(0.35, 0.4),
                new NormalizedPoint(0.3, 0.45),
                new NormalizedPoint(0.4, 0.45)),
            0.75);
        DetectedFaceCandidate second = new(
            new NormalizedBoundingBox(0.22, 0.22, 0.3, 0.3),
            first.Landmarks, 0.74);
        EmbeddingVector vector = new(new float[] { 1, 0, 0 });
        var pairs = VideoFaceCoverageSensitivity.DescribeSameFrame(
            14, [first, second], [vector, vector]);
        VideoFaceSameFramePair pair = Assert.Single(pairs);
        Assert.Equal(14, pair.FrameNumber);
        Assert.Equal("possible-duplicate-review", pair.ReviewStatus);
        Assert.True(pair.BoundingBoxIntersectionOverUnion > 0.40);
        Assert.Equal(1, pair.EmbeddingCosineSimilarity);

        VideoFaceCoverageGrouper grouper = new(
            repeatSimilarityThreshold: 0.60, useMultipleObservations: true);
        grouper.Observe(14, 14000, 1, vector);
        grouper.Observe(14, 14000, 2, vector);
        Assert.Equal(2, grouper.Groups.Count);
        Assert.Equal(2, grouper.Assignments.Count);
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
        private readonly bool _missSecondFrame;

        public OneFaceDetector(bool missSecondFrame = false) =>
            _missSecondFrame = missSecondFrame;

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
            if (_missSecondFrame && frame.Data[0] == 2)
            {
                return Task.FromResult<IReadOnlyList<DetectedFaceCandidate>>([]);
            }

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
