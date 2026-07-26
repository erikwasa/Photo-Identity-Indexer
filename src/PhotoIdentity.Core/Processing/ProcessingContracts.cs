using System.Text.Json;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Processing;

public enum ProcessingFailureKind
{
    Transient,
    Permanent,
}

public sealed record ProcessingJobContext
{
    public ProcessingJobContext(
        ProcessingRunId runId,
        ProcessingJobId jobId,
        AssetRevisionId assetRevisionId,
        int attempt,
        string idempotencyKey,
        string? checkpointJson)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (checkpointJson is not null)
        {
            using JsonDocument _ = JsonDocument.Parse(checkpointJson);
        }

        RunId = runId;
        JobId = jobId;
        AssetRevisionId = assetRevisionId;
        Attempt = attempt;
        IdempotencyKey = idempotencyKey.Trim();
        CheckpointJson = checkpointJson?.Trim();
    }

    public ProcessingRunId RunId { get; }
    public ProcessingJobId JobId { get; }
    public AssetRevisionId AssetRevisionId { get; }
    public int Attempt { get; }
    public string IdempotencyKey { get; }
    public string? CheckpointJson { get; }
}

public interface IProcessingCheckpointWriter
{
    Task WriteAsync(string checkpointJson, CancellationToken cancellationToken);
}

public interface IProcessingJobHandler
{
    Task ProcessAsync(
        ProcessingJobContext context,
        IProcessingCheckpointWriter checkpointWriter,
        CancellationToken cancellationToken);
}

public sealed class ProcessingJobFailureException : Exception
{
    public ProcessingJobFailureException(
        ProcessingFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public ProcessingFailureKind FailureKind { get; }
}

public sealed record ProcessingRetryPolicy
{
    public ProcessingRetryPolicy(
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maximumDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        TimeSpan initial = initialDelay ?? TimeSpan.FromSeconds(5);
        TimeSpan maximum = maximumDelay ?? TimeSpan.FromMinutes(5);
        if (initial <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay), "The retry delay must be positive.");
        }

        if (maximum < initial)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDelay),
                "The maximum retry delay cannot be shorter than the initial delay.");
        }

        MaxAttempts = maxAttempts;
        InitialDelay = initial;
        MaximumDelay = maximum;
    }

    public int MaxAttempts { get; }
    public TimeSpan InitialDelay { get; }
    public TimeSpan MaximumDelay { get; }

    public TimeSpan GetDelayAfterAttempt(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        double multiplier = Math.Pow(2, Math.Min(attempt - 1, 30));
        double ticks = Math.Min(InitialDelay.Ticks * multiplier, MaximumDelay.Ticks);
        return TimeSpan.FromTicks((long)ticks);
    }
}
