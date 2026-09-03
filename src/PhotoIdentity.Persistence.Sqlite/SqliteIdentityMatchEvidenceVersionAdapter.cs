using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Compatibility adapter that exposes the existing SQLite evidence reader through the
/// provider-neutral WI-0100 contract without changing current regeneration composition.
/// </summary>
public sealed class SqliteIdentityMatchEvidenceVersionAdapter :
    IIdentityMatchEvidenceVersionReader
{
    private readonly SqliteIdentityMatchEvidenceVersionReader _reader;

    public SqliteIdentityMatchEvidenceVersionAdapter(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _reader = new SqliteIdentityMatchEvidenceVersionReader(database);
    }

    public async Task<ReviewIdentityMatchEvidenceVersion> ReadAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        IdentityMatchEvidenceVersion version = await _reader.ReadAsync(
            modelId,
            modelHash,
            cancellationToken);
        return new ReviewIdentityMatchEvidenceVersion(
            version.ReviewActionId,
            version.SuggestionReviewActionId,
            version.PersonMergeActionId,
            version.EmbeddingId);
    }
}
