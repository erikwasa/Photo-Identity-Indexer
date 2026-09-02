using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Privacy-safe aggregate storage accounting for the permanent archive.
/// Implementations return only byte totals and never expose source paths or filenames.
/// </summary>
public interface IArchiveStorageAccountingRepository
{
    Task<long> GetCurrentLogicalSourceBytesAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default);

    Task<long> GetReviewProxyBytesAsync(
        string? profileId,
        CancellationToken cancellationToken = default);
}
