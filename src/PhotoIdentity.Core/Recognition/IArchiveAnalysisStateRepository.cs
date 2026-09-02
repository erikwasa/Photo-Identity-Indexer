using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

/// <summary>
/// Durable provider-neutral state for exact archive-analysis profiles and successful revision completion.
/// </summary>
public interface IArchiveAnalysisStateRepository
{
    Task RegisterRunAsync(
        ProcessingRunId runId,
        AnalysisProfileDefinition profile,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default);

    Task<Sha256Digest> GetRunProfileHashAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default);

    Task<bool> IsCompletedAsync(
        AssetRevisionId revisionId,
        Sha256Digest profileHash,
        CancellationToken cancellationToken = default);

    Task RecordCompletionAsync(
        ProcessingRunId runId,
        AssetRevisionId revisionId,
        Sha256Digest profileHash,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);
}
