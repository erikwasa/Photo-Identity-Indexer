using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Worker;

public sealed record LocalArchiveSyncSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    IReadOnlyList<string> IncludedFolders,
    int SupportedFileCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int MarkedDeletedCount);

/// <summary>
/// Synchronizes selected recursive folders under one permanent catalogue source root.
/// </summary>
public sealed class LocalArchiveSyncCoordinator
{
    private readonly SqliteSourceCatalogueScanner _scanner;

    public LocalArchiveSyncCoordinator(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _scanner = new SqliteSourceCatalogueScanner(database);
    }

    public async Task<LocalArchiveSyncSummary> SyncAsync(
        IAssetSource source,
        CatalogueSource catalogueSource,
        IEnumerable<string> includedFolders,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogueSource);
        ArgumentNullException.ThrowIfNull(includedFolders);

        IReadOnlyList<string> normalized = ArchiveCoverage.NormalizeIncludedFolders(includedFolders);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("At least one archive folder must be included.", nameof(includedFolders));
        }

        int supported = 0;
        int newRevisions = 0;
        int unchanged = 0;
        int deleted = 0;

        foreach (string folder in normalized)
        {
            SourceCatalogueScanSummary summary = await _scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(
                    RelativeRoot: folder.Length == 0 ? null : folder,
                    Recursive: true),
                scannedAtUtc,
                cancellationToken);
            supported += summary.SupportedFileCount;
            newRevisions += summary.NewRevisionCount;
            unchanged += summary.UnchangedFileCount;
            deleted += summary.MarkedDeletedCount;
        }

        return new LocalArchiveSyncSummary(
            catalogueSource.Id,
            scannedAtUtc.ToUniversalTime(),
            normalized,
            supported,
            newRevisions,
            unchanged,
            deleted);
    }
}
