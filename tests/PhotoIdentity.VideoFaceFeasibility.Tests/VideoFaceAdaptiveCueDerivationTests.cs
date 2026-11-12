using System.Text.Json;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceAdaptiveCueDerivationTests
{
    [Fact]
    public void Detector_boundaries_and_weak_samples_request_nearby_frames_without_identity_labels()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(60_000);
        int[] counts = [0, 1, 1, 0, 1, 1, 1, 0, 0];
        Assert.Equal(counts.Length, plan.CoarseFrameCount);
        var observed = plan.FrameTimestampsMs.Select((t, i) =>
            new VideoFaceObservedCoarseFrame(
                i + 1, t, counts[i],
                counts[i] == 0 ? null : i == 6 ? 0.60 : 0.96,
                counts[i] == 0 ? null : i == 4 ? 32.0 : 125.0,
                i == 5 ? 2 : 0)).ToArray();

        var cues = VideoFaceAdaptiveCueDeriver.Derive(plan, observed);

        Assert.Equal(cues.Count, cues.Select(c => c.ObservedFrameTimestampMs).Distinct().Count());
        Assert.Contains(cues, c =>
            c.ObservedFrameTimestampMs == plan.FrameTimestampsMs[4] &&
            c.Kind == VideoFaceSamplingCueKind.PossibleNewOrPoorlyObservedFace);
        Assert.Contains(cues, c =>
            c.ObservedFrameTimestampMs == plan.FrameTimestampsMs[5] &&
            c.Kind == VideoFaceSamplingCueKind.UncertainFace);
        Assert.Contains(cues, c =>
            c.ObservedFrameTimestampMs == plan.FrameTimestampsMs[6] &&
            c.Kind == VideoFaceSamplingCueKind.UncertainFace);
        Assert.Contains(cues, c =>
            c.ObservedFrameTimestampMs == plan.FrameTimestampsMs[0] &&
            c.Kind == VideoFaceSamplingCueKind.PossibleEntranceOrExit);
        Assert.Contains(cues, c =>
            c.ObservedFrameTimestampMs == plan.FrameTimestampsMs[3] &&
            c.Kind == VideoFaceSamplingCueKind.PossibleEntranceOrExit);

        var refined = VideoFaceAdaptiveSamplingPlanner.Refine(plan, cues);
        Assert.True(refined.FrameTimestampsMs.Count > plan.FrameTimestampsMs.Count);
        Assert.All(plan.FrameTimestampsMs,
            t => Assert.Contains(t, refined.FrameTimestampsMs));
    }

    [Fact]
    public void No_detected_faces_produces_no_claim_of_people_or_events()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(70_000);
        var observations = plan.FrameTimestampsMs.Select((t, i) =>
            new VideoFaceObservedCoarseFrame(i + 1, t, 0, null, null, 0)).ToArray();
        Assert.Empty(VideoFaceAdaptiveCueDeriver.Derive(plan, observations));
    }

    [Fact]
    public void Always_visible_stable_detection_does_not_monopolize_the_refinement_budget()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(600_000);
        var observations = plan.FrameTimestampsMs.Select((t, i) =>
            new VideoFaceObservedCoarseFrame(i + 1, t, 1, .95, 120, 0)).ToArray();
        var cues = VideoFaceAdaptiveCueDeriver.Derive(plan, observations);
        Assert.Single(cues);
        Assert.Equal(plan.FrameTimestampsMs[0], cues[0].ObservedFrameTimestampMs);
    }

    [Fact]
    public void Missing_or_tampered_coarse_evidence_fails_closed()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(45_000);
        var observed = plan.FrameTimestampsMs.Select((t, i) =>
            new VideoFaceObservedCoarseFrame(i + 1, t, 0, null, null, 0)).ToArray();

        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveCueDeriver.Derive(plan, observed.Skip(1).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveCueDeriver.Derive(plan,
                observed.Select((f, i) => i == 1 ? f with { AbsoluteTimestampMs = f.AbsoluteTimestampMs + 1 } : f).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveCueDeriver.Derive(plan,
                observed.Select((f, i) => i == 0 ? f with { FacesDetected = 33 } : f).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveCueDeriver.Derive(plan,
                observed.Select((f, i) => i == 0 ? f with { CandidatesDiscarded = 1 } : f).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveCueDeriver.Derive(plan,
                observed.Select((f, i) => i == 0 ? f with { LowestConfidence = double.NaN } : f).ToArray()));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(600_000L)]
    public async Task Private_io_requires_complete_matching_coarse_window_and_new_output(long start)
    {
        string tmp = Path.Combine(Path.GetTempPath(),
            "wi0155-cue-derivation-" + Guid.NewGuid().ToString("N"));
        string repo = Path.Combine(tmp, "repository");
        string workspace = Path.Combine(tmp, "coverage-001");
        string privateFolder = Path.Combine(tmp, "private");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(privateFolder);
        const long duration = 1_200_000;
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(duration, start);
        var manifest = Path.Combine(workspace, "frames.json");
        var report = Path.Combine(privateFolder, "report.json");
        var output = Path.Combine(privateFolder, "cues.json");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "wi0155-run.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    kind = "PhotoIdentity.WI0155.CoverageWorkspace",
                    state = "extracted",
                }));
            await File.WriteAllTextAsync(Path.Combine(workspace, "extraction-metadata.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    samplingMode = "Adaptive",
                    samplingClipDurationMs = duration,
                    samplingWindowStartMs = start,
                    samplingWindowEndMs = plan.WindowEndMs,
                    samplingCoarseFrameCount = plan.CoarseFrameCount,
                    samplingRefinementBudgetReached = false,
                    samplingHasMoreWindows = plan.HasMoreWindows,
                }));
            var frameEntries = plan.FrameTimestampsMs.Select((timestamp, i) =>
                new { timestampMs = timestamp - start, file = $"frame-{i + 1:000}.png" })
                .ToArray();
            await File.WriteAllTextAsync(manifest,
                JsonSerializer.Serialize(new { schemaVersion = 1, frames = frameEntries }));
            foreach (var frame in frameEntries)
                await File.WriteAllBytesAsync(Path.Combine(workspace, frame.file), [0x89, 0x50, 0x4E, 0x47]);

            var observations = plan.FrameTimestampsMs.Select((timestamp, i) => new
            {
                frameNumber = i + 1,
                timestampMs = timestamp - start,
                facesDetected = i == 1 ? 1 : 0,
                lowestConfidence = i == 1 ? (double?)0.95 : null,
                smallestFaceWidthPixels = i == 1 ? (double?)90 : null,
                candidatesDiscarded = 0,
            }).ToArray();
            await File.WriteAllTextAsync(report,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    aggregate = new { framesExamined = frameEntries.Length },
                    diagnostics = new { frameDetections = observations },
                    secretTopReferenceAlias = "must-not-be-in-output",
                }));
            var cues = await VideoFaceAdaptiveCueCommand.DeriveFromPrivateFilesAsync(
                manifest, report, repo);
            Assert.NotEmpty(cues);
            Assert.All(cues, c => Assert.InRange(c.ObservedFrameTimestampMs, start, plan.WindowEndMs - 1));
            await VideoFaceAdaptiveCueCommand.WriteCuesAsync(output, repo, workspace, cues);
            var outputText = await File.ReadAllTextAsync(output);
            Assert.DoesNotContain("secretTopReferenceAlias", outputText);
            Assert.DoesNotContain("must-not-be-in-output", outputText);
            var loaded = await VideoFaceAdaptiveSamplingCommand.LoadCuesAsync(output);
            Assert.Equal(cues, loaded);
            var refined = VideoFaceAdaptiveSamplingCommand.CreatePlan(duration, start, loaded);
            Assert.True(refined.FrameTimestampsMs.Count > plan.FrameTimestampsMs.Count);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await VideoFaceAdaptiveCueCommand.WriteCuesAsync(output, repo, workspace, cues));
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await VideoFaceAdaptiveCueCommand.WriteCuesAsync(
                    Path.Combine(repo, "oops.json"), repo, workspace, cues));

            // A report with the same count but the wrong timeline is rejected.
            string mismatch = await File.ReadAllTextAsync(report);
            mismatch = mismatch.Replace($"\"timestampMs\":{frameEntries[0].timestampMs}",
                $"\"timestampMs\":{frameEntries[0].timestampMs + 1}", StringComparison.Ordinal);
            await File.WriteAllTextAsync(report, mismatch);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                _ = await VideoFaceAdaptiveCueCommand.DeriveFromPrivateFilesAsync(
                    manifest, report, repo);
            });
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
