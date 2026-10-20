using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Cli;

/// <summary>
/// Independent, hash-pinned, Windows-constrained FFprobe inspection of the
/// finished experimental MP4 before private publication. Unregistered.
/// Binary stdout/stderr and all source/media tags are discarded.
/// </summary>
public sealed class VideoProxyOutputValidator(
    string ffprobePath, string ffprobeSha256, string scratchRoot)
{
    private static VideoToolExecutionBudget ProbeBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(15),
        MaximumTemporaryBytes: 128 * 1024,
        MaximumTemporaryEntries: 8,
        InspectionInterval: TimeSpan.FromMilliseconds(100),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 512, MaximumProcesses: 1,
            MaximumCpuPercent: 25));

    public async Task<VideoProxyInspection> ValidateAsync(
        string candidateFile, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows() ||
            !Path.IsPathFullyQualified(candidateFile) ||
            !File.Exists(candidateFile) ||
            (File.GetAttributes(candidateFile) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Video candidate requires a completed local file.");

        // Cheap structural check before FFprobe; it refuses missing or
        // post-mdat moov atoms without reading the whole video into memory.
        VideoProxyCandidateProfile.ValidateFastStart(candidateFile);
        long length = new FileInfo(candidateFile).Length;
        VideoProxyInspection? inspection = null;
        VideoToolExecutionResult result = await new VideoToolProcessSupervisor().RunAsync(
            ffprobePath, ffprobeSha256, scratchRoot,
            workspace =>
            [
                "-v", "error", "-hide_banner",
                "-protocol_whitelist", "file",
                "-probesize", "2097152", "-analyzeduration", "2000000",
                "-show_streams", "-show_format",
                "-show_entries",
                "format=format_name,duration,size:stream=codec_type,codec_name,pix_fmt,width,height,r_frame_rate,color_transfer,color_primaries,color_space,channels:stream_tags=rotate:stream_side_data=rotation",
                "-of", "json",
                "-o", Path.Combine(workspace, "probe.json"),
                "-i", candidateFile
            ],
            ProbeBudget, token,
            afterSuccessfulExit: async (workspace, checkToken) =>
            {
                string json = Path.Combine(workspace, "probe.json");
                if (!File.Exists(json) ||
                    (File.GetAttributes(json) & FileAttributes.ReparsePoint) != 0 ||
                    new FileInfo(json).Length is < 20 or > 65536)
                    throw new InvalidDataException("Video probe output is absent or unbounded.");
                byte[] data = await File.ReadAllBytesAsync(json, checkToken);
                inspection = VideoProxyCandidateProfile.ValidateOutputJson(data, length);
            });
        if (result.ExitCode != 0 || inspection is null)
            throw new InvalidDataException("Video proxy inspection could not verify the candidate.");
        // The encoder's finished file must remain unchanged between probe
        // and publication. The final publisher opens/rehashes under its own
        // owned-root and lease fencing; both checks are required.
        if (new FileInfo(candidateFile).Length != length)
            throw new InvalidDataException("Video candidate changed during inspection.");
        return inspection;
    }
}
