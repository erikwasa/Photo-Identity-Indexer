using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Core.Catalogue;

public sealed record LocalBatchCatalogueSource
{
    public LocalBatchCatalogueSource(
        SourceId sourceId,
        string kind,
        string rootLocator,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);

        SourceId = sourceId;
        Kind = kind.Trim();
        RootLocator = rootLocator.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public SourceId SourceId { get; }
    public string Kind { get; }
    public string RootLocator { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

public sealed record LocalBatchCatalogueScanSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    int SupportedFileCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int MarkedDeletedCount);

/// <summary>
/// Database-neutral catalogue operations needed to start a durable local-folder batch.
/// </summary>
public interface ILocalBatchCatalogueRepository
{
    Task<LocalBatchCatalogueSource> GetOrCreateLocalFolderSourceAsync(
        string rootLocator,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);

    Task<LocalBatchCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        LocalBatchCatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetRevisionId>> GetCurrentRevisionIdsAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default);
}
