using PhotoIdentity.Core.Imaging;
using Npgsql;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Finds current archive revisions that already contain detected faces but have not yet completed
/// the durable face-review derivative profile. This is intentionally independent of the detector
/// profile so existing analyzed catalogues can be backfilled without rerunning inference.
/// </summary>
public sealed class PostgresFaceReviewDerivativeBackfillRepository : IFaceReviewDerivativeBackfillRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresFaceReviewDerivativeBackfillRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<AssetRevisionId?> GetNextPendingCurrentRevisionAsync(
        SourceId sourceId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id
            FROM assets AS asset
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            LEFT JOIN asset_revision_face_review_completions AS completion
                ON completion.asset_revision_id = revision.id
               AND completion.profile_id = @profile_id
            WHERE asset.source_id = @source_id
              AND asset.deleted_at_utc IS NULL
              AND completion.asset_revision_id IS NULL
              AND EXISTS (
                    SELECT 1
                    FROM face_occurrences AS face
                    WHERE face.asset_revision_id = revision.id
                  )
            ORDER BY asset.source_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@source_id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("@profile_id", profileId.Trim());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id
            ? AssetRevisionId.From(id)
            : null;
    }
}
