namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Dedicated manual FaceEvidence lease identity. The older CLIP FrameEvidence
/// work type must never authorize native YuNet/SFace execution or publication.
/// </summary>
public static class VideoFaceEvidenceLeaseIdentity
{
    public const string Kind = "FaceEvidence";
    public const string PolicyVersion = VideoFrameSamplingPlanner.PolicyVersion;
    public const string PresetVersion = "wi0155-native-sdr-face-jpeg-v1";
    public const int MaximumFrames = 4;
    public const string DetectorModelId = "yunet-2023mar-fp32";
    public const string DetectorSha256 =
        "8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4";
    public const string EmbedderModelId = "sface-2021dec-fp32";
    public const string EmbedderSha256 =
        "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79";
    public const string AlignmentProtocol = "sface-five-point-v1";

    public static bool Matches(
        string? kind, string? policy, string? preset, int frames) =>
        kind == Kind && policy == PolicyVersion &&
        preset == PresetVersion && frames is >= 1 and <= MaximumFrames;
}
