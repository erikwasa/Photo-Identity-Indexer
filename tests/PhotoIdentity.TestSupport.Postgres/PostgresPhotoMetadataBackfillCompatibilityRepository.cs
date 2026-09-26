using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed metadata backfill adapter retaining the legacy two-argument candidate query
/// used by integration fixtures while implementing the current provider-neutral contract.
/// </summary>
public sealed class PostgresPhotoMetadataBackfillCompatibilityRepository :
    IPhotoMetadataBackfillRepository
{
    private readonly PostgresPhotoMetadataBackfillRepository _inner;

    public PostgresPhotoMetadataBackfillCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _inner = new PostgresPhotoMetadataBackfillRepository(database.Database);
    }

    public async Task<IReadOnlyList<PhotoMetadataBackfillCandidate>> GetCandidatesAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PhotoMetadataBackfillRefreshCandidate> candidates =
            await _inner.GetRefreshCandidatesAsync(
                limit,
                offset,
                PhotoMetadataExtractionContract.CurrentVersion,
                force: false,
                cancellationToken);

        return candidates
            .Select(candidate => new PhotoMetadataBackfillCandidate(
                candidate.RevisionId,
                candidate.ContentHash,
                candidate.SizeBytes,
                candidate.RootLocator,
                candidate.SourceKey,
                candidate.MediaType))
            .ToArray();
    }

    public Task<IReadOnlyList<PhotoMetadataBackfillRefreshCandidate>> GetRefreshCandidatesAsync(
        int limit,
        int offset,
        int currentVersion,
        bool force,
        CancellationToken cancellationToken = default) =>
        _inner.GetRefreshCandidatesAsync(limit, offset, currentVersion, force, cancellationToken);
}
