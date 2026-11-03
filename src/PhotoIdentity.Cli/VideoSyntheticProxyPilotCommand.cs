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
    string Ffprobe, string FfprobeSha256, bool Apply,
    string EncodingProfile = VideoProxyEncodingProfiles.StandardKey,
    bool FullRangeEvaluation = false)
{
    public static VideoSyntheticProxyPilotOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false, fullRangeEvaluation = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply)
            {
                apply = true;
                continue;
            }
            if (args[i] == "--full-range-evaluation" && !fullRangeEvaluation)
            {
                fullRangeEvaluation = true;
                continue;
            }
            if (args[i] is not ("--scratch-root" or "--ffmpeg" or "--ffmpeg-sha256" or
                               "--ffprobe" or "--ffprobe-sha256" or
                               "--profile") ||
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
        if (fullRangeEvaluation && values.ContainsKey("--profile"))
            throw new ArgumentException(
                "Full-range synthetic evaluation cannot select a production preset.");
        string profile = fullRangeEvaluation
            ? VideoRec709FullRangeEvaluationProfile.Version
            : values.TryGetValue("--profile", out string? requested)
                ? requested : VideoProxyEncodingProfiles.StandardKey;
        if (!fullRangeEvaluation)
            _ = VideoProxyEncodingProfiles.Resolve(profile);
        return new(Path.GetFullPath(root), Path.GetFullPath(ffmpeg), encodeHash,
            Path.GetFullPath(ffprobe), probeHash, apply, profile, fullRangeEvaluation);
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record VideoSyntheticProxyPilotCase(
    string Orientation, int Width, int Height, bool HasAudio,
    double DurationSeconds, long EncodedBytes, long EncodeMilliseconds,
    string EncodingProfile = VideoProxyEncodingProfiles.StandardKey);

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

    private static VideoToolExecutionBudget SyntheticSourceProbeBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(15),
        MaximumTemporaryBytes: 128 * 1024,
        MaximumTemporaryEntries: 8,
        InspectionInterval: TimeSpan.FromMilliseconds(100),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 512, MaximumProcesses: 1,
            MaximumCpuPercent: 25,
            MaximumObservedReadBytes: 128L * 1024 * 1024,
            MaximumObservedWriteBytes: 32L * 1024 * 1024));

    private static VideoToolExecutionBudget EncodeBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(120),
        MaximumTemporaryBytes: 32L * 1024 * 1024,
        MaximumTemporaryEntries: 16,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 15));

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
        bool fullRangeEvaluation = options.FullRangeEvaluation;
        VideoProxyEncodingProfile? profile = fullRangeEvaluation
            ? null : VideoProxyEncodingProfiles.Resolve(options.EncodingProfile);
        if (fullRangeEvaluation &&
            options.EncodingProfile != VideoRec709FullRangeEvaluationProfile.Version)
            throw new ArgumentException("Full-range evaluation requires its exact fixed profile.");
        string profileLabel = fullRangeEvaluation
            ? VideoRec709FullRangeEvaluationProfile.Version : profile!.Key;
        if (!options.Apply)
        {
            Write(output, true, "not-run", [], profile: profileLabel);
            return 0;
        }
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Synthetic video execution requires Windows Job Object containment.");

        var encoder = new VideoToolProcessSupervisor();
        var validator = new VideoProxyOutputValidator(
            options.Ffprobe, options.FfprobeSha256, root,
            fullRangeEvaluation ? 720 : profile!.MaximumDimension,
            requireLimitedRange: fullRangeEvaluation);
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
                    workspace => GenerateArguments(workspace, width, height, audio,
                        fullRangeEvaluation),
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
                        if (fullRangeEvaluation)
                        {
                            stage = "synthetic-source-range-probe";
                            await VerifySyntheticFullRangeSourceAsync(
                                options.Ffprobe, options.FfprobeSha256,
                                root, input, width, height, audio, generatedToken);
                        }
                        stage = "proxy-encode";
                        Stopwatch clock = Stopwatch.StartNew();
                        VideoToolExecutionResult transcode = await encoder.RunAsync(
                            options.Ffmpeg, options.FfmpegSha256, root,
                            encodeWorkspace => fullRangeEvaluation
                                ? VideoRec709FullRangeEvaluationProfile.BuildArguments(
                                    input, encodeWorkspace)
                                : profile!.BuildArguments(input, encodeWorkspace),
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
                                    (long)clock.Elapsed.TotalMilliseconds,
                                    profileLabel);
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

            Write(output, false, "validated", observations, profile: profileLabel);
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
                VideoToolFailureEvidence.Read(error),
                profile: profileLabel);
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
        string workspace, int width, int height, bool audio,
        bool fullRangeEvaluation = false)
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
        if (fullRangeEvaluation)
            args.AddRange([
                "-color_range", "pc",
                "-x264-params", "range=pc:colorprim=bt709:transfer=bt709:colormatrix=bt709"
            ]);
        if (audio)
            args.AddRange(["-c:a", "aac", "-b:a", "96k"]);
        args.AddRange([
            "-movflags", "+faststart", "-f", "mp4", "-fs", "33554432",
            Path.Combine(workspace, "output.mp4")
        ]);
        return args;
    }

    /// <summary>
    /// Independently check the FFmpeg-created input really is full-range.
    /// Only the fixed scratch-owned synthetic fixture can reach this probe.
    /// The standard synthetic pilot remains byte-for-byte unchanged.
    /// </summary>
    private static async Task VerifySyntheticFullRangeSourceAsync(
        string ffprobe, string ffprobeHash, string scratchRoot, string input,
        int width, int height, bool audio, CancellationToken token)
    {
        bool verified = false;
        VideoToolExecutionResult probe = await new VideoToolProcessSupervisor()
            .RunAsync(ffprobe, ffprobeHash, scratchRoot,
                workspace =>
                [
                    "-v", "error", "-hide_banner",
                    "-protocol_whitelist", "file",
                    "-probesize", "2097152", "-analyzeduration", "2000000",
                    "-show_streams", "-show_format",
                    "-show_entries",
                    "format=format_name,duration,size:stream=codec_type,codec_name,pix_fmt,width,height,color_range,color_transfer,color_primaries,color_space,channels:stream_tags=rotate:stream_side_data=rotation",
                    "-of", "json", "-o", Path.Combine(workspace, "probe.json"),
                    "-i", input
                ],
                SyntheticSourceProbeBudget, token,
                afterSuccessfulExit: async (workspace, checkToken) =>
                {
                    string path = Path.Combine(workspace, "probe.json");
                    if (!File.Exists(path) ||
                        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                        new FileInfo(path).Length is < 20 or > 65536)
                        throw new InvalidDataException("Synthetic source probe evidence missing.");
                    byte[] evidence = await File.ReadAllBytesAsync(path, checkToken);
                    VideoRec709FullRangeEvaluationProfile.ValidateSyntheticInputProbeJson(
                        evidence, width, height, audio, new FileInfo(input).Length);
                    verified = true;
                });
        if (probe.ExitCode != 0 || !verified)
            throw new InvalidDataException("Generated source range was not independently proven.");
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
        int? nativeErrorCode = null, string? failureCheckpoint = null,
        string profile = VideoProxyEncodingProfiles.StandardKey)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-synthetic-proxy-pilot-v1",
            profile, dryRun, status, schemaUsed = false, originalsOpened = false,
            videoWorkersActivated = false, derivativesRetained = false,
            failureStage, failureOrientation, failureClass,
            toolExitCode, nativeErrorCode, failureCheckpoint, results
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
