using System.Diagnostics;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Worker;

public sealed record LocalArchiveFolderSyncDiagnostics(
    int FolderIndex,
    int EnumeratedDirectoryCount,
    int EnumeratedFileCount,
    int AvailabilityCheckCount,
    TimeSpan SourceScanElapsed,
    int HashedFileCount,
    long HashedBytes,
    TimeSpan HashingElapsed,
    int ObservationWriteCount,
    TimeSpan ObservationPersistenceElapsed,
    TimeSpan MissingReconciliationElapsed,
    TimeSpan TotalElapsed);

public sealed record LocalArchiveSyncDiagnostics(
    TimeSpan TotalElapsed,
    IReadOnlyList<LocalArchiveFolderSyncDiagnostics> Folders);

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
    int MarkedDeletedCount,
    LocalArchiveSyncDiagnostics Diagnostics);

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

        Stopwatch totalStopwatch = Stopwatch.StartNew();
        List<LocalArchiveFolderSyncDiagnostics> folderDiagnostics = [];
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

        try
        {
            for (int folderIndex = 0; folderIndex < normalized.Count; folderIndex++)
            {
                string folder = normalized[folderIndex];
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

                OneDriveSyncDiagnostics? sourceDiagnostics =
                    (archiveSource as OneDriveSyncAssetSource)?.LastScanDiagnostics;
                int statusChecks = (archiveSource as OneDriveSyncAssetSource)?.StatusCheckCount ?? 0;
                ArchiveSourceCatalogueScanDiagnostics scanDiagnostics = summary.Diagnostics;
                LocalArchiveFolderSyncDiagnostics diagnostics = new(
                    folderIndex + 1,
                    sourceDiagnostics?.EnumeratedDirectoryCount ?? 0,
                    sourceDiagnostics?.EnumeratedFileCount ?? summary.SupportedFileCount,
                    statusChecks,
                    sourceDiagnostics?.SourceScanElapsed ?? TimeSpan.Zero,
                    scanDiagnostics.HashedFileCount,
                    scanDiagnostics.HashedBytes,
                    scanDiagnostics.HashingElapsed,
                    scanDiagnostics.ObservationWriteCount,
                    scanDiagnostics.ObservationPersistenceElapsed,
                    scanDiagnostics.MissingReconciliationElapsed,
                    scanDiagnostics.TotalElapsed);
                folderDiagnostics.Add(diagnostics);
                WriteFolderDiagnostics(diagnostics);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            totalStopwatch.Stop();
            Console.WriteLine(
                "[WI-0079 sync diagnostics] cancelled=true completed_folders={0} included_folders={1} total_ms={2:F1}",
                folderDiagnostics.Count,
                normalized.Count,
                totalStopwatch.Elapsed.TotalMilliseconds);
            throw;
        }

        totalStopwatch.Stop();
        LocalArchiveSyncDiagnostics diagnosticsSummary = new(totalStopwatch.Elapsed, folderDiagnostics);
        WriteTotalDiagnostics(diagnosticsSummary);

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
            deleted,
            diagnosticsSummary);
    }

    private static void WriteFolderDiagnostics(LocalArchiveFolderSyncDiagnostics diagnostics)
    {
        Console.WriteLine(
            "[WI-0079 sync diagnostics] folder_index={0} total_ms={1:F1} source_scan_ms={2:F1} directories={3} files={4} status_checks={5} hashed_files={6} hashed_bytes={7} hash_ms={8:F1} observation_writes={9} persistence_ms={10:F1} missing_reconcile_ms={11:F1}",
            diagnostics.FolderIndex,
            diagnostics.TotalElapsed.TotalMilliseconds,
            diagnostics.SourceScanElapsed.TotalMilliseconds,
            diagnostics.EnumeratedDirectoryCount,
            diagnostics.EnumeratedFileCount,
            diagnostics.AvailabilityCheckCount,
            diagnostics.HashedFileCount,
            diagnostics.HashedBytes,
            diagnostics.HashingElapsed.TotalMilliseconds,
            diagnostics.ObservationWriteCount,
            diagnostics.ObservationPersistenceElapsed.TotalMilliseconds,
            diagnostics.MissingReconciliationElapsed.TotalMilliseconds);
    }

    private static void WriteTotalDiagnostics(LocalArchiveSyncDiagnostics diagnostics)
    {
        Console.WriteLine(
            "[WI-0079 sync diagnostics] cancelled=false included_folders={0} total_ms={1:F1} directories={2} files={3} status_checks={4} hashed_files={5} hashed_bytes={6} source_scan_ms={7:F1} hash_ms={8:F1} observation_writes={9} persistence_ms={10:F1} missing_reconcile_ms={11:F1}",
            diagnostics.Folders.Count,
            diagnostics.TotalElapsed.TotalMilliseconds,
            diagnostics.Folders.Sum(static folder => folder.EnumeratedDirectoryCount),
            diagnostics.Folders.Sum(static folder => folder.EnumeratedFileCount),
            diagnostics.Folders.Sum(static folder => folder.AvailabilityCheckCount),
            diagnostics.Folders.Sum(static folder => folder.HashedFileCount),
            diagnostics.Folders.Sum(static folder => folder.HashedBytes),
            diagnostics.Folders.Sum(static folder => folder.SourceScanElapsed.TotalMilliseconds),
            diagnostics.Folders.Sum(static folder => folder.HashingElapsed.TotalMilliseconds),
            diagnostics.Folders.Sum(static folder => folder.ObservationWriteCount),
            diagnostics.Folders.Sum(static folder => folder.ObservationPersistenceElapsed.TotalMilliseconds),
            diagnostics.Folders.Sum(static folder => folder.MissingReconciliationElapsed.TotalMilliseconds));
    }
}
