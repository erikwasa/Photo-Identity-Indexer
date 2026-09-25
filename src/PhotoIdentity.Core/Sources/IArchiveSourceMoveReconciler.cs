using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Reconciles source-copy moves only after every configured included folder has been scanned for
/// the same synchronization timestamp. Implementations must prefer leaving copies separate over
/// guessing when exact-content evidence is ambiguous.
/// </summary>
public interface IArchiveSourceMoveReconciler
{
    Task<int> ReconcileAsync(
        SourceId sourceId,
        IReadOnlyList<string> includedFolders,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default);
}
