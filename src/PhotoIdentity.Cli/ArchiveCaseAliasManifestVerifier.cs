using System.Data;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Read-only, manifest-scoped verification of the already committed
/// retire/restore outcome. Complements (does not replace) the global SQL
/// retirement postcheck. Never calls either mutation service method.
/// </summary>
internal static class ArchiveCaseAliasManifestVerifier
{
    internal sealed record Result(
        int Examined, int Retired, int Restored, int NeverRetired,
        long GlobalActiveRetirements);

    internal static async Task<Result> VerifyAsync(
        PostgresCatalogueDatabase catalogue,
        IReadOnlyList<ArchiveCaseAliasRetirementRequest> pairs,
        IReadOnlyDictionary<Guid, long> expectedAutomaticReviewActions,
        bool expectRetired,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection =
            await catalogue.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);

        // A consistent snapshot across all pairs and the global mapping
        // count; no updates, schema initialization or source-media access.
        using (NpgsqlCommand readOnly = connection.CreateCommand())
        {
            readOnly.Transaction = transaction;
            readOnly.CommandText = "SET TRANSACTION READ ONLY;";
            await readOnly.ExecuteNonQueryAsync(cancellationToken);
        }

        int retired = 0, restored = 0, neverRetired = 0;
        for (int index = 0; index < pairs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveCaseAliasRetirementRequest pair = pairs[index];
            using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    survivor.source_id = @source
                        AND secondary.source_id = @source AS source_ids_match,
                    survivor.source_key = @survivor_key
                        AND secondary.source_key = @secondary_key AS keys_match,
                    survivor.deleted_at_utc IS NULL AS survivor_visible,
                    secondary.deleted_at_utc IS NULL AS secondary_visible,
                    mapping.secondary_asset_id IS NOT NULL AS has_mapping,
                    mapping.restored_at_utc IS NOT NULL AS mapping_restored,
                    coalesce(
                        mapping.source_id = @source
                        AND mapping.survivor_asset_id = @survivor
                        AND mapping.secondary_asset_id = @secondary
                        AND mapping.survivor_source_key = @survivor_key
                        AND mapping.secondary_source_key = @secondary_key
                        AND mapping.expected_sha256 = @hash,
                        false) AS mapping_pinned,
                    (SELECT count(*)
                     FROM asset_revisions AS revision
                     WHERE revision.asset_id = secondary.id) AS secondary_revision_count,
                    (SELECT count(*)
                     FROM review_actions AS action
                     JOIN face_occurrences AS face
                       ON face.id = action.face_occurrence_id
                     JOIN asset_revisions AS revision
                       ON revision.id = face.asset_revision_id
                     WHERE revision.asset_id = secondary.id
                       AND action.actor IN (
                           'identity-matcher:auto',
                           'identity-matcher:auto-multi-evidence'))
                        AS preserved_automatic_review_count,
                    EXISTS (
                        SELECT 1 FROM asset_revisions AS revision
                        WHERE revision.id = @survivor_revision
                          AND revision.asset_id = survivor.id
                          AND revision.content_sha256 = @hash
                          AND revision.size_bytes = @size)
                      AND EXISTS (
                        SELECT 1 FROM asset_revisions AS revision
                        WHERE revision.id = @secondary_revision
                          AND revision.asset_id = secondary.id
                          AND revision.content_sha256 = @hash
                          AND revision.size_bytes = @size)
                        AS pinned_revisions_preserved
                FROM assets AS survivor
                JOIN assets AS secondary ON secondary.id = @secondary
                LEFT JOIN archive_case_alias_retirements AS mapping
                  ON mapping.secondary_asset_id = secondary.id
                WHERE survivor.id = @survivor;
                """;
            command.Parameters.AddWithValue("source", pair.SourceId);
            command.Parameters.AddWithValue("survivor", pair.SurvivorAssetId);
            command.Parameters.AddWithValue("secondary", pair.SecondaryAssetId);
            command.Parameters.AddWithValue("survivor_key", pair.SurvivorSourceKey);
            command.Parameters.AddWithValue("secondary_key", pair.SecondarySourceKey);
            command.Parameters.AddWithValue("survivor_revision", pair.SurvivorVerifiedRevisionId);
            command.Parameters.AddWithValue("secondary_revision", pair.SecondaryVerifiedRevisionId);
            command.Parameters.AddWithValue("hash", pair.ExpectedSha256);
            command.Parameters.AddWithValue("size", pair.ExpectedSizeBytes);

            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    $"Manifest pair {index + 1}: one or both catalogue assets are missing.");

            bool sourceIdsMatch = reader.GetBoolean(0);
            bool keysMatch = reader.GetBoolean(1);
            bool survivorVisible = reader.GetBoolean(2);
            bool secondaryVisible = reader.GetBoolean(3);
            bool mapped = reader.GetBoolean(4);
            bool mappingRestored = reader.GetBoolean(5);
            bool mappingPinned = reader.GetBoolean(6);
            long secondaryRevisions = reader.GetInt64(7);
            long automaticReviews = reader.GetInt64(8);
            bool revisionsPreserved = reader.GetBoolean(9);

            expectedAutomaticReviewActions.TryGetValue(
                pair.SecondaryAssetId, out long minimumReviews);
            if (!sourceIdsMatch || !keysMatch || !survivorVisible ||
                secondaryRevisions < 1 || !revisionsPreserved ||
                automaticReviews < minimumReviews || (mapped && !mappingPinned))
            {
                throw new InvalidOperationException(
                    $"Manifest pair {index + 1}: source identity, pinned revisions, " +
                    "survivor visibility, mapped identity or preserved review history differs.");
            }
            if (expectRetired)
            {
                if (!mapped || mappingRestored || secondaryVisible)
                    throw new InvalidOperationException(
                        $"Manifest pair {index + 1}: expected an active retirement mapping " +
                        "and a hidden secondary asset.");
                retired++;
            }
            else
            {
                if (!secondaryVisible || (mapped && !mappingRestored))
                    throw new InvalidOperationException(
                        $"Manifest pair {index + 1}: expected the secondary to be visible " +
                        "with no active retirement mapping.");
                if (mapped)
                    restored++;
                else
                    neverRetired++; // Safe for a partially applied/rolled-back batch.
            }
        }

        using NpgsqlCommand count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = """
            SELECT count(*) FROM archive_case_alias_retirements
            WHERE restored_at_utc IS NULL;
            """;
        long globalActive = (long)(await count.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Retirement mapping count unavailable."));
        await transaction.CommitAsync(cancellationToken);
        return new(pairs.Count, retired, restored, neverRetired, globalActive);
    }
}
