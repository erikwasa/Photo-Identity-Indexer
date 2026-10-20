using System.Collections;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Strongly allowlisted diagnostic markers attached only by our process
/// supervisor. This preserves the existing exception types and their fail-closed
/// behavior while allowing operator-facing tools to report WHICH guard stopped
/// an attempted synthetic encode. Messages, paths, arguments, environment,
/// FFmpeg stdout/stderr and child-supplied data are never serialized.
/// </summary>
public static class VideoToolFailureEvidence
{
    private const string Key = "PhotoIdentity.VideoTool.SanitizedCheckpoint";

    private static readonly HashSet<string> Allowed =
    [
        "resource-profile-refused", "linked-executable",
        "tool-digest-mismatch", "linked-tool-path",
        "argument-preparation", "job-object-setup",
        "suspended-process-launch", "process-observation",
        "post-exit-validation", "scratch-entry-limit",
        "scratch-linked-entry", "scratch-byte-limit",
        "process-shutdown-unverified"
    ];

    /// <summary>Assign a fixed code only. Never pass a tool-derived string.</summary>
    public static InvalidOperationException Mark(
        InvalidOperationException error, string checkpoint)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!Allowed.Contains(checkpoint))
            throw new ArgumentException("Video tool checkpoint must be predefined.");
        if (!error.Data.Contains(Key))
            error.Data[Key] = checkpoint;
        return error;
    }

    /// <summary>Only return a known checkpoint; arbitrary Data is ignored.</summary>
    public static string? Read(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Data[Key] is string checkpoint && Allowed.Contains(checkpoint)
            ? checkpoint
            : null;
    }
}
