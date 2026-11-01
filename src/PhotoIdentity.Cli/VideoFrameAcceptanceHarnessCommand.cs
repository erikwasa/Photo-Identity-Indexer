using System.Diagnostics;
using System.Text.Json;

namespace PhotoIdentity.Cli;

/// <summary>
/// An operator-only wrapper around this exact CLI's selected FrameEvidence
/// command. This never accepts a media path or arbitrary executable.
/// Preview never launches a child; execution needs a second consent gate
/// in addition to BOTH underlying FrameEvidence disclosure acknowledgments.
/// </summary>
public sealed record VideoFrameAcceptanceHarnessOptions(
    string Mode, bool Execute, int TerminateAfterSeconds,
    bool AcknowledgeChildTermination,
    VideoFrameSelectedOneShotOptions Selected, IReadOnlyList<string> SelectedArgs)
{
    public static VideoFrameAcceptanceHarnessOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int separator = Array.IndexOf(args, "--");
        if (separator <= 0 || separator == args.Length - 1 ||
            args[(separator + 1)..].Contains("--"))
            throw new ArgumentException(
                "Acceptance harness requires -- followed by selected-one-shot options.");

        string? mode = null;
        bool execute = false, acknowledge = false;
        int afterSeconds = 0;
        bool afterProvided = false;
        for (int i = 0; i < separator; i++)
        {
            if (args[i] == "--execute" && !execute)
            {
                execute = true;
                continue;
            }
            if (args[i] == "--acknowledge-owned-child-termination" && !acknowledge)
            {
                acknowledge = true;
                continue;
            }
            if (args[i] is "--mode" or "--terminate-after-seconds" &&
                i + 1 < separator)
            {
                string key = args[i++];
                string value = args[i];
                if (key == "--mode" && mode is null)
                {
                    mode = value;
                    continue;
                }
                if (key == "--terminate-after-seconds" && !afterProvided &&
                    int.TryParse(value, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out afterSeconds))
                {
                    afterProvided = true;
                    continue;
                }
            }
            throw new ArgumentException(
                "Unknown or repeated acceptance harness option.");
        }

        if (mode is not ("measure" or "interrupt") ||
            (mode == "measure" && (afterProvided || acknowledge)) ||
            (mode == "interrupt" &&
             (!afterProvided || afterSeconds is < 5 or > 600)) ||
            (execute && mode == "interrupt" && !acknowledge))
            throw new ArgumentException(
                "Harness mode, bounded interruption time and separate termination acknowledgment are required.");

        string[] selectedArgs = args[(separator + 1)..];
        VideoFrameSelectedOneShotOptions selected =
            VideoFrameSelectedOneShotOptions.Parse(selectedArgs);
        if (!selected.Apply ||
            !selected.AcknowledgeV38 || !selected.AcknowledgeDisclosure)
            throw new ArgumentException(
                "Acceptance harness requires an explicitly approved selected-one-shot job.");
        return new(mode, execute, afterSeconds, acknowledge,
            selected, selectedArgs);
    }
}

/// <summary>
/// Process-local observation only. OS CPU time and working set of the
/// spawned CLI host are measured; FFmpeg children and locally/remote
/// hosted Ollama CPU/GPU, Azure billing and energy are not attributed.
/// Never promote a partial CPU measurement to cpuTimeMs in the private
/// multi-clip acceptance report.
/// </summary>
public sealed record VideoFrameAcceptanceProcessObservation(
    bool ChildStarted, bool ChildExited, bool TerminationRequested,
    int? ChildExitCode, long WallTimeMs, long? CliHostCpuTimeMs,
    long? CliHostPeakWorkingSetBytes, string ReasonCode);

public static class VideoFrameAcceptanceHarnessCommandRunner
{
    public static async Task<int> RunAsync(
        VideoFrameAcceptanceHarnessOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Frame acceptance subprocess containment requires Windows.");
        if (options.Mode is not ("measure" or "interrupt") ||
            !options.Selected.Apply || !options.Selected.AcknowledgeV38 ||
            !options.Selected.AcknowledgeDisclosure ||
            options.Mode == "interrupt" &&
              (options.TerminateAfterSeconds is < 5 or > 600 ||
               options.Execute && !options.AcknowledgeChildTermination))
            throw new InvalidOperationException(
                "Explicit selected-job and termination authorization is required.");

        // Validate a *fresh* owned scratch before even showing an executable
        // plan. The actual child repeats all DB, SHA, source and safety checks.
        VideoFrameEvidenceScratchGuard.Validate(options.Selected.Producer.ScratchRoot);

        if (!options.Execute)
        {
            Write(output, options.Mode, false, new(false, false, false,
                null, 0, null, null, "preview-only"));
            return 0;
        }

        VideoFrameAcceptanceProcessObservation observation =
            await ExecuteChildAsync(options, token);
        Write(output, options.Mode, true, observation);
        return options.Mode == "interrupt"
            ? observation.ReasonCode == "owned-child-terminated" ? 0 : 1
            : observation.ChildExitCode == 0 &&
              observation.ReasonCode == "child-completed" ? 0 : 1;
    }

