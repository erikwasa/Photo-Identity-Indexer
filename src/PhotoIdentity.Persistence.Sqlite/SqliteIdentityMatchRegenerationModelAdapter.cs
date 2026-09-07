using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Compatibility adapter that exposes the accepted SQLite model-revision listing through the
/// provider-neutral regeneration application boundary.
/// </summary>
public sealed class SqliteIdentityMatchRegenerationModelAdapter :
    IIdentityMatchRegenerationModelRepository
{
    private readonly SqliteIdentityMatchRegenerationModelRepository _repository;

    public SqliteIdentityMatchRegenerationModelAdapter(
        SqliteIdentityMatchRegenerationModelRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<IReadOnlyList<ReviewIdentityMatchModelRevision>> ListAsync(
        CancellationToken cancellationToken = default) =>
        (await _repository.ListAsync(cancellationToken))
            .Select(revision => new ReviewIdentityMatchModelRevision(
                revision.ModelId,
                revision.ModelHash,
                revision.FaceCount))
            .ToArray();
}
