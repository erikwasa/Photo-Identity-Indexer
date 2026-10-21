using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Cli;

/// <summary>
/// Strictly synthetic, database-free codec contract experiment. This is NOT
/// video job admission, an archive processing command or a worker enablement.
/// Both generated input and validated output are deleted from marked scratch.
/// </summary>
public sealed record VideoSyntheticProxyPilotOptions(
    string ScratchRoot, string Ffmpeg, string FfmpegSha256,
    string Ffprobe, string FfprobeSha256, bool Apply)
{
    public static VideoSyntheticProxyPilotOptions Parse(string[] args)
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
            if (args[i] is not ("--scratch-root" or "--ffmpeg" or "--ffmpeg-sha256" or
                               "--ffprobe" or "--ffprobe-sha256") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid, repeated or incomplete synthetic video pilot argument.");
        }

        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException(
                    "Synthetic video pilot requires all explicit tool and scratch settings.");
        string root = Required("--scratch-root");
        string ffmpeg = Required("--ffmpeg");
        string ffprobe = Required("--ffprobe");
        string encodeHash = Required("--ffmpeg-sha256");
        string probeHash = Required("--ffprobe-sha256");
        if (!Path.IsPathFullyQualified(root) ||
            !Path.IsPathFullyQualified(ffmpeg) ||
            !Path.IsPathFullyQualified(ffprobe) ||
            !IsHash(encodeHash) || !IsHash(probeHash))
            throw new ArgumentException(
                "Video pilot needs absolute tool/scratch paths and lowercase approved SHA-256 values.");
        return new(Path.GetFullPath(root), Path.GetFullPath(ffmpeg), encodeHash,
            Path.GetFullPath(ffprobe), probeHash, apply);
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record VideoSyntheticProxyPilotCase(
    string Orientation, int Width, int Height, bool HasAudio,
    double DurationSeconds, long EncodedBytes, long EncodeMilliseconds);

/// <summary>
/// Generates bounded 3-second camera-like synthetic inputs via a trusted
/// operator-supplied FFmpeg; runs the SAME candidate encoder argument array
/// as the unregistered worker; validates the MP4 with the independent
/// hash-pinned FFprobe/atom verifier; and removes every generated file.
/// No original media path, PostgreSQL credentials, jobs or OneDrive API exists
/// in this command. --apply authorizes only transient synthetic test files.
/// </summary>
public static class VideoSyntheticProxyPilotCommandRunner
{
    public const string RootMarker = ".photoidentity-synthetic-video-lab";
    public const string MarkerText = "wi0188-synthetic-video-lab-v1";

    private static VideoToolExecutionBudget EncodeBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(120),
        MaximumTemporaryBytes: 32L * 1024 * 1024,
        MaximumTemporaryEntries: 16,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 15,
            // Only synthetic fixtures opt in until Windows CI + maintained
            // toolchain validate the native mitigation attributes.
            RequireProcessCreationMitigations: true));

    public static async Task<int> RunAsync(
        VideoSyntheticProxyPilotOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        // Even preview requires a dedicated explicit local fixture root
        // beneath the OS temp directory, never a source archive, OneDrive
        // collection, conventional derivatives folder or network share.
        string root = ValidateRoot(options.ScratchRoot);
        if (!options.Apply)
        {
            Write(output, true, "not-run", []);
            return 0;
        }
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Synthetic video execution requires Windows Job Object containment.");

        var encoder = new VideoToolProcessSupervisor();
        var validator = new VideoProxyOutputValidator(
            options.Ffprobe, options.FfprobeSha256, root);
        List<VideoSyntheticProxyPilotCase> observations = [];
        // Only predetermined, public phase identifiers are reported. Tool
        // stderr, executable locations and private workspace names are never
        // emitted even if a binary or probe fails.
        string stage = "scratch-preflight";
        string orientationForReport = "landscape";
        int? toolExitCode = null;
        try
        {
            VideoScratchCapacityGuard.EnsureCapacity(root, 128L * 1024 * 1024);
            foreach ((string orientation, int width, int height, bool audio)
                     in new[] { ("landscape", 1280, 720, true),
                                ("portrait", 720, 1280, false) })
            {
                token.ThrowIfCancellationRequested();
                orientationForReport = orientation;
                stage = "fixture-generation";
                toolExitCode = null;
                VideoSyntheticProxyPilotCase? observed = null;
                VideoToolExecutionResult generator = await encoder.RunAsync(
                    options.Ffmpeg, options.FfmpegSha256, root,
                    workspace => GenerateArguments(workspace, width, height, audio),
                    EncodeBudget, token,
                    afterSuccessfulExit: async (generatorWorkspace, generatedToken) =>
                    {
                        // This input was JUST GENERATED in an owned private
                        // workspace. It is not a user-selected/original media
                        // path and will vanish with the outer workspace.
                        string input = Path.Combine(generatorWorkspace, "output.mp4");
                        if (!File.Exists(input))
                            throw new InvalidDataException(
                                "Synthetic fixture generator did not produce an input.");
                        stage = "proxy-encode";
                        Stopwatch clock = Stopwatch.StartNew();
                        VideoToolExecutionResult transcode = await encoder.RunAsync(
                            options.Ffmpeg, options.FfmpegSha256, root,
                            encodeWorkspace =>
                                VideoProxyCandidateProfile.BuildArguments(input, encodeWorkspace),
                            EncodeBudget, generatedToken,
                            afterSuccessfulExit: async (encodeWorkspace, encodeToken) =>
                            {
                                // The encoder has exited. Do not include the
                                // independent FFprobe pass in encode duration.
                                clock.Stop();
                                string candidate = Path.Combine(encodeWorkspace, "output.mp4");
                                stage = "independent-probe";
                                VideoProxyInspection valid = await validator.ValidateAsync(
                                    candidate,
                                    _ => Task.FromResult(VideoExecutionPermit.Ready),
                                    encodeToken);
                                stage = "output-contract";
                                if (valid.HasAudio != audio ||
                                    valid.DurationSeconds is < 2.5 or > 3.5 ||
                                    (orientation == "portrait" && valid.Height <= valid.Width) ||
                                    (orientation == "landscape" && valid.Width <= valid.Height))
                                    throw new InvalidDataException(
                                        "Synthetic proxy did not preserve its expected orientation, duration or audio policy.");
                                long bytes = new FileInfo(candidate).Length;
                                observed = new(orientation, valid.Width, valid.Height,
                                    valid.HasAudio, valid.DurationSeconds, bytes,
                                    (long)clock.Elapsed.TotalMilliseconds);
                            });
                        if (transcode.ExitCode != 0)
                        {
                            stage = "proxy-encode";
                            toolExitCode = transcode.ExitCode;
                            throw new InvalidDataException("Synthetic proxy encoder returned a nonzero exit code.");
                        }
                        if (observed is null)
                            throw new InvalidDataException(
                                "Synthetic transcode or independent verification failed.");
                    });

                if (generator.ExitCode != 0)
                {
                    stage = "fixture-generation";
                    toolExitCode = generator.ExitCode;
                    throw new InvalidDataException("Synthetic fixture generator returned a nonzero exit code.");
                }
                if (observed is null)
                    throw new InvalidDataException(
                        "Synthetic fixture generator or codec validation failed.");
                observations.Add(observed);
            }

            Write(output, false, "validated", observations);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Only allowlisted reason classes, phase and numeric tool/Win32
            // status appear here. Never include exception.Message, stack
            // traces, raw stdout/stderr, source names or media paths.
            Write(output, false, "pilot-failed", observations,
                stage, orientationForReport, ClassifyFailure(error),
                toolExitCode ??
                    (error is VideoProxyValidationException probe
                        ? probe.ToolExitCode : null),
                error is Win32Exception native ? native.NativeErrorCode : null,
                VideoToolFailureEvidence.Read(error));
            return 1;
        }
    }

    public static string ValidateRoot(string scratchRoot)
    {
        if (!Path.IsPathFullyQualified(scratchRoot) ||
            !Directory.Exists(scratchRoot))
            throw new ArgumentException("Create a dedicated absolute synthetic video scratch root first.");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchRoot));
        string osTemp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (OperatingSystem.IsWindows() && root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Synthetic video scratch may not be a network path.");
        if (!root.StartsWith(osTemp + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException(
                "Synthetic video scratch must be a dedicated child of the system temp root.");

        for (string? directory = root; directory is not null;
             directory = Path.GetDirectoryName(directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked synthetic video scratch directories are not allowed.");
            if (string.Equals(directory, osTemp, comparison)) break;
        }

        string marker = Path.Combine(root, RootMarker);
        if (!File.Exists(marker) ||
            (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(marker).Length != MarkerText.Length ||
            File.ReadAllText(marker) != MarkerText)
            throw new InvalidOperationException(
                "Synthetic video scratch ownership marker is absent or invalid.");

        // The operator's root is not a general-purpose cache or archive.
        // Its only child before work begins must be the exact marker.
        if (Directory.EnumerateFileSystemEntries(root).Any(entry =>
                !string.Equals(Path.GetFileName(entry), RootMarker,
                    StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Synthetic video scratch must be otherwise empty.");
        return root;
    }

    public static IReadOnlyList<string> GenerateArguments(
        string workspace, int width, int height, bool audio)
    {
        bool approvedFixture =
            (width == 1280 && height == 720 && audio) ||
            (width == 720 && height == 1280 && !audio);
        if (!Path.IsPathFullyQualified(workspace) || !approvedFixture)
            throw new ArgumentException("Only the two fixed generated video fixtures are permitted.");

        List<string> args =
        [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-protocol_whitelist", "file",
            "-threads", "1", "-filter_threads", "1",
            "-f", "lavfi", "-i",
            "testsrc2=size=" + width.ToString(CultureInfo.InvariantCulture) +
                "x" + height.ToString(CultureInfo.InvariantCulture) +
                ":rate=24:duration=3"
        ];
        if (audio)
            args.AddRange([
                "-f", "lavfi", "-i",
                "sine=frequency=440:sample_rate=48000:duration=3"
            ]);
        args.AddRange([
            "-map", "0:v:0"
        ]);
        if (audio) args.AddRange(["-map", "1:a:0"]);
        args.AddRange([
            "-t", "3", "-c:v", "libx264", "-preset", "ultrafast",
            "-crf", "28", "-pix_fmt", "yuv420p",
            "-color_primaries", "bt709", "-color_trc", "bt709",
            "-colorspace", "bt709"
        ]);
        if (audio)
            args.AddRange(["-c:a", "aac", "-b:a", "96k"]);
        args.AddRange([
            "-movflags", "+faststart", "-f", "mp4", "-fs", "33554432",
            Path.Combine(workspace, "output.mp4")
        ]);
        return args;
    }

    public static string ClassifyFailure(Exception error) => error switch
    {
        VideoProxyValidationException validation => validation.ReasonCode,
        Win32Exception => "native-process-setup",
        TimeoutException => "timeout",
        UnauthorizedAccessException => "filesystem-access-denied",
        FileNotFoundException => "required-file-missing",
        DirectoryNotFoundException => "required-directory-missing",
        IOException => "filesystem-io",
        InvalidDataException => "invalid-media-evidence",
        ArgumentException => "configuration-rejected",
        InvalidOperationException => "execution-safety-rejected",
        OperationCanceledException => "cancelled",
        _ => "unexpected-failure"
    };

    private static void Write(
        TextWriter output, bool dryRun, string status,
        IReadOnlyList<VideoSyntheticProxyPilotCase> results,
        string? failureStage = null, string? failureOrientation = null,
        string? failureClass = null, int? toolExitCode = null,
        int? nativeErrorCode = null, string? failureCheckpoint = null)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-synthetic-proxy-pilot-v1",
            dryRun, status, schemaUsed = false, originalsOpened = false,
            videoWorkersActivated = false, derivativesRetained = false,
            failureStage, failureOrientation, failureClass,
            toolExitCode, nativeErrorCode, failureCheckpoint, results
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
