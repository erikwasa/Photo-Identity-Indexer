using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Preserves the historical automatic-place fixture constructor while delegating all writes and
/// precedence rules to the PostgreSQL repository.
/// </summary>
public sealed class PostgresAutomaticPhotoPlaceCompatibilityRepository : IAutomaticPhotoPlaceRepository
{
    private readonly PostgresPhotoPlaceRepository _inner;

    public PostgresAutomaticPhotoPlaceCompatibilityRepository(
        PostgresTestCatalogueDatabase database,
        IPhotoPlaceRepository places,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _inner = new PostgresPhotoPlaceRepository(database, timeProvider);
    }

    public Task<AutomaticPhotoPlaceEligibility> GetEligibilityAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default) =>
        _inner.GetEligibilityAsync(revisionId, cancellationToken);

    public Task<AutomaticPhotoPlaceWriteResult> TrySetAsync(
        AssetRevisionId revisionId,
        string placeValue,
        string provider,
        string actor,
        CancellationToken cancellationToken = default) =>
        _inner.TrySetAsync(revisionId, placeValue, provider, actor, cancellationToken);
}
