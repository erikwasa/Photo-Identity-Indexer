using System.Diagnostics;
using System.Security.Cryptography;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Process supervision budget. This is NOT an OS sandbox or CPU/GPU/memory
/// quota by default. Explicit WindowsJobLimits opts into native memory/CPU/
/// process caps; a later worker must still enforce OS startup containment,
/// disk/IO/GPU limits and independent authorization of source media.
/// </summary>
public sealed record VideoToolExecutionBudget(
    TimeSpan MaximumWallTime,
    long MaximumTemporaryBytes,
    int MaximumTemporaryEntries = 1024,
    TimeSpan? InspectionInterval = null,
    WindowsVideoJobLimits? WindowsJobLimits = null)
{
    public void Validate()
    {
        if (MaximumWallTime < TimeSpan.FromMilliseconds(100) ||
            MaximumWallTime > TimeSpan.FromMinutes(30) ||
            MaximumTemporaryBytes is < 1 or > 8L * 1024 * 1024 * 1024 ||
            MaximumTemporaryEntries is < 1 or > 4096 ||
            (InspectionInterval is { } interval &&
             (interval < TimeSpan.FromMilliseconds(50) || interval > TimeSpan.FromSeconds(1))))
            throw new ArgumentOutOfRangeException(nameof(VideoToolExecutionBudget),
                "Local video tool execution requires bounded time and scratch-space estimates.");
        WindowsJobLimits?.Validate();
    }
}

/// <summary>Only sanitized measurements, never subprocess stdout, stderr or media paths.</summary>
public sealed record VideoToolExecutionResult(int ExitCode, TimeSpan Elapsed);

