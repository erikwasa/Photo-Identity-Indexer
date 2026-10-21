using System.Text.Json;

namespace PhotoIdentity.Cli;

/// <summary>
/// Offline, source-free WI-0188 performance comparison. This never connects
/// to a database, sends requests, opens archives, starts FFmpeg or authorizes
/// a video lane. Inputs are deliberately numeric, bounded, and manually
/// reviewed: successful math is NOT proof that a mixed trial took place.
/// </summary>
public sealed record VideoPhotoLoadEvidenceOptions(
    string BaselineFile, string MixedFile, string LimitsFile)
{
    public static VideoPhotoLoadEvidenceOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--baseline-file" or "--mixed-file" or "--limits-file") ||
                i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException(
                    "Photo load evidence accepts only three explicit local JSON file options.");
        }
        string Required(string key)
        {
            if (!values.TryGetValue(key, out string? path) ||
                string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) ||
                (OperatingSystem.IsWindows() &&
                 path.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException(
                    "Photo load evidence requires three local absolute files.");
            return Path.GetFullPath(path);
        }
        return new(Required("--baseline-file"), Required("--mixed-file"),
            Required("--limits-file"));
    }
}

public sealed record VideoPhotoLoadObservation(
    string Phase, int PhotoJobsCompleted, double WindowSeconds,
    bool VideoJobObserved, double[] VisualSearchMs,
    double[] CaptionSearchMs, double[] CombinedSearchMs,
    double[] SlideshowAdvanceMs);

public sealed record VideoPhotoLoadLimits(
    double MaxThroughputDropPercent, double MaxP95IncreasePercent);

public sealed record VideoPhotoLoadMetric(
    string Name, int BaselineSamples, int MixedSamples,
    double Baseline, double Mixed, double RegressionPercent,
    bool WithinOperatorLimit);

public sealed record VideoPhotoLoadComparison(
    bool WithinOperatorLimits, VideoPhotoLoadMetric[] Metrics);

public static class VideoPhotoLoadEvidenceCommandRunner
{
    private const string Protocol = "wi0188-photo-load-observation-v1";
    private const string LimitsProtocol = "wi0188-photo-load-operator-limits-v1";
    private const int MaxFileBytes = 65536;

    public static async Task<int> RunAsync(
        VideoPhotoLoadEvidenceOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        VideoPhotoLoadObservation baseline =
            ParseObservation(await ReadBoundedAsync(options.BaselineFile, token),
                "photo-only");
        VideoPhotoLoadObservation mixed =
            ParseObservation(await ReadBoundedAsync(options.MixedFile, token),
                "mixed-with-video");
        VideoPhotoLoadLimits limits =
            ParseLimits(await ReadBoundedAsync(options.LimitsFile, token));

        VideoPhotoLoadComparison report = Compare(baseline, mixed, limits);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-photo-load-comparison-v1",
            diagnosticOnly = true,
            evidenceProvenance = "operator-supplied",
            photoWorkloadIdenticalIndependentlyVerified = false,
            independentVideoProcessObservationVerified = false,
            videoExecutionAuthorized = false,
            wi0188Accepted = false,
            withinOperatorLimits = report.WithinOperatorLimits,
            metrics = report.Metrics
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return report.WithinOperatorLimits ? 0 : 1;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(path) ||
            (OperatingSystem.IsWindows() &&
             path.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException("Benchmark inputs must be local absolute files.");
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length is < 2 or > MaxFileBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Benchmark input is missing, linked or exceeds size limits.");
            byte[] bytes = await File.ReadAllBytesAsync(path, token);
            if (bytes.Length > MaxFileBytes)
                throw new InvalidDataException("Benchmark input exceeds size limits.");
            return bytes;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            throw new InvalidDataException("Benchmark file could not be read safely.");
        }
    }

    public static VideoPhotoLoadObservation ParseObservation(
        ReadOnlyMemory<byte> utf8, string expectedPhase)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(utf8);
            JsonElement root = doc.RootElement;
            RequireObject(root,
            [
                "protocol", "phase", "photoJobsCompleted", "windowSeconds",
                "videoJobObserved", "visualSearchMs", "captionSearchMs",
                "combinedSearchMs", "slideshowAdvanceMs"
            ]);
            if (root.GetProperty("protocol").GetString() != Protocol ||
                root.GetProperty("phase").GetString() != expectedPhase)
                throw Invalid();

            int jobs = root.GetProperty("photoJobsCompleted").GetInt32();
            double duration = root.GetProperty("windowSeconds").GetDouble();
            bool video = root.GetProperty("videoJobObserved").GetBoolean();
            if (jobs is < 1 or > 100000 || !double.IsFinite(duration) ||
                duration is < 60 or > 3600 || video != (expectedPhase == "mixed-with-video"))
                throw Invalid();

