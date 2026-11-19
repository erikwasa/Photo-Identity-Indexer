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

    public static bool Matches(
        string? kind, string? policy, string? preset, int frames) =>
        kind == Kind && policy == PolicyVersion &&
        preset == PresetVersion && frames is >= 1 and <= MaximumFrames;
}
