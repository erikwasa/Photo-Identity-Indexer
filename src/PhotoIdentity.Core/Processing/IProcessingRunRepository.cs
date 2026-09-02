using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Database-neutral lifecycle boundary for durable processing runs and their queued jobs.
/// Execution leasing/checkpoint transitions remain on <see cref="IProcessingExecutionRepository"/>.
/// </summary>
public interface IProcessingRunRepository
{
    Task<CatalogueProcessingBatch> CreateRunAsync(
        CatalogueProcessingRun run,
        IReadOnlyCollection<CatalogueProcessingJob> jobs,
        CancellationToken cancellationToken = default);

    Task<CatalogueProcessingRun?> GetRunAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueProcessingJob>> GetJobsAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default);

    Task<CatalogueProcessingRun> RequestCancellationAsync(
        ProcessingRunId runId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);
}
