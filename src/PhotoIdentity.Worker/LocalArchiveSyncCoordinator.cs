using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Worker;

public sealed record LocalArchiveSyncSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    IReadOnlyList<string> IncludedFolders,
    int SupportedFileCount,
    int LocalFileCount,
    int OnlineOnlyFileCount,
    int DownloadingFileCount,
    int UnavailableFileCount,
    int AvailabilityErrorCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int VerifiedSourceCount,
    int NeedsSourceVerificationCount,
    int UnverifiedSourceCount,
    int MarkedDeletedCount);

/// <summary>
/// Synchronizes selected recursive folders under one permanent catalogue source root.
/// Local-folder archive sources are inspected through the OneDrive-aware filesystem adapter so
/// Files On-Demand placeholders are recorded without opening or hydrating them.
/// </summary>
public sealed class LocalArchiveSyncCoordinator
{
    private readonly SqliteArchiveSourceCatalogueScanner _scanner;

    public LocalArchiveSyncCoordinator(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _scanner = new SqliteArchiveSourceCatalogueScanner(database);
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

        IAssetSource archiveSource = source is LocalFolderAssetSource
            ? new OneDriveSyncAssetSource(catalogueSource.Id, catalogueSource.RootLocator)
            : source;
        IReadOnlyList<string> normalized = ArchiveCoverage.NormalizeIncludedFolders(includedFolders);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("At least one archive folder must be included.", nameof(includedFolders));
        }

        int supported = 0;
        int local = 0;
        int onlineOnly = 0;
        int downloading = 0;
        int unavailable = 0;
        int availabilityErrors = 0;
        int newRevisions = 0;
        int unchanged = 0;
        int verified = 0;
        int needsVerification = 0;
        int unverified = 0;
        int deleted = 0;

        foreach (string folder in normalized)
        {
            ArchiveSourceCatalogueScanSummary summary = await _scanner.ScanAsync(
                archiveSource,
                catalogueSource,
                new SourceScanOptions(
                    RelativeRoot: folder.Length == 0 ? null : folder,
                    Recursive: true),
                scannedAtUtc,
                cancellationToken);
            supported += summary.SupportedFileCount;
            local += summary.LocalFileCount;
            onlineOnly += summary.OnlineOnlyFileCount;
            downloading += summary.DownloadingFileCount;
            unavailable += summary.UnavailableFileCount;
            availabilityErrors += summary.AvailabilityErrorCount;
            newRevisions += summary.NewRevisionCount;
            unchanged += summary.UnchangedFileCount;
            verified += summary.VerifiedSourceCount;
            needsVerification += summary.NeedsSourceVerificationCount;
            unverified += summary.UnverifiedSourceCount;
            deleted += summary.MarkedDeletedCount;
        }

        return new LocalArchiveSyncSummary(
            catalogueSource.Id,
            scannedAtUtc.ToUniversalTime(),
            normalized,
            supported,
            local,
            onlineOnly,
            downloading,
            unavailable,
            availabilityErrors,
            newRevisions,
            unchanged,
            verified,
            needsVerification,
            unverified,
            deleted);
    }
}