/// <summary>
/// Unregistered, explicit-call local process supervisor for a future video
/// adapter. Creates a private working directory, verifies the selected binary,
/// discards stdout/stderr without retaining media names, checks scratch usage,
/// and kills the process tree on cancel/time limit/over-budget. Does NOT take
/// a source path or open/authorize video originals, publish artifacts, hydrate,
/// or schedule a job. Callers must use a fixed, audited FFmpeg argument profile.
///
/// Scratch polling is best-effort: an OS disk quota is still required to bound
/// transient allocation. WindowsJobLimits, when explicitly supplied, configures
/// a Windows Job Object for memory/CPU/process caps and kill-on-close. Because
/// Process.Start precedes job assignment, the short startup gap is NOT fully
/// contained. No production worker may process untrusted media until a
/// suspended/atomic launch path and disk/IO caps have been reviewed.
/// </summary>
public sealed class VideoToolProcessSupervisor
{
    public async Task<VideoToolExecutionResult> RunAsync(
        string executablePath,
        string expectedSha256,
        string scratchRoot,
        Func<string, IReadOnlyList<string>> buildArguments,
        VideoToolExecutionBudget budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);
        ArgumentNullException.ThrowIfNull(buildArguments);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Validate();
        if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath))
            throw new ArgumentException("Video executable must be an existing absolute local file.", nameof(executablePath));
        if (!Path.IsPathFullyQualified(scratchRoot) || !Directory.Exists(scratchRoot))
            throw new ArgumentException("Video scratch root must be an existing absolute directory.", nameof(scratchRoot));
        if (OperatingSystem.IsWindows() &&
            (scratchRoot.StartsWith(@"\\", StringComparison.Ordinal) ||
             executablePath.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException("Remote paths cannot be used for video tool execution.");

        AssertNoReparsePoints(scratchRoot);
        AssertNoReparsePoints(Path.GetDirectoryName(Path.GetFullPath(executablePath))!);
        if ((File.GetAttributes(executablePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Video executable must not be a symbolic link.");
        if (expectedSha256 is null || expectedSha256.Length != 64 ||
            expectedSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Provide the lowercase SHA-256 of the approved executable.", nameof(expectedSha256));

        byte[] digest;
        await using (FileStream file = new(executablePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            digest = await SHA256.HashDataAsync(file, cancellationToken);
        if (!string.Equals(
            Convert.ToHexString(digest), expectedSha256,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Video executable digest is not approved.");

        cancellationToken.ThrowIfCancellationRequested();
        string workspace = Path.Combine(scratchRoot, "video-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        WindowsVideoJobObject? resourceJob = null;
        bool resourceJobAssigned = false;
        bool processStopped = true;
        Task? stdoutDrain = null;
        Task? stderrDrain = null;
        using Process process = new();
        Stopwatch timer = Stopwatch.StartNew();
        using CancellationTokenSource timeout = new(budget.MaximumWallTime);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);

        try
        {
            IReadOnlyList<string> arguments = buildArguments(workspace)
                ?? throw new ArgumentException("Video tool argument builder returned no arguments.");
            if (arguments.Count > 128 ||
                arguments.Any(value => value is null || value.Length > 4096 ||
                                       value.Any(c => c is '\0' or '\r' or '\n')))
                throw new ArgumentException("Video process arguments exceed the safety envelope.");

            ProcessStartInfo start = new(executablePath)
            {
                WorkingDirectory = workspace,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Never leak launcher credentials or private operator settings into
            // untrusted media tooling. Keep the essential Windows loader values.
            start.Environment.Clear();
            foreach (string name in new[] { "SystemRoot", "WINDIR" })
            {
                string? value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value)) start.Environment[name] = value;
            }
            if (OperatingSystem.IsWindows())
            {
                // Tools such as cmd.exe may need standard system utilities, but
                // never search inherited/current/user PATH entries.
                start.Environment["PATH"] =
                    Environment.GetFolderPath(Environment.SpecialFolder.System);
            }
            start.Environment["TEMP"] = workspace;
            start.Environment["TMP"] = workspace;
            start.Environment["TMPDIR"] = workspace;
            foreach (string argument in arguments) start.ArgumentList.Add(argument);

            // Resource constraints must be configured successfully before
            // starting the process. On failure, refuse execution and clean scratch.
            if (budget.WindowsJobLimits is { } resourceLimits)
                resourceJob = WindowsVideoJobObject.Create(resourceLimits);

            process.StartInfo = start;
            if (!process.Start())
                throw new InvalidOperationException("Approved video tool could not start.");
            processStopped = false;
            // Assignment is intentionally fail-closed; future production startup
            // must launch suspended to eliminate this transient uncontained gap.
            if (resourceJob is not null)
            {
                resourceJob.Assign(process);
                resourceJobAssigned = true;
            }

            // Streaming discard prevents unbounded metadata, path or stderr
            // accumulation and prevents a child from blocking on full pipes.
            stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            while (!process.HasExited)
            {
                linked.Token.ThrowIfCancellationRequested();
                CheckWorkspace(workspace, budget);
                await Task.Delay(
                    budget.InspectionInterval ?? TimeSpan.FromMilliseconds(200),
                    linked.Token);
            }

            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stdoutDrain, stderrDrain).WaitAsync(TimeSpan.FromSeconds(5), linked.Token);
            CheckWorkspace(workspace, budget);
            return new VideoToolExecutionResult(process.ExitCode, timer.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                 timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Video tool exceeded its maximum wall-clock duration.");
        }
        finally
        {
            // Closing the last job handle kills any remaining descendants,
            // including grandchildren which Process.WaitForExit cannot observe.
            resourceJob?.Dispose();
            if (!processStopped)
            {
                // Closing an assigned Job Object already terminates its
                // process tree. Calling Kill again can race the kernel's job
                // teardown; only use the old explicit fallback when no job
                // successfully contained the started process.
                if (!process.HasExited && !resourceJobAssigned)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { /* exited concurrently */ }
                }
                using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(shutdown.Token);
                    // WaitForExitAsync only waits for the parent process. On
                    // Windows, killed descendants may still hold inherited
                    // output pipes and the workspace for a short period.
                    // Drain to EOF before attempting to remove the directory.
                    if (stdoutDrain is not null && stderrDrain is not null)
                    {
                        await Task.WhenAll(stdoutDrain, stderrDrain)
                            .WaitAsync(shutdown.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        "Video process tree or its inherited pipes did not terminate; " +
                        "preserve scratch for controlled cleanup.");
                }
            }

            // Delete only our newly created scratch folder after the process
            // tree has released its handles. Windows may briefly report a
            // directory-in-use error after Kill(entireProcessTree: true) and
            // the root process's exit. Use a bounded, uncancellable retry;
            // never treat the caller's cancellation as permission to leave
            // temporary files behind.
            await DeleteWorkspaceAfterExitAsync(workspace);
        }
    }

    private static async Task DeleteWorkspaceAfterExitAsync(string workspace)
    {
        Stopwatch cleanup = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                Directory.Delete(workspace, recursive: true);
                return;
            }
            catch (IOException) when (cleanup.Elapsed < TimeSpan.FromSeconds(8))
            {
                // Descendant working-directory handles can outlive their
                // parent Process.WaitForExitAsync on Windows.
            }
            catch (UnauthorizedAccessException) when (
                OperatingSystem.IsWindows() &&
                cleanup.Elapsed < TimeSpan.FromSeconds(8))
            {
                // Windows may temporarily deny removal of recently closed
                // child handles. Other errors remain terminal.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(125));
        }
    }

    private static void AssertNoReparsePoints(string directory)
    {
        for (string? cursor = Path.GetFullPath(directory); cursor is not null;
             cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Video tool paths must not traverse junctions or links.");
            if (Path.GetPathRoot(cursor) == cursor) break;
        }
    }

    private static void CheckWorkspace(string workspace, VideoToolExecutionBudget budget)
    {
        long bytes = 0;
        int entries = 0;
        Stack<string> dirs = new();
        dirs.Push(workspace);
        while (dirs.TryPop(out string? dir))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
            {
                if (++entries > budget.MaximumTemporaryEntries)
                    throw new InvalidOperationException("Video tool exceeded its scratch entry limit.");
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Video tool created a linked scratch entry.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    dirs.Push(entry);
                    continue;
                }
                bytes += new FileInfo(entry).Length;
                if (bytes > budget.MaximumTemporaryBytes)
                    throw new InvalidOperationException("Video tool exceeded its scratch byte estimate.");
            }
        }
    }
}
