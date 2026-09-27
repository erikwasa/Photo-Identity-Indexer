using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed compatibility facade for mature integration fixtures that still pass the
/// historical TestSupport CatalogueSource record into source-observation persistence.
/// </summary>
public sealed class PostgresArchiveSourceObservationCompatibilityRepository :
    IArchiveSourceObservationRepository
{
    private readonly PostgresTestCatalogueDatabase _database;
    private readonly PostgresArchiveSourceObservationRepository _inner;

    public PostgresArchiveSourceObservationCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _inner = new PostgresArchiveSourceObservationRepository(database);
    }

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        _database.InitializeAsync(cancellationToken);

    public Task<ArchiveSourceObservationPersistenceResult> RecordScanObservationAsync(
        CatalogueSource source,
        SourceAsset sourceAsset,
        Sha256Digest? verifiedContentHash,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default) =>
        RecordScanObservationAsync(
            new ArchiveCatalogueSource(
                source.Id,
                source.Kind,
                source.RootLocator,
                source.CreatedAtUtc),
            sourceAsset,
            verifiedContentHash,
            scannedAtUtc,
            cancellationToken);

    public Task<ArchiveSourceObservationPersistenceResult> RecordScanObservationAsync(
        ArchiveCatalogueSource source,
        SourceAsset sourceAsset,
        Sha256Digest? verifiedContentHash,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default) =>
        _inner.RecordScanObservationAsync(
            source,
            sourceAsset,
            verifiedContentHash,
            scannedAtUtc,
            cancellationToken);

    public Task<ArchiveSourceObservationSnapshot?> GetNextPendingAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default) =>
        _inner.GetNextPendingAsync(sourceId, cancellationToken);

    public Task<ArchiveSourceObservationSnapshot?> GetAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default) =>
        _inner.GetAsync(assetId, cancellationToken);

    public Task<ArchiveSourceVerificationPersistenceResult> RecordVerifiedContentAsync(
        AssetId assetId,
        Sha256Digest contentHash,
        long sizeBytes,
        DateTimeOffset lastWriteTimeUtc,
        string mediaType,
        DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken = default) =>
        _inner.RecordVerifiedContentAsync(
            assetId,
            contentHash,
            sizeBytes,
            lastWriteTimeUtc,
            mediaType,
            verifiedAtUtc,
            cancellationToken);
}