            return new(expectedPhase, jobs, duration, video,
                Samples(root.GetProperty("visualSearchMs")),
                Samples(root.GetProperty("captionSearchMs")),
                Samples(root.GetProperty("combinedSearchMs")),
                Samples(root.GetProperty("slideshowAdvanceMs")));
        }
        catch (Exception e) when (
            e is JsonException or KeyNotFoundException or FormatException or
                 InvalidOperationException or OverflowException)
        {
            throw Invalid();
        }
    }

    public static VideoPhotoLoadLimits ParseLimits(ReadOnlyMemory<byte> utf8)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(utf8);
            JsonElement root = doc.RootElement;
            RequireObject(root,
                ["protocol", "maxThroughputDropPercent", "maxP95IncreasePercent"]);
            if (root.GetProperty("protocol").GetString() != LimitsProtocol)
                throw Invalid();
            double throughput = root.GetProperty("maxThroughputDropPercent").GetDouble();
            double latency = root.GetProperty("maxP95IncreasePercent").GetDouble();
            if (!double.IsFinite(throughput) || throughput is < 0 or > 100 ||
                !double.IsFinite(latency) || latency is < 0 or > 100)
                throw Invalid();
            return new(throughput, latency);
        }
        catch (Exception e) when (
            e is JsonException or KeyNotFoundException or FormatException or
                 InvalidOperationException or OverflowException)
        {
            throw Invalid();
        }
    }

    private static double[] Samples(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() is < 20 or > 200)
            throw Invalid();
        double[] values = array.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        if (values.Any(x => !double.IsFinite(x) || x is <= 0 or > 120000))
            throw Invalid();
        return values;
    }

    private static void RequireObject(JsonElement obj, string[] known)
    {
        if (obj.ValueKind != JsonValueKind.Object ||
            obj.EnumerateObject().Count() != known.Length ||
            obj.EnumerateObject().Any(x => !known.Contains(x.Name, StringComparer.Ordinal)) ||
            known.Any(x => !obj.TryGetProperty(x, out _)))
            throw Invalid();
    }

    private static InvalidDataException Invalid() =>
        new("Photo load evidence is incomplete, malformed, inconsistent or unbounded.");

    public static VideoPhotoLoadComparison Compare(
        VideoPhotoLoadObservation baseline,
        VideoPhotoLoadObservation mixed,
        VideoPhotoLoadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(mixed);
        ArgumentNullException.ThrowIfNull(limits);
        if (baseline.Phase != "photo-only" || baseline.VideoJobObserved ||
            mixed.Phase != "mixed-with-video" || !mixed.VideoJobObserved)
            throw Invalid();
        // Revalidate even for programmatically constructed observations.
        if (baseline.PhotoJobsCompleted < 1 || mixed.PhotoJobsCompleted < 1 ||
            baseline.WindowSeconds is < 60 or > 3600 ||
            mixed.WindowSeconds is < 60 or > 3600 ||
            !double.IsFinite(baseline.WindowSeconds) ||
            !double.IsFinite(mixed.WindowSeconds) ||
            limits.MaxP95IncreasePercent is < 0 or > 100 ||
            limits.MaxThroughputDropPercent is < 0 or > 100 ||
            !double.IsFinite(limits.MaxP95IncreasePercent) ||
            !double.IsFinite(limits.MaxThroughputDropPercent))
            throw Invalid();

        static double P95(double[] values)
        {
            if (values is null || values.Length is < 20 or > 200 ||
                values.Any(x => !double.IsFinite(x) || x is <= 0 or > 120000))
                throw Invalid();
            double[] sorted = [.. values.OrderBy(x => x)];
            return sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
        }

        VideoPhotoLoadMetric Delta(string name, int countA, int countB,
            double a, double b, bool throughput)
        {
            double regression = Math.Max(0, throughput
                ? 100 * (a - b) / a : 100 * (b - a) / a);
            double maximum = throughput
                ? limits.MaxThroughputDropPercent
                : limits.MaxP95IncreasePercent;
            return new(name, countA, countB,
                Math.Round(a, 4), Math.Round(b, 4),
                Math.Round(regression, 3), regression <= maximum);
        }

        VideoPhotoLoadMetric[] metrics =
        [
            Delta("photo-jobs-per-minute",
                baseline.PhotoJobsCompleted, mixed.PhotoJobsCompleted,
                baseline.PhotoJobsCompleted / baseline.WindowSeconds * 60,
                mixed.PhotoJobsCompleted / mixed.WindowSeconds * 60, true),
            Delta("visual-search-p95-ms", baseline.VisualSearchMs.Length,
                mixed.VisualSearchMs.Length, P95(baseline.VisualSearchMs),
                P95(mixed.VisualSearchMs), false),
            Delta("caption-search-p95-ms", baseline.CaptionSearchMs.Length,
                mixed.CaptionSearchMs.Length, P95(baseline.CaptionSearchMs),
                P95(mixed.CaptionSearchMs), false),
            Delta("combined-search-p95-ms", baseline.CombinedSearchMs.Length,
                mixed.CombinedSearchMs.Length, P95(baseline.CombinedSearchMs),
                P95(mixed.CombinedSearchMs), false),
            Delta("slideshow-advance-p95-ms", baseline.SlideshowAdvanceMs.Length,
                mixed.SlideshowAdvanceMs.Length,
                P95(baseline.SlideshowAdvanceMs),
                P95(mixed.SlideshowAdvanceMs), false)
        ];
        return new(metrics.All(x => x.WithinOperatorLimit), metrics);
    }
}
