using System.Text.Json;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public enum ProcessingRunStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public enum ProcessingJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// Durable execution record for one catalogue-processing configuration.
/// </summary>
public sealed record CatalogueProcessingRun
{
    public CatalogueProcessingRun(
        ProcessingRunId id,
        ProcessingRunStatus status,
        string configurationJson,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? completedAtUtc = null,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationJson);
        using JsonDocument _ = JsonDocument.Parse(configurationJson);

        DateTimeOffset started = startedAtUtc.ToUniversalTime();
        DateTimeOffset? completed = completedAtUtc?.ToUniversalTime();
        if (completed < started)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "Completion time cannot precede the run start time.");
        }

        bool terminal = status is ProcessingRunStatus.Completed
            or ProcessingRunStatus.Failed
            or ProcessingRunStatus.Cancelled;
        if (terminal != completed.HasValue)
        {
            throw new ArgumentException("Terminal runs require a completion time, and active runs cannot have one.");
        }

        string? normalizedError = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        if (status == ProcessingRunStatus.Failed && normalizedError is null)
        {
            throw new ArgumentException("Failed runs require an error.", nameof(error));
        }

        Id = id;
        Status = status;
        ConfigurationJson = configurationJson.Trim();
        StartedAtUtc = started;
        CompletedAtUtc = completed;
        Error = normalizedError;
    }

    public ProcessingRunId Id { get; }
    public ProcessingRunStatus Status { get; }
    public string ConfigurationJson { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public string? Error { get; }
}

/// <summary>
/// Durable work item for processing one immutable asset revision.
/// </summary>
public sealed record CatalogueProcessingJob
{
    public CatalogueProcessingJob(
        ProcessingJobId id,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        ProcessingJobStatus status,
        int attemptCount,
        DateTimeOffset availableAtUtc,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? completedAtUtc = null,
        string? error = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attemptCount);

        DateTimeOffset available = availableAtUtc.ToUniversalTime();
        DateTimeOffset? started = startedAtUtc?.ToUniversalTime();
        DateTimeOffset? completed = completedAtUtc?.ToUniversalTime();
        if (completed < started)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "Completion time cannot precede the job start time.");
        }

        switch (status)
        {
            case ProcessingJobStatus.Queued when started is not null || completed is not null:
                throw new ArgumentException("Queued jobs cannot have active or completion timestamps.");
            case ProcessingJobStatus.Running when started is null || completed is not null || attemptCount == 0:
                throw new ArgumentException("Running jobs require a start time and a positive attempt count.");
            case ProcessingJobStatus.Succeeded or ProcessingJobStatus.Failed or ProcessingJobStatus.Cancelled
                when completed is null:
                throw new ArgumentException("Terminal jobs require a completion time.");
        }

        string? normalizedError = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        if (status == ProcessingJobStatus.Failed && normalizedError is null)
        {
            throw new ArgumentException("Failed jobs require an error.", nameof(error));
        }

        Id = id;
        ProcessingRunId = processingRunId;
        AssetRevisionId = assetRevisionId;
        Status = status;
        AttemptCount = attemptCount;
        AvailableAtUtc = available;
        StartedAtUtc = started;
        CompletedAtUtc = completed;
        Error = normalizedError;
    }

    public ProcessingJobId Id { get; }
    public ProcessingRunId ProcessingRunId { get; }
    public AssetRevisionId AssetRevisionId { get; }
    public ProcessingJobStatus Status { get; }
    public int AttemptCount { get; }
    public DateTimeOffset AvailableAtUtc { get; }
    public DateTimeOffset? StartedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public string? Error { get; }
}

public sealed record CatalogueProcessingBatch(
    CatalogueProcessingRun Run,
    IReadOnlyList<CatalogueProcessingJob> Jobs);
