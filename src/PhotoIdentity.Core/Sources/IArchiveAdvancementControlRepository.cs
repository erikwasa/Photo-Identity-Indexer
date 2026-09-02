using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

public sealed record ArchiveAdvancementControlState(
    SourceId SourceId,
    string DesiredState,
    string RuntimeState,
    bool SyncRequired,
    string? Message,
    DateTimeOffset UpdatedAtUtc)
{
    public bool IsRequested =>
        string.Equals(DesiredState, "running", StringComparison.Ordinal);
}

/// <summary>
/// Persists operator intent and runtime progress for automatic archive advancement.
/// </summary>
public interface IArchiveAdvancementControlRepository
{
    Task<ArchiveAdvancementControlState?> GetAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default);

    Task RequestRunAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task PauseAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task UpdateRuntimeAsync(
        SourceId sourceId,
        string runtimeState,
        bool? syncRequired,
        string? message,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task BlockAsync(
        SourceId sourceId,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
