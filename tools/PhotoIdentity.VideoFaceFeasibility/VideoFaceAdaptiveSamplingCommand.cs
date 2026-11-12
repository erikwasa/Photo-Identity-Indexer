using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Source-free JSON command for the opt-in PowerShell coverage extractor.
/// The command NEVER opens a video, probes the catalogue, extracts frames,
/// or writes private files. Standard output is solely a bounded timestamp plan.
/// </summary>
public static class VideoFaceAdaptiveSamplingCommand
{
    private const int MaximumCueFileBytes = 16 * 1024;
    private sealed record CueFile(int SchemaVersion, List<VideoFaceSamplingCue>? Cues);

    private static readonly JsonSerializerOptions CueJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<VideoFaceSamplingCueKind>(
                namingPolicy: null, allowIntegerValues: false),
        },
    };

    public static VideoFaceSamplingPlan CreatePlan(
        long clipDurationMs, long windowStartMs,
        IReadOnlyList<VideoFaceSamplingCue>? cues = null)
    {
        VideoFaceSamplingPlan coarse = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(
            clipDurationMs, windowStartMs);
        return cues is null
            ? coarse
            : VideoFaceAdaptiveSamplingPlanner.Refine(coarse, cues);
    }

    /// <summary>
    /// Strict local JSON input containing only event cues at timestamps of
    /// already-planned frames. Never accepts paths, vectors or identities in JSON.
    /// </summary>
    public static async Task<IReadOnlyList<VideoFaceSamplingCue>> LoadCuesAsync(
        string path, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified local cue-file path is required.");
        }

        FileInfo info = new(Path.GetFullPath(path));
        const FileAttributes blocked = FileAttributes.ReparsePoint | FileAttributes.Offline;
        const long recallAttributes = 0x00040000 | 0x00400000;
        if (!info.Exists || info.Length is < 1 or > MaximumCueFileBytes ||
            ((long)info.Attributes & ((long)blocked | recallAttributes)) != 0 ||
            info.Directory is null ||
            ((long)info.Directory.Attributes & ((long)blocked | recallAttributes)) != 0)
        {
            throw new ArgumentException("Local cue file is missing, linked, offline or oversized.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(info.FullName, cancellationToken);
        if (bytes.Length is < 1 or > MaximumCueFileBytes)
        {
            throw new ArgumentException("Invalid cue-file byte count.");
        }

        CueFile? input = JsonSerializer.Deserialize<CueFile>(bytes, CueJsonOptions);
        if (input?.SchemaVersion != 1 ||
            input.Cues is null ||
            input.Cues.Count > VideoFaceAdaptiveSamplingPlanner.MaximumCuesPerWindow ||
            input.Cues.Any(c => c is null || !Enum.IsDefined(c.Kind)))
        {
            throw new ArgumentException("Require schemaVersion 1 and bounded valid sampling cues.");
        }

        return input.Cues;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            long? duration = null;
            long start = 0;
            bool startSpecified = false;
            string? cuePath = null;
            bool commandSeen = false;
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (name == "--adaptive-sampling-plan")
                {
                    if (commandSeen) throw new ArgumentException("Duplicate command.");
                    commandSeen = true;
                    continue;
                }

                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                {
                    throw new ArgumentException("Invalid sampling option.");
                }

                string value = args[i];
                switch (name)
                {
                    case "--duration-ms" when duration is null &&
                        long.TryParse(value, NumberStyles.None,
                            CultureInfo.InvariantCulture, out long parsedDuration):
                        duration = parsedDuration;
                        break;
                    case "--window-start-ms" when !startSpecified &&
                        long.TryParse(value, NumberStyles.None,
                            CultureInfo.InvariantCulture, out long parsedStart):
                        start = parsedStart;
                        startSpecified = true;
                        break;
                    case "--private-cues" when cuePath is null:
                        cuePath = value;
                        break;
                    default:
                        throw new ArgumentException("Unknown or repeated sampling option.");
                }
            }

            if (!commandSeen || duration is null)
                throw new ArgumentException("Sampling duration is required.");

            IReadOnlyList<VideoFaceSamplingCue>? cues = cuePath is null
                ? null
                : await LoadCuesAsync(cuePath);

            VideoFaceSamplingPlan plan = CreatePlan(duration.Value, start, cues);
            // No media paths, catalogue people, cue details, vectors, or human
            // labels ever enter stdout. Frame timestamps are still private
            // when called for a real clip: the extractor captures this output.
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                clipDurationMs = plan.ClipDurationMs,
                windowStartMs = plan.WindowStartMs,
                windowEndMs = plan.WindowEndMs,
                frameTimestampsMs = plan.FrameTimestampsMs,
                coarseFrameCount = plan.CoarseFrameCount,
                hasMoreWindows = plan.HasMoreWindows,
                refinementBudgetReached = plan.RefinementBudgetReached,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch
        {
            // Never expose cue paths or private input values on error.
            Console.Error.WriteLine(
                "error: private adaptive sampling request was invalid or unavailable.");
            return 1;
        }
    }
}
