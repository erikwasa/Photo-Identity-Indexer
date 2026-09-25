using PhotoIdentity.Core.Imaging;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Finds current archive revisions that already contain detected faces but have not yet completed
/// the durable face-review derivative profile. Excluded source copies are not selected for backfill.
/// </summary>
public sealed class SqliteFaceReviewDerivativeBackfillRepository : IFaceReviewDerivativeBackfillRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteFaceReviewDerivativeRepository _derivatives;

    public SqliteFaceReviewDerivativeBackfillRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _derivatives = new SqliteFaceReviewDerivativeRepository(database);
    }

    public async Task<AssetRevisionId?> GetNextPendingCurrentRevisionAsync(
        SourceId sourceId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await _derivatives.EnsureSchemaAsync(cancellationToken);
        IReadOnlyList<SourceCopyExclusionState> exclusions =
            await new SqliteSourceCopyExclusionRepository(_database).ListAsync(sourceId, cancellationToken);
        HashSet<string> excludedKeys = exclusions
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.Ordinal);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id, asset.source_key
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
               AND completion.profile_id = $profile_id
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
              AND completion.asset_revision_id IS NULL
              AND EXISTS (
                    SELECT 1
                    FROM face_occurrences AS face
                    WHERE face.asset_revision_id = revision.id
                  )
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$profile_id", profileId.Trim());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!excludedKeys.Contains(reader.GetString(1)))
            {
                return AssetRevisionId.From(Guid.Parse(reader.GetString(0)));
            }
        }

        return null;
    }
}
