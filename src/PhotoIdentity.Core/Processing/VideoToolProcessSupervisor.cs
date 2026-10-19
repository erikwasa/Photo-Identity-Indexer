using System.Diagnostics;
using System.Security.Cryptography;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Process supervision budget. This is NOT an OS sandbox or CPU/GPU/memory
/// quota: the future worker must separately enforce those and authorize media.
/// </summary>
public sealed record VideoToolExecutionBudget(
    TimeSpan MaximumWallTime,
    long MaximumTemporaryBytes,
    int MaximumTemporaryEntries = 1024,
    TimeSpan? InspectionInterval = null)
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
/// transient allocation. Kill(entireProcessTree:true) is best effort, not a
/// Windows job-object resource limit.
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
        bool processStopped = true;
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

            process.StartInfo = start;
            if (!process.Start())
                throw new InvalidOperationException("Approved video tool could not start.");
            processStopped = false;

            // Streaming discard prevents unbounded metadata, path or stderr
            // accumulation and prevents a child from blocking on full pipes.
            Task stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            Task stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

            while (!process.HasExited)
            {
                linked.Token.ThrowIfCancellationRequested();
                CheckWorkspace(workspace, budget);
                await Task.Delay(
                    budget.InspectionInterval ?? TimeSpan.FromMilliseconds(200),
                    linked.Token);
            }

            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5), linked.Token);
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
            if (!processStopped)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { /* exited concurrently */ }
                }
                using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(shutdown.Token); }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        "Video tool did not terminate; preserve scratch for controlled cleanup.");
                }
            }

            // Delete only our newly created scratch folder after the process
            // has exited, not any caller-owned source or derivative directory.
            Directory.Delete(workspace, recursive: true);
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
