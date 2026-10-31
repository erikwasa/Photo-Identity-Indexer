using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Cli;

/// <summary>
/// Explicit private offline JSON dataset; never a source video path, cloud
/// URL, database, or deployed search API. The report never reflects input
/// IDs, vectors, model digests, filenames or arbitrary JSON strings.
/// </summary>
public sealed record VideoFrameRetrievalEvaluationOptions(
    string ManifestPath, int FrameTopK = 3, int RankingK = 5)
{
    public static VideoFrameRetrievalEvaluationOptions Parse(string[] args)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--manifest" or "--frame-top-k" or "--rank-k") ||
                i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !fields.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unknown or duplicate offline evaluation option.");
        }
        if (!fields.TryGetValue("--manifest", out string? file) ||
            string.IsNullOrWhiteSpace(file) ||
            !Path.IsPathFullyQualified(file) ||
            (OperatingSystem.IsWindows() && file.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException(
                "An explicit absolute local evaluation manifest is required.");

        int ParseBound(string key, int fallback, int maximum)
        {
            if (!fields.TryGetValue(key, out string? text))
                return fallback;
            if (!int.TryParse(text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int count) || count < 1 || count > maximum)
                throw new ArgumentException("Offline evaluation ranking bound is invalid.");
            return count;
        }

        return new(Path.GetFullPath(file),
            ParseBound("--frame-top-k", 3, VideoFrameSamplingPlanner.HardMaximumFrames),
            ParseBound("--rank-k", 5, VideoFrameRetrievalEvaluation.MaximumClips));
    }
}

public static class VideoFrameRetrievalEvaluationCommandRunner
{
    public const int MaximumManifestBytes = 16 * 1024 * 1024;

    public static async Task<int> RunAsync(
        VideoFrameRetrievalEvaluationOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        // Fail closed before opening any files or producing a JSON report.
        if (!Path.IsPathFullyQualified(options.ManifestPath) ||
            (OperatingSystem.IsWindows() &&
             options.ManifestPath.StartsWith(@"\\", StringComparison.Ordinal)) ||
            options.FrameTopK is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
            options.RankingK is < 1 or > VideoFrameRetrievalEvaluation.MaximumClips)
            throw new ArgumentException("Offline evaluation options are invalid.");

        VideoFrameRetrievalEvaluationManifest manifest;
        try
        {
            FileAttributes attributes = File.GetAttributes(options.ManifestPath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("Offline manifest must be a physical regular file.");
            await using var input = new FileStream(options.ManifestPath,
                FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            if (input.Length is < 1 or > MaximumManifestBytes)
                throw new InvalidDataException("Private manifest exceeds bounded input size.");
            byte[] data = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(data, token);
            if (await input.ReadAsync(new byte[1], token) != 0)
                throw new InvalidDataException("Private manifest changed while reading.");

            var settings = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                MaxDepth = 12
            };
            manifest = JsonSerializer.Deserialize<
                VideoFrameRetrievalEvaluationManifest>(data, settings)
                ?? throw new InvalidDataException("Retrieval manifest is missing.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (
            error is IOException or InvalidDataException or UnauthorizedAccessException
                or JsonException or NotSupportedException)
        {
            // Never print a private manifest path or content excerpt supplied
            // by native/JSON parser exceptions.
            throw new InvalidOperationException(
                "Private offline retrieval manifest is unavailable or invalid.");
        }

        VideoFrameRetrievalEvaluationResult result =
            VideoFrameRetrievalEvaluation.Evaluate(
                manifest, options.FrameTopK, options.RankingK);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = VideoFrameRetrievalEvaluation.ReportProtocol,
            offline = true,
            readOnly = true,
            manualRelevanceJudgments = true,
            modelProvenanceSelfReported = true,
            result,
            originalsOpened = false,
            oneDriveHydrationRequested = false,
            mediaDecoded = false,
            captionsReturned = false,
            embeddingsReturned = false,
            videoJobsEnqueued = false,
            schemaChanged = false,
            modelInferencePerformed = false,
            networkRequestsMade = false
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return 0;
    }
}
