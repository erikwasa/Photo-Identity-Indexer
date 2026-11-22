namespace PhotoIdentity.Cli;

/// <summary>
/// Narrow first-party diagnostics for explicitly authorized native video-face
/// work. The value is a literal code chosen by the application, never a
/// message, model output, frame, source path or FFmpeg stderr.
/// Marking an exception does not alter failure/retry semantics.
/// </summary>
public static class VideoFaceFailureEvidence
{
    private const string CheckpointKey = "PhotoIdentity.VideoFace.SanitizedCheckpoint";
    private const string ExitCodeKey = "PhotoIdentity.VideoFace.SanitizedToolExitCode";

    private static readonly HashSet<string> Allowed =
    [
        "native-frame-extraction", "native-frame-validation",
        "face-detector", "face-detection-overflow",
        "face-alignment", "face-embedding",
        "face-observation-validation"
    ];

    public static T Mark<T>(T error, string checkpoint, int? toolExitCode = null)
        where T : Exception
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!Allowed.Contains(checkpoint))
            throw new ArgumentException("Video face checkpoint must be predefined.");

        if (!error.Data.Contains(CheckpointKey))
            error.Data[CheckpointKey] = checkpoint;
        if (toolExitCode is int code && code != 0 &&
            !error.Data.Contains(ExitCodeKey))
            error.Data[ExitCodeKey] = code;

        return error;
    }

    public static string? Read(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Data[CheckpointKey] is string checkpoint &&
               Allowed.Contains(checkpoint) ? checkpoint : null;
    }

    public static int? ReadExitCode(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Read(error) is not null &&
               error.Data[ExitCodeKey] is int code && code != 0
            ? code : null;
    }
}
