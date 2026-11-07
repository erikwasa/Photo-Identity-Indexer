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
        bool fullRange = VideoProxyEncodingProfiles.IsRec709FullRange(Key);
        string[] args = [.. (fullRange
            ? VideoRec709FullRangeEvaluationProfile.BuildArguments(
                stagedFile, toolWorkspace)
            : VideoProxyCandidateProfile.BuildArguments(stagedFile, toolWorkspace))];
        if (Key is VideoProxyEncodingProfiles.StandardKey or
            VideoProxyEncodingProfiles.Rec709FullRangeKey)
            return args;

        if (Key is not (VideoProxyEncodingProfiles.CompactKey or
            VideoProxyEncodingProfiles.Rec709FullRangeCompactKey))
            throw new ArgumentException("Unsupported video encoding profile.");

        Replace("-vf", fullRange
            ? args[Array.IndexOf(args, "-vf") + 1].Replace(
                "scale=w=720:h=720:", "scale=w=540:h=540:",
                StringComparison.Ordinal)
            : "scale=w=540:h=540:force_original_aspect_ratio=decrease:force_divisible_by=2,fps=30,format=yuv420p");
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
    // Internal conversion identities; only Standard/Compact are selectable.
    public const string Rec709FullRangeKey = "rec709-fullrange-720p";
    public const string Rec709FullRangeCompactKey = "rec709-fullrange-540p";

    public static VideoProxyEncodingProfile Standard { get; } = new(
        StandardKey, VideoProxyCandidateProfile.Version,
        MaximumDimension: 720, Crf: 24,
        MaximumRate: "2500k", BufferSize: "5000k", AudioRate: "96k");

    public static VideoProxyEncodingProfile Compact { get; } = new(
        CompactKey, "wi0189-sdr-h264-aac-540p-compact-v1",
        MaximumDimension: 540, Crf: 26,
        MaximumRate: "1400k", BufferSize: "2800k", AudioRate: "80k");

    // Distinct durable job/preset identity. The accepted experimental
    // argument recipe is reused without changing standard/compact receipts.
    public static VideoProxyEncodingProfile Rec709FullRange { get; } = new(
        Rec709FullRangeKey, "wi0210-rec709-fullrange-720p-production-v1",
        MaximumDimension: 720, Crf: 24,
        MaximumRate: "2500k", BufferSize: "5000k", AudioRate: "96k");

    public static VideoProxyEncodingProfile Rec709FullRangeCompact { get; } = new(
        Rec709FullRangeCompactKey, "wi0207-rec709-fullrange-540p-compact-v1",
        MaximumDimension: 540, Crf: 26,
        MaximumRate: "1400k", BufferSize: "2800k", AudioRate: "80k");

    public static bool IsOutputQualityKey(string? key) =>
        key is StandardKey or CompactKey;

    public static bool IsRec709FullRange(string? key) =>
        key is Rec709FullRangeKey or Rec709FullRangeCompactKey;

    // Metadata chooses the conversion, never the operator's output quality.
    public static VideoProxyEncodingProfile ResolveForInput(
        string qualityKey, bool fullRange) => (qualityKey, fullRange) switch
    {
        (StandardKey, false) => Standard,
        (CompactKey, false) => Compact,
        (StandardKey, true) => Rec709FullRange,
        (CompactKey, true) => Rec709FullRangeCompact,
        _ => throw new ArgumentException("Select Standard 720p or Compact 540p.")
    };

    public static bool IsEligible(VideoProxyEncodingProfile profile,
        PhotoIdentity.Core.Catalogue.VideoRevisionInspection? input) =>
        Equals(profile, Resolve(profile.Key)) &&
        (IsRec709FullRange(profile.Key)
            ? VideoRec709FullRangeEvaluationProfile.Eligible(input)
            : VideoProxyCandidateProfile.Eligible(input));

    public static VideoProxyEncodingProfile Resolve(string? key) => key switch
    {
        StandardKey => Standard,
        CompactKey => Compact,
        Rec709FullRangeKey => Rec709FullRange,
        Rec709FullRangeCompactKey => Rec709FullRangeCompact,
        _ => throw new ArgumentException(
            "Select an approved video proxy encoding profile.")
    };
}
