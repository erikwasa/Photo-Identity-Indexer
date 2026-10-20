using System.Text.Json;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Cli;

/// <summary>
/// Video-only operator cleanup. Never touches catalogue records, media
/// originals, photo derivatives or directories without an ownership marker.
/// </summary>
public sealed record VideoScratchCleanupOptions(
    string ScratchRoot, TimeSpan MinimumAge, int MaximumDirectories, bool Apply)
{
    public static VideoScratchCleanupOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            if (args[i] is not ("--scratch-root" or "--min-age-hours" or
                                "--max-directories") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--") ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid, repeated or incomplete video scratch cleanup option.");
        }

        if (!values.TryGetValue("--scratch-root", out string? scratch) ||
            string.IsNullOrWhiteSpace(scratch) || !Path.IsPathFullyQualified(scratch))
            throw new ArgumentException("Specify an existing, absolute --scratch-root.");
        int age = ParseBound(values, "--min-age-hours", 48, 24, 720);
        int count = ParseBound(values, "--max-directories", 64, 1, 256);
        return new(Path.GetFullPath(scratch), TimeSpan.FromHours(age), count, apply);
    }

    private static int ParseBound(Dictionary<string, string> values,
        string key, int fallback, int minimum, int maximum)
    {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        if (!int.TryParse(raw, out int n) || n < minimum || n > maximum)
            throw new ArgumentException(key + " exceeds the safe operator range.");
        return n;
    }
}

public static class VideoScratchCleanupCommandRunner
{
    public static async Task<int> RunAsync(
        VideoScratchCleanupOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        VideoScratchSweepResult report = await VideoScratchJanitor.SweepAsync(
            options.ScratchRoot, options.MinimumAge, options.MaximumDirectories,
            options.Apply, token);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-video-scratch-cleanup-v1",
            dryRun = report.DryRun,
            report.Considered, report.Eligible, report.Deleted, report.Refused
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return 0;
    }
}
