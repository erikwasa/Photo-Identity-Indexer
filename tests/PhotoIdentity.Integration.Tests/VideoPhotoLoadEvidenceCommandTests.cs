using System.Text;
using System.Text.Json;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoPhotoLoadEvidenceCommandTests
{
    private static double[] Samples(double milliseconds) =>
        Enumerable.Repeat(milliseconds, 20).ToArray();

    private static VideoPhotoLoadObservation Observation(
        string phase, int jobs, double duration, double latency,
        bool videoObserved) => new(
            phase, jobs, duration, videoObserved,
            Samples(latency), Samples(latency), Samples(latency),
            Samples(latency));

    private static byte[] ObservationJson(string phase, bool videoObserved,
        double latency = 100) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocol = "wi0188-photo-load-observation-v1",
            phase,
            photoJobsCompleted = 40,
            windowSeconds = 120,
            videoJobObserved = videoObserved,
            visualSearchMs = Samples(latency),
            captionSearchMs = Samples(latency),
            combinedSearchMs = Samples(latency),
            slideshowAdvanceMs = Samples(latency)
        });

    private static byte[] LimitsJson(double maxDrop = 10, double maxRise = 20) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocol = "wi0188-photo-load-operator-limits-v1",
            maxThroughputDropPercent = maxDrop,
            maxP95IncreasePercent = maxRise
        });

    [Fact]
    public void Explicit_limits_define_diagnostic_comparison_not_WI_acceptance()
    {
        VideoPhotoLoadObservation baseline = Observation(
            "photo-only", 40, 120, 100, false);
        VideoPhotoLoadObservation mixed = Observation(
            "mixed-with-video", 36, 120, 119, true);
        var comparison = VideoPhotoLoadEvidenceCommandRunner.Compare(
            baseline, mixed, new VideoPhotoLoadLimits(10, 20));
        Assert.True(comparison.WithinOperatorLimits);
        Assert.Equal(5, comparison.Metrics.Length);
        Assert.Equal("photo-jobs-per-minute", comparison.Metrics[0].Name);
        Assert.Equal(10, comparison.Metrics[0].RegressionPercent);
        Assert.Equal(19, comparison.Metrics[1].RegressionPercent);
        Assert.Equal(20, comparison.Metrics[1].BaselineSamples);
        Assert.Equal(20, comparison.Metrics[1].MixedSamples);

        var worse = VideoPhotoLoadEvidenceCommandRunner.Compare(
            baseline, Observation("mixed-with-video", 30, 120, 150, true),
            new VideoPhotoLoadLimits(10, 20));
        Assert.False(worse.WithinOperatorLimits);
        Assert.False(worse.Metrics[0].WithinOperatorLimit);
        Assert.False(worse.Metrics[4].WithinOperatorLimit);
    }

    [Fact]
    public void P95_uses_nearest_rank_and_only_compares_fixed_photo_metrics()
    {
        double[] baselineTimes = Enumerable.Range(1, 20)
            .Select(i => (double)i).ToArray();
        var baseline = Observation("photo-only", 40, 120, 100, false)
            with { VisualSearchMs = baselineTimes };
        var mixed = Observation("mixed-with-video", 40, 120, 100, true)
            with { VisualSearchMs = baselineTimes.Select(x => 2 * x).ToArray() };
        var result = VideoPhotoLoadEvidenceCommandRunner.Compare(
            baseline, mixed, new VideoPhotoLoadLimits(0, 100));
        Assert.Equal(19, result.Metrics[1].Baseline);
        Assert.Equal(38, result.Metrics[1].Mixed);
        Assert.Equal(100, result.Metrics[1].RegressionPercent);
        Assert.True(result.WithinOperatorLimits);
    }

    [Fact]
    public void Strict_numeric_only_schema_refuses_missing_private_or_unmeasured_data()
    {
        var baseReport = VideoPhotoLoadEvidenceCommandRunner.ParseObservation(
            ObservationJson("photo-only", false), "photo-only");
        Assert.False(baseReport.VideoJobObserved);
        Assert.Equal(40, baseReport.PhotoJobsCompleted);
        Assert.Equal(20, baseReport.VisualSearchMs.Length);

        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.ParseObservation(
                ObservationJson("mixed-with-video", false), "mixed-with-video"));
        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.ParseObservation(
                ObservationJson("photo-only", true), "photo-only"));
        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.ParseObservation(
                ObservationJson("mixed-with-video", true), "photo-only"));
        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.ParseObservation(
                Encoding.UTF8.GetBytes(
                    Encoding.UTF8.GetString(ObservationJson("photo-only", false))
                        .Replace("\"windowSeconds\":120", "\"windowSeconds\":2")),
                "photo-only"));
        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.ParseObservation(
                Encoding.UTF8.GetBytes(
                    Encoding.UTF8.GetString(ObservationJson("photo-only", false))
                        .Replace("\"videoJobObserved\":false",
                                 "\"videoJobObserved\":false,\"sourcePath\":\"C:\\\\private\\\\clip.mp4\"")),
                "photo-only"));

        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.ParseLimits(
                LimitsJson(-1, 20)));
        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.ParseLimits(
                LimitsJson(10, 101)));
        Assert.Equal(10,
            VideoPhotoLoadEvidenceCommandRunner.ParseLimits(
                LimitsJson()).MaxThroughputDropPercent);
        Assert.Throws<InvalidDataException>(() =>
            VideoPhotoLoadEvidenceCommandRunner.Compare(
                Observation("photo-only", 40, 120, 100, false),
                Observation("mixed-with-video", 40, 120, 100, false),
                new VideoPhotoLoadLimits(10, 20)));
    }

    [Fact]
    public async Task Offline_runner_has_no_database_or_media_dependency_and_cannot_apply()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "wi0188-load-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string baseline = Path.Combine(root, "baseline.json");
            string mixed = Path.Combine(root, "mixed.json");
            string limits = Path.Combine(root, "limits.json");
            await File.WriteAllBytesAsync(baseline, ObservationJson("photo-only", false));
            await File.WriteAllBytesAsync(mixed, ObservationJson("mixed-with-video", true));
            await File.WriteAllBytesAsync(limits, LimitsJson());
            string[] args =
            [
                "--baseline-file", baseline,
                "--mixed-file", mixed,
                "--limits-file", limits
            ];
            var options = VideoPhotoLoadEvidenceOptions.Parse(args);
            using StringWriter output = new();
            Assert.Equal(0,
                await VideoPhotoLoadEvidenceCommandRunner.RunAsync(options, output));
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            JsonElement result = document.RootElement;
            Assert.True(result.GetProperty("withinOperatorLimits").GetBoolean());
            Assert.True(result.GetProperty("diagnosticOnly").GetBoolean());
            Assert.False(result.GetProperty("wi0188Accepted").GetBoolean());
            Assert.False(result.GetProperty("videoExecutionAuthorized").GetBoolean());
            Assert.False(result.GetProperty("photoWorkloadIdenticalIndependentlyVerified").GetBoolean());
            Assert.Equal(5, result.GetProperty("metrics").GetArrayLength());
            Assert.DoesNotContain(root, output.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Throws<ArgumentException>(() =>
                VideoPhotoLoadEvidenceOptions.Parse([.. args, "--apply"]));
            Assert.Throws<ArgumentException>(() =>
                VideoPhotoLoadEvidenceOptions.Parse([.. args, "--source-file", baseline]));
            Assert.Throws<ArgumentException>(() =>
                VideoPhotoLoadEvidenceOptions.Parse([.. args, "--limits-file", limits]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