    internal static ProcessStartInfo BuildOwnCliStart(
        string processPath, string assemblyPath,
        IReadOnlyList<string> selectedArgs)
    {
        if (!Path.IsPathFullyQualified(processPath) ||
            !Path.IsPathFullyQualified(assemblyPath) ||
            !File.Exists(processPath) || !File.Exists(assemblyPath))
            throw new InvalidOperationException("A physically installed CLI host is required.");
        string host = Path.GetFileNameWithoutExtension(processPath);
        if (!host.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            !host.Equals("PhotoIdentity.Cli", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The harness can only start its own PhotoIdentity CLI executable.");

        var info = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!
        };
        if (host.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(assemblyPath);
        info.ArgumentList.Add("video");
        info.ArgumentList.Add("frames");
        info.ArgumentList.Add("selected-one-shot");
        foreach (string argument in selectedArgs)
            info.ArgumentList.Add(argument);
        return info;
    }

    private static async Task<VideoFrameAcceptanceProcessObservation>
        ExecuteChildAsync(VideoFrameAcceptanceHarnessOptions options,
            CancellationToken token)
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("CLI executable path is unavailable.");
        string assembly = typeof(Program).Assembly.Location;
        ProcessStartInfo info = BuildOwnCliStart(
            processPath, assembly, options.SelectedArgs);
        return await ObserveStartedChildAsync(info, options.Mode,
            options.TerminateAfterSeconds, token);
    }

    /// <summary>
    /// Test seam: accepts an explicit synthetic process in isolated CI tests.
    /// The operator CLI never passes anything other than BuildOwnCliStart.
    /// The only termination target is the Process instance created here.
    /// </summary>
    public static async Task<VideoFrameAcceptanceProcessObservation>
        ObserveStartedChildAsync(ProcessStartInfo info, string mode,
            int terminateAfterSeconds, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (mode is not ("measure" or "interrupt") ||
            mode == "interrupt" && terminateAfterSeconds is < 5 or > 600 ||
            info.UseShellExecute || !info.RedirectStandardOutput ||
            !info.RedirectStandardError)
            throw new ArgumentException(
                "A bounded directly launched and redacted child is required.");
        using Process child = new() { StartInfo = info };
        long clock = Stopwatch.GetTimestamp();
        try
        {
            if (!child.Start())
                throw new InvalidOperationException("The approved child could not start.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or IOException or InvalidOperationException)
        {
            // Never echo absolute paths, private arguments or child failures.
            throw new InvalidOperationException(
                "The approved selected-job child could not be started.");
        }

        // Discard all child output, even if a nested library or model emits a
        // private path. Never buffer process output to an artifact or PR log.
        Task stdout = child.StandardOutput.BaseStream.CopyToAsync(Stream.Null, token);
        Task stderr = child.StandardError.BaseStream.CopyToAsync(Stream.Null, token);
        Task finished = child.WaitForExitAsync(token);
        bool requested = false;
        string reason = "child-completed";
        try
        {
            TimeSpan deadline = mode == "interrupt"
                ? TimeSpan.FromSeconds(terminateAfterSeconds)
                : TimeSpan.FromMinutes(14);
            Task tick = Task.Delay(deadline, token);
            if (await Task.WhenAny(finished, tick) == tick)
            {
                token.ThrowIfCancellationRequested();
                if (!child.HasExited)
                {
                    // Kill ONLY the child created by this Process object,
                    // including descendants. No PID input, process-name
                    // matching or operator-supplied arbitrary executable.
                    requested = true;
                    child.Kill(entireProcessTree: true);
                    reason = mode == "interrupt"
                        ? "owned-child-terminated" : "harness-safety-timeout";
                }
            }
            await child.WaitForExitAsync(token);
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10), token);
            if (mode == "interrupt" && !requested)
                reason = "child-exited-before-interruption";
        }
        finally
        {
            if (!child.HasExited)
            {
                try { child.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already exited */ }
            }
        }

        long? cpu = null, peak = null;
        try
        {
            cpu = checked((long)child.TotalProcessorTime.TotalMilliseconds);
            peak = child.PeakWorkingSet64;
        }
        catch (Exception error) when (error is InvalidOperationException or
            System.ComponentModel.Win32Exception or OverflowException)
        {
            // Missing counters stay null, not fabricated zero.
        }
        return new(true, child.HasExited, requested,
            child.HasExited ? child.ExitCode : null,
            (long)Stopwatch.GetElapsedTime(clock).TotalMilliseconds,
            cpu, peak, reason);
    }

    private static void Write(TextWriter output, string mode, bool execute,
        VideoFrameAcceptanceProcessObservation observed) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-frame-acceptance-harness-v1",
            mode, executed = execute,
            observed.ChildStarted, observed.ChildExited,
            observed.TerminationRequested, observed.ChildExitCode,
            observed.WallTimeMs, observed.CliHostCpuTimeMs,
            observed.CliHostPeakWorkingSetBytes, observed.ReasonCode,
            cpuTimeMs = (long?)null,
            gpuTimeMs = (long?)null,
            azureCostMinorUnits = (long?)null,
            cpuCoverage = "cli-process-only-not-ffmpeg-or-ollama",
            fullCpuTimeMeasured = false, gpuMeasured = false,
            azureCostMeasured = false,
            publishedVerified = false, databaseVerified = false,
            scratchVerified = false, remoteInferenceCancellationVerified = false,
            originalsOpenedByHarness = false,
            oneDriveHydrationRequestedByHarness = false,
            childOutputDiscarded = true, schemaChangedByHarness = false,
            automaticallyEnabled = false
        }));
}
