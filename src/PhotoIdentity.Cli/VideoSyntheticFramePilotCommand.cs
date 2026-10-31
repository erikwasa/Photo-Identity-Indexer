using System.Text.Json;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Cli;

/// <summary>
/// Entirely synthetic, opt-in frame decoding exercise. No source path,
/// database, job, OneDrive or model option is accepted.
/// </summary>
public sealed record VideoSyntheticFramePilotOptions(
    string ScratchRoot, string Ffmpeg, string FfmpegSha256, bool Apply)
{
    public static VideoSyntheticFramePilotOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply)
            {
                apply = true;
                continue;
            }
            if (args[i] is not ("--scratch-root" or "--ffmpeg" or "--ffmpeg-sha256") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Synthetic frame pilot accepts only explicit tool and scratch options.");
        }

        string Required(string key) =>
            values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException("Synthetic frame pilot needs explicit tool and scratch settings.");
        string root = Required("--scratch-root");
        string ffmpeg = Required("--ffmpeg");
        string digest = Required("--ffmpeg-sha256");
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(ffmpeg) ||
            digest.Length != 64 ||
            digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Synthetic frame pilot requires absolute local paths and lowercase tool SHA-256.");
        return new(Path.GetFullPath(root), Path.GetFullPath(ffmpeg), digest, apply);
    }
}

public sealed record VideoSyntheticFrameCase(
    string Orientation, int Frames, int Width, int Height,
    long TotalJpegBytes, long ReservedDecodeMilliseconds);

public static class VideoSyntheticFramePilotCommandRunner
{
    // Same native, hash-pinned single-process containment as the older proxy
    // pilot. Every child process and output belongs to a disposable workspace.
    private static VideoToolExecutionBudget Budget => new(
        MaximumWallTime: TimeSpan.FromSeconds(75),
        MaximumTemporaryBytes: 32L * 1024 * 1024,
        MaximumTemporaryEntries: 16,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 15));

    public static async Task<int> RunAsync(
        VideoSyntheticFramePilotOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string root = VideoSyntheticProxyPilotCommandRunner.ValidateRoot(options.ScratchRoot);
        if (!options.Apply)
        {
            Write(output, true, "not-run", [], null);
            return 0;
        }
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Synthetic frame execution requires Windows Job Object containment.");

        var supervisor = new VideoToolProcessSupervisor();
        List<VideoSyntheticFrameCase> cases = [];
        string stage = "scratch-preflight";
        try
        {
            VideoScratchCapacityGuard.EnsureCapacity(root, 128L * 1024 * 1024);
            foreach ((string orientation, int width, int height, bool audio) in
                     new[] { ("landscape", 1280, 720, true),
                             ("portrait", 720, 1280, false) })
            {
                token.ThrowIfCancellationRequested();
                stage = "fixture-generation";
                VideoSyntheticFrameCase? observation = null;
                VideoToolExecutionResult generated = await supervisor.RunAsync(
                    options.Ffmpeg, options.FfmpegSha256, root,
                    workspace => VideoSyntheticProxyPilotCommandRunner.GenerateArguments(
                        workspace, width, height, audio),
                    Budget, token,
                    afterSuccessfulExit: async (generatorWorkspace, generatorToken) =>
                    {
                        string input = Path.Combine(generatorWorkspace, "output.mp4");
                        if (!File.Exists(input))
                            throw new InvalidDataException("Synthetic source fixture was not produced.");
                        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
                            new VideoFrameSamplingRequest(3m, MaximumFrames: 4,
                                MaximumReservedDecodeMilliseconds: 4_000));
                        long bytes = 0;
                        int count = 0;
                        int observedWidth = 0, observedHeight = 0;
                        foreach (VideoFrameSample frame in plan.Samples)
                        {
                            stage = "frame-extraction";
                            VideoToolExecutionResult extracted = await supervisor.RunAsync(
                                options.Ffmpeg, options.FfmpegSha256, root,
                                frameWorkspace => VideoFrameExtractionProfile.BuildArguments(
                                    input, frameWorkspace, frame, plan.DurationMilliseconds),
                                Budget, generatorToken,
                                afterSuccessfulExit: async (frameWorkspace, frameToken) =>
                                {
                                    string jpeg = Path.Combine(frameWorkspace,
                                        VideoFrameExtractionProfile.OutputName);
                                    PhotoIdentity.Core.Imaging.ImageFrame image =
                                        await VideoFrameExtractionProfile.ReadValidatedFrameAsync(
                                            jpeg, frameToken);
                                    if ((width > height && image.Size.Width <= image.Size.Height) ||
                                        (height > width && image.Size.Height <= image.Size.Width))
                                        throw new InvalidDataException("Synthetic frame rotation/aspect was not preserved.");
                                    observedWidth = image.Size.Width;
                                    observedHeight = image.Size.Height;
                                    bytes += new FileInfo(jpeg).Length;
                                    count++;
                                });
                            if (extracted.ExitCode != 0)
                                throw new InvalidDataException("Synthetic frame FFmpeg extraction failed.");
                        }
                        if (count != plan.Samples.Count || bytes <= 0)
                            throw new InvalidDataException("Synthetic frame sample count did not match the plan.");
                        observation = new(orientation, count, observedWidth,
                            observedHeight, bytes, plan.ReservedDecodeMilliseconds);
                    });
                if (generated.ExitCode != 0 || observation is null)
                    throw new InvalidDataException("Synthetic frame source fixture failed.");
                cases.Add(observation);
            }
            Write(output, false, "validated", cases, null);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Never emit native stderr, tool arguments, exception messages or
            // paths: this command is safe to share as aggregate-only evidence.
            Write(output, false, "pilot-failed", cases,
                stage + ":" + VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(error));
            return 1;
        }
    }

    private static void Write(TextWriter output, bool dryRun, string status,
        IReadOnlyList<VideoSyntheticFrameCase> results, string? failureCode)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-synthetic-frame-pilot-v1",
            dryRun, status, failureCode,
            originalsOpened = false, oneDriveHydrationRequested = false,
            schemaUsed = false, videoWorkersActivated = false,
            imagesPersisted = false, results
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
