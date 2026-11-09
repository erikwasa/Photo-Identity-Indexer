namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Fixed, non-identifying failure stages for the opt-in private catalogue
/// experiment. Never prints exception messages, stack traces, frame ordinals,
/// timestamps, file paths, personal aliases or database details.
/// </summary>
public static class VideoFaceFailureDiagnostics
{
    private const string StageKey = "PhotoIdentity.WI0155.SafeFailureStage";

    private static readonly HashSet<string> Stages = new(StringComparer.Ordinal)
    {
        "input-selection",
        "model-verification",
        "catalogue-snapshot",
        "clip-analysis",
        "frame-read",
        "frame-decode",
        "frame-fidelity",
        "face-detection",
        "face-count-limit",
        "face-alignment",
        "face-embedding",
        "candidate-ranking",
        "frame-integrity",
        "report-persistence",
    };

    public static void Mark(Exception exception, string stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!Stages.Contains(stage))
        {
            throw new ArgumentException("Unrecognized safe diagnostic stage.", nameof(stage));
        }

        // Exception.Data does not alter the exception's type or stack. This
        // preserves existing fail-closed and exact-type test contracts.
        exception.Data[StageKey] = stage;
    }

    public static string Describe(Exception exception, string fallbackStage)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string? attributed = exception.Data[StageKey] as string;
        string stage = attributed is not null && Stages.Contains(attributed)
            ? attributed
            : Stages.Contains(fallbackStage) ? fallbackStage : "unknown";

        string category = stage == "face-count-limit"
            ? "frame-face-limit"
            : exception switch
            {
                OutOfMemoryException => "memory",
                OperationCanceledException => "cancelled",
                IOException => "file-io",
                UnauthorizedAccessException => "file-access",
                System.Text.Json.JsonException => "invalid-json",
                ArgumentException => "invalid-input",
                InvalidOperationException => "invalid-state",
                _ => "runtime",
            };

        return "error: private read-only catalogue evaluation failed " +
            $"(stage: {stage}; category: {category}); no identity data was logged.";
    }
}
