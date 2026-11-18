using System.Text.Json;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceWholeClipCoverageTests
{
    private const string SourceA = "A123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string SourceB = "B123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private static VideoFaceCoverageWindowEvidence Window(
        long duration, long start, bool refined = false,
        string source = SourceA, Guid? run = null)
    {
        VideoFaceSamplingPlan plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(duration, start);
        var times = plan.FrameTimestampsMs.ToList();
        if (refined)
        {
            times.Add(times[0] + 250);
            times.Sort();
        }

        return new VideoFaceCoverageWindowEvidence(
            run ?? Guid.NewGuid(), source, duration, start, plan.WindowEndMs,
            plan.CoarseFrameCount, times, false, times.Count * 9L);
    }

    [Fact]
    public void Long_clip_only_completes_when_every_explicit_window_is_present()
    {
        const long duration = 1_450_000;
        var first = Window(duration, 0);
        var middle = Window(duration, 600_000, refined: true);
        var last = Window(duration, 1_200_000);
        var partial = VideoFaceWholeClipCoverage.Evaluate([last, first]);
        Assert.False(partial.WholeClipCoverageComplete);
        Assert.Equal(3, partial.ExpectedWindows);
        Assert.Equal(2, partial.VerifiedWindows);
        Assert.Equal(new long[] { 600_000 }, partial.MissingWindowStartsMs);
        Assert.Equal(600_000L, partial.NextMissingWindowStartMs);
        Assert.Equal(850_000, partial.CoveredWindowDurationMs);
        Assert.True(partial.MaximumRequestedFrameGapMs >= 600_000);
        Assert.False(partial.SourceContentSha256Verified);

        var complete = VideoFaceWholeClipCoverage.Evaluate([last, first, middle]);
        var reordered = VideoFaceWholeClipCoverage.Evaluate([middle, last, first]);
        Assert.True(complete.WholeClipCoverageComplete);
        Assert.Empty(complete.MissingWindowStartsMs);
        Assert.Null(complete.NextMissingWindowStartMs);
        Assert.Equal(duration, complete.CoveredWindowDurationMs);
        Assert.Equal(complete.TotalRequestedFrames, reordered.TotalRequestedFrames);
        Assert.Equal(complete.Windows, reordered.Windows);
        Assert.Equal(1, complete.RefinedFrames);
        Assert.InRange(complete.MaximumRequestedFrameGapMs, 1,
            VideoFaceAdaptiveSamplingPlanner.MaximumCoarseGapMs);
    }

    [Theory]
    [InlineData(200L)]
    [InlineData(45_000L)]
    [InlineData(600_000L)]
    [InlineData(3_600_000L)]
    public void Boundary_duration_receipts_have_no_unreported_tail(long duration)
    {
        var windows = new List<VideoFaceCoverageWindowEvidence>();
        for (long start = 0; start < duration;
            start += VideoFaceAdaptiveSamplingPlanner.MaximumWindowLengthMs)
            windows.Add(Window(duration, start));
        var receipt = VideoFaceWholeClipCoverage.Evaluate(windows);
        Assert.True(receipt.WholeClipCoverageComplete);
        Assert.Equal(duration, receipt.CoveredWindowDurationMs);
        Assert.Equal(windows.Count, receipt.ExpectedWindows);
        Assert.InRange(receipt.MaximumRequestedFrameGapMs, 1,
            VideoFaceAdaptiveSamplingPlanner.MaximumCoarseGapMs);
    }

    [Fact]
    public void Pure_audit_rejects_wrong_source_repeated_windows_missing_coarse_and_forged_caps()
    {
        const long duration = 660_000;
        var first = Window(duration, 0);
        var last = Window(duration, 600_000);
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([first, last with { SourceDescriptorSha256 = SourceB }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([first, last with { RunId = first.RunId }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([first, first]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([first, last with { ClipDurationMs = duration + 1 }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([first with
            {
                RequestedFrameTimestampsMs = first.RequestedFrameTimestampsMs.Skip(1).ToArray(),
            }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([first with
            {
                RequestedFrameTimestampsMs = first.RequestedFrameTimestampsMs
                    .Concat([first.RequestedFrameTimestampsMs[0]]).ToArray(),
            }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([first with { RefinementBudgetReached = true }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceWholeClipCoverage.Evaluate([last with { WindowStartMs = 599_999 }]));
    }

    [Fact]
    public async Task Owned_coarse_and_refined_window_manifests_produce_private_complete_receipt()
    {
        using var f = new Fixture();
        const long duration = 660_000;
        string first = await f.CreateWindowAsync(duration, 0, refined: true);
        string last = await f.CreateWindowAsync(duration, 600_000);
        VideoFaceWholeClipCoverageReport audited =
            await VideoFaceWholeClipCoverage.AuditAsync([last, first], f.Repository);
        Assert.True(audited.WholeClipCoverageComplete);
        Assert.Equal(2, audited.VerifiedWindows);
        Assert.Equal(1, audited.RefinedFrames);
        Assert.Equal((audited.TotalRequestedFrames * 9L), audited.ExtractedFrameBytes);

        string output = Path.Combine(f.Private, "coverage-receipt.json");
        Assert.Equal(0, await Program.Main([
            "--audit-adaptive-coverage", "--manifest", last, "--manifest", first,
            "--private-report", output, "--root", f.Repository,
        ]));
        string json = await File.ReadAllTextAsync(output);
        Assert.Contains("\"wholeClipCoverageComplete\": true", json);
        Assert.Contains("\"sourceContentSha256Verified\": false", json);
        Assert.Contains("\"sourceDescriptorSha256\"", json);
        Assert.DoesNotContain(f.Root, json, StringComparison.Ordinal);
        Assert.DoesNotContain("frame-001.png", json, StringComparison.Ordinal);
        Assert.NotEqual(0, await Program.Main([
            "--audit-adaptive-coverage", "--manifest", first,
            "--private-report", output, "--root", f.Repository,
        ]));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.WritePrivateReportAsync(
                Path.Combine(f.Repository, "forbidden.json"), f.Repository, [first], audited));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.WritePrivateReportAsync(
                Path.Combine(Path.GetDirectoryName(first)!, "forbidden.json"),
                f.Repository, [first], audited));
    }

    [Fact]
    public async Task A_later_window_alone_is_valid_evidence_but_never_complete()
    {
        using var f = new Fixture();
        string last = await f.CreateWindowAsync(650_000, 600_000);
        VideoFaceWholeClipCoverageReport result =
            await VideoFaceWholeClipCoverage.AuditAsync([last], f.Repository);
        Assert.False(result.WholeClipCoverageComplete);
        Assert.Equal(new long[] { 0 }, result.MissingWindowStartsMs);
        Assert.Equal(0L, result.NextMissingWindowStartMs);
        Assert.True(result.MaximumRequestedFrameGapMs >= 600_000);
        Assert.Equal(1, result.VerifiedWindows);
    }

    [Fact]
    public async Task Missing_original_metadata_and_incomplete_frame_inputs_fail_closed()
    {
        using var f = new Fixture();
        string manifest = await f.CreateWindowAsync(45_000, 0);
        string dir = Path.GetDirectoryName(manifest)!;
        string metadataPath = Path.Combine(dir, "extraction-metadata.json");
        string metadata = await File.ReadAllTextAsync(metadataPath);
        await File.WriteAllTextAsync(metadataPath,
            metadata.Replace("\"sourceDescriptorSha256\"", "\"oldSourceDescriptor\"",
                StringComparison.Ordinal));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.AuditAsync([manifest], f.Repository));

        await File.WriteAllTextAsync(metadataPath, metadata);
        File.Delete(Path.Combine(dir, "frame-002.png"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.AuditAsync([manifest], f.Repository));
    }

    [Fact]
    public async Task Tampering_declared_duration_files_or_workspace_state_refuses_completion()
    {
        using var f = new Fixture();
        string manifest = await f.CreateWindowAsync(45_000, 0);
        string dir = Path.GetDirectoryName(manifest)!;
        string metadataPath = Path.Combine(dir, "extraction-metadata.json");
        string metadata = await File.ReadAllTextAsync(metadataPath);
        await File.WriteAllTextAsync(metadataPath,
            metadata.Replace("\"extractedFrameBytes\":", "\"wrongByteCount\":",
                StringComparison.Ordinal));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.AuditAsync([manifest], f.Repository));

        await File.WriteAllTextAsync(metadataPath, metadata);
        string markerPath = Path.Combine(dir, "wi0155-run.json");
        string marker = await File.ReadAllTextAsync(markerPath);
        await File.WriteAllTextAsync(markerPath,
            marker.Replace("\"extracted\"", "\"extracting\"", StringComparison.Ordinal));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.AuditAsync([manifest], f.Repository));
    }

    [Fact]
    public async Task Completed_workspace_rejects_unlisted_files_without_touching_them()
    {
        using var f = new Fixture();
        string manifest = await f.CreateWindowAsync(45_000, 0);
        string unexpected = Path.Combine(Path.GetDirectoryName(manifest)!, "unlisted-crop.png");
        await File.WriteAllBytesAsync(unexpected, [1, 2, 3]);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.AuditAsync([manifest], f.Repository));
        Assert.True(File.Exists(unexpected));
    }

    [Fact]
    public async Task Distinct_locally_owned_windows_with_different_source_descriptors_are_rejected()
    {
        using var f = new Fixture();
        string a = await f.CreateWindowAsync(660_000, 0, source: SourceA);
        string b = await f.CreateWindowAsync(660_000, 600_000, source: SourceB);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            VideoFaceWholeClipCoverage.AuditAsync([a, b], f.Repository));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "wi0155-completion-" + Guid.NewGuid().ToString("N"));
        public string Repository => Path.Combine(Root, "repo");
        public string Private => Path.Combine(Root, "private");
        private int _index;

        public Fixture()
        {
            Directory.CreateDirectory(Repository);
            Directory.CreateDirectory(Private);
        }

        public async Task<string> CreateWindowAsync(
            long duration, long start, bool refined = false, string source = SourceA)
        {
            int index = ++_index;
            string folder = Path.Combine(Root, $"coverage-{index:D3}");
            Directory.CreateDirectory(folder);
            var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(duration, start);
            var timestamps = plan.FrameTimestampsMs.ToList();
            if (refined)
            {
                timestamps.Add(timestamps[0] + 250);
                timestamps.Sort();
            }
            string manifest = Path.Combine(folder, "frames.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                frames = timestamps.Select((t, i) => new
                {
                    timestampMs = t - start,
                    file = $"frame-{i + 1:D3}.png",
                }),
            }));
            foreach (int i in Enumerable.Range(1, timestamps.Count))
                await File.WriteAllBytesAsync(Path.Combine(folder, $"frame-{i:D3}.png"),
                    [137, 80, 78, 71, 13, 10, 26, 10, 1]);
            await File.WriteAllTextAsync(Path.Combine(folder, "wi0155-run.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    kind = "PhotoIdentity.WI0155.CoverageWorkspace",
                    state = "extracted",
                    runId = Guid.NewGuid().ToString("D"),
                }));
            await File.WriteAllTextAsync(Path.Combine(folder, "extraction-metadata.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    frameEncoding = "NativeLosslessPng",
                    extractedFrameBytes = timestamps.Count * 9L,
                    maximumWorkspaceMiB = 1024,
                    samplingMode = "Adaptive",
                    sourceDescriptorSha256 = source,
                    samplingCoarseFrameCount = plan.CoarseFrameCount,
                    samplingRefinementBudgetReached = false,
                    samplingHasMoreWindows = plan.HasMoreWindows,
                    samplingClipDurationMs = duration,
                    samplingWindowStartMs = start,
                    samplingWindowEndMs = plan.WindowEndMs,
                }));
            return manifest;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
