namespace PhotoIdentity.Cli;

/// <summary>
/// Deliberately small, reviewed software-only SDR encode choices. The preset
/// version is a durable video-job identity component; never repurpose an
/// existing version for changed FFmpeg arguments.
/// </summary>
public sealed record VideoProxyEncodingProfile(
    string Key, string Version, int MaximumDimension,
    int Crf, string MaximumRate, string BufferSize, string AudioRate)
{
    public IReadOnlyList<string> BuildArguments(
        string stagedFile, string toolWorkspace)
    {
        // Retain the original WI-0188 720p byte-for-byte argument contract,
        // including Windows process limits, network refusal, autorotation,
        // faststart, color VUI and one optional audio stream.
        string[] args = [.. VideoProxyCandidateProfile.BuildArguments(
            stagedFile, toolWorkspace)];
        if (Key == VideoProxyEncodingProfiles.StandardKey)
            return args;

        if (Key != VideoProxyEncodingProfiles.CompactKey)
            throw new ArgumentException("Unsupported video encoding profile.");

        Replace("-vf",
            "scale=w=540:h=540:force_original_aspect_ratio=decrease:force_divisible_by=2,fps=30,format=yuv420p");
        Replace("-crf", Crf.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Replace("-maxrate", MaximumRate);
        Replace("-bufsize", BufferSize);
        Replace("-b:a", AudioRate);
        return args;

        void Replace(string option, string replacement)
        {
            int index = Array.IndexOf(args, option);
            if (index < 0 || index + 1 == args.Length)
                throw new InvalidOperationException(
                    "The approved video argument profile is incomplete.");
            args[index + 1] = replacement;
        }
    }
}

public static class VideoProxyEncodingProfiles
{
    public const string StandardKey = "standard-720p";
    public const string CompactKey = "compact-540p";

    public static VideoProxyEncodingProfile Standard { get; } = new(
        StandardKey, VideoProxyCandidateProfile.Version,
        MaximumDimension: 720, Crf: 24,
        MaximumRate: "2500k", BufferSize: "5000k", AudioRate: "96k");

    public static VideoProxyEncodingProfile Compact { get; } = new(
        CompactKey, "wi0189-sdr-h264-aac-540p-compact-v1",
        MaximumDimension: 540, Crf: 26,
        MaximumRate: "1400k", BufferSize: "2800k", AudioRate: "80k");

    public static VideoProxyEncodingProfile Resolve(string? key) => key switch
    {
        StandardKey => Standard,
        CompactKey => Compact,
        _ => throw new ArgumentException(
            "Select an approved video proxy encoding profile.")
    };
}
