using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed compatibility facade for integration fixtures that predate the explicit
/// saved-definition dependency on PostgresSmartCollectionQueryRepository.
/// </summary>
public sealed class PostgresSmartCollectionQueryCompatibilityRepository : ISmartCollectionQueryRepository
{
    private readonly PostgresSmartCollectionQueryRepository _inner;

    public PostgresSmartCollectionQueryCompatibilityRepository(PostgresTestCatalogueDatabase database)
        : this(database, TimeProvider.System)
    {
    }

    public PostgresSmartCollectionQueryCompatibilityRepository(
        PostgresTestCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _inner = new PostgresSmartCollectionQueryRepository(
            database,
            new PostgresSmartCollectionRepository(database, timeProvider),
            timeProvider);
    }

    public Task<SmartCollectionPhotoPage> QueryAsync(
        SmartCollectionFilter filter,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default) =>
        _inner.QueryAsync(filter, offset, limit, cancellationToken);

    public Task<IReadOnlyList<SmartCollectionPhoto>> QueryAllAsync(
        SmartCollectionFilter filter,
        CancellationToken cancellationToken = default) =>
        ((ISmartCollectionQueryRepository)_inner).QueryAllAsync(filter, cancellationToken);

    public Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
        SmartCollectionId collectionId,
        CancellationToken cancellationToken = default) =>
        _inner.CreateSlideshowSnapshotAsync(collectionId, cancellationToken);
}
