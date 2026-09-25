using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// SQLite implementation of conservative exact source-move reconciliation. Only one-to-one
/// verified hash groups whose missing/new transitions belong to the same completed sync qualify.
/// Excluded locators are never move candidates.
/// </summary>
public sealed class SqliteArchiveSourceMoveReconciler : IArchiveSourceMoveReconciler
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly ISourceCopyExclusionRepository? _exclusions;

    public SqliteArchiveSourceMoveReconciler(
        SqliteCatalogueDatabase database,
        ISourceCopyExclusionRepository? exclusions = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _exclusions = exclusions;
    }

    public async Task<int> ReconcileAsync(
        SourceId sourceId,
        IReadOnlyList<string> includedFolders,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(includedFolders);
        IReadOnlyList<string> coverage = ArchiveCoverage.NormalizeIncludedFolders(includedFolders);
        if (coverage.Count == 0)
        {
            return 0;
        }

        HashSet<string>? excludedKeys = null;
        if (_exclusions is not null)
        {
            IReadOnlyList<SourceCopyExclusionState> excluded = await _exclusions.ListAsync(sourceId, cancellationToken);
            excludedKeys = excluded
                .Select(static state => state.SourceKey)
                .ToHashSet(StringComparer.Ordinal);
        }

        await new SqliteArchiveSourceObservationRepository(_database).EnsureSchemaAsync(cancellationToken);
        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        List<MoveCandidate> candidates = await ReadCandidatesAsync(
            connection,
            transaction,
            sourceId,
            coverage,
            cancellationToken);
        if (excludedKeys is not null)
        {
            candidates.RemoveAll(candidate => excludedKeys.Contains(candidate.SourceKey));
        }

        int reconciled = 0;
        foreach (IGrouping<Sha256Digest, MoveCandidate> group in candidates.GroupBy(static candidate => candidate.ContentHash))
        {
            MoveCandidate[] current = group.Where(static candidate => candidate.DeletedAtUtc is null).ToArray();
            MoveCandidate[] missing = group.Where(static candidate => candidate.DeletedAtUtc is not null).ToArray();
            if (current.Length != 1 || missing.Length != 1)
            {
                continue;
            }

            MoveCandidate newCopy = current[0];
            MoveCandidate oldCopy = missing[0];
            if (newCopy.CreatedAtUtc != scannedAt || oldCopy.DeletedAtUtc != scannedAt)
            {
                continue;
            }

            await CopyCurrentObservationAsync(
                connection,
                transaction,
                oldCopy,
                newCopy,
                cancellationToken);
            await CopyCurrentAvailabilityAsync(
                connection,
                transaction,
                oldCopy.AssetId,
                newCopy.AssetId,
                cancellationToken);

            int deleted = await DeleteTransientNewAssetAsync(
                connection,
                transaction,
                newCopy,
                scannedAt,
                cancellationToken);
            if (deleted != 1)
            {
                throw new InvalidOperationException("The transient move target changed during reconciliation.");
            }

            int updated = await RetargetOldAssetAsync(
                connection,
                transaction,
                sourceId,
                oldCopy,
                newCopy.SourceKey,
                scannedAt,
                cancellationToken);
            if (updated != 1)
            {
                throw new InvalidOperationException("The missing move source changed during reconciliation.");
            }

            reconciled++;
        }

        transaction.Commit();
        return reconciled;
    }

    private static async Task<List<MoveCandidate>> ReadCandidatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceId sourceId,
        IReadOnlyList<string> coverage,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                asset.id,
                asset.source_key,
                asset.created_at_utc,
                asset.deleted_at_utc,
                observation.verified_revision_id,
                revision.content_sha256
            FROM assets AS asset
            INNER JOIN archive_source_observations AS observation
                ON observation.asset_id = asset.id
            INNER JOIN asset_revisions AS revision
                ON revision.id = observation.verified_revision_id
               AND revision.asset_id = asset.id
            WHERE asset.source_id = $source_id
              AND observation.verification_state = 'verified'
              AND observation.verified_revision_id IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());

        List<MoveCandidate> candidates = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string sourceKey = reader.GetString(1);
            if (!coverage.Any(folder => ArchiveCoverage.Covers(folder, sourceKey)))
            {
                continue;
            }

            candidates.Add(new MoveCandidate(
                AssetId.From(Guid.Parse(reader.GetString(0))),
                sourceKey,
                Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Parse(reader.GetString(3)),
                AssetRevisionId.From(Guid.Parse(reader.GetString(4))),
                new Sha256Digest(reader.GetString(5))));
        }

        return candidates;
    }

    private static async Task CopyCurrentObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MoveCandidate oldCopy,
        MoveCandidate newCopy,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE archive_source_observations
            SET observed_size_bytes = (
                    SELECT observed_size_bytes FROM archive_source_observations WHERE asset_id = $new_asset_id),
                observed_last_write_utc = (
                    SELECT observed_last_write_utc FROM archive_source_observations WHERE asset_id = $new_asset_id),
                observed_media_type = (
                    SELECT observed_media_type FROM archive_source_observations WHERE asset_id = $new_asset_id),
                observed_at_utc = (
                    SELECT observed_at_utc FROM archive_source_observations WHERE asset_id = $new_asset_id),
                verification_state = 'verified',
                verified_revision_id = $old_revision_id,
                verified_size_bytes = (
                    SELECT verified_size_bytes FROM archive_source_observations WHERE asset_id = $new_asset_id),
                verified_last_write_utc = (
                    SELECT verified_last_write_utc FROM archive_source_observations WHERE asset_id = $new_asset_id),
                verified_media_type = (
                    SELECT verified_media_type FROM archive_source_observations WHERE asset_id = $new_asset_id),
                verified_at_utc = (
                    SELECT verified_at_utc FROM archive_source_observations WHERE asset_id = $new_asset_id)
            WHERE asset_id = $old_asset_id;
            """;
        command.Parameters.AddWithValue("$old_asset_id", oldCopy.AssetId.ToString());
        command.Parameters.AddWithValue("$new_asset_id", newCopy.AssetId.ToString());
        command.Parameters.AddWithValue("$old_revision_id", oldCopy.VerifiedRevisionId.ToString());
        int updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException("Move reconciliation could not transfer the verified source observation.");
        }
    }

    private static async Task CopyCurrentAvailabilityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetId oldAssetId,
        AssetId newAssetId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
            SELECT $old_asset_id, availability, checked_at_utc
            FROM archive_asset_availability
            WHERE asset_id = $new_asset_id
            ON CONFLICT(asset_id) DO UPDATE SET
                availability = excluded.availability,
                checked_at_utc = excluded.checked_at_utc;
            """;
        command.Parameters.AddWithValue("$old_asset_id", oldAssetId.ToString());
        command.Parameters.AddWithValue("$new_asset_id", newAssetId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> DeleteTransientNewAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MoveCandidate newCopy,
        DateTimeOffset scannedAt,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM assets
            WHERE id = $asset_id
              AND source_key = $source_key
              AND created_at_utc = $scanned_at_utc
              AND deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$asset_id", newCopy.AssetId.ToString());
        command.Parameters.AddWithValue("$source_key", newCopy.SourceKey);
        command.Parameters.AddWithValue("$scanned_at_utc", Format(scannedAt));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> RetargetOldAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceId sourceId,
        MoveCandidate oldCopy,
        string newSourceKey,
        DateTimeOffset scannedAt,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE assets
            SET source_key = $new_source_key,
                last_seen_at_utc = $scanned_at_utc,
                deleted_at_utc = NULL
            WHERE id = $asset_id
              AND source_id = $source_id
              AND deleted_at_utc = $scanned_at_utc
              AND NOT EXISTS (
                  SELECT 1
                  FROM assets AS other
                  WHERE other.source_id = $source_id
                    AND other.source_key = $new_source_key
                    AND other.id <> $asset_id);
            """;
        command.Parameters.AddWithValue("$asset_id", oldCopy.AssetId.ToString());
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$new_source_key", newSourceKey);
        command.Parameters.AddWithValue("$scanned_at_utc", Format(scannedAt));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private sealed record MoveCandidate(
        AssetId AssetId,
        string SourceKey,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? DeletedAtUtc,
        AssetRevisionId VerifiedRevisionId,
        Sha256Digest ContentHash);
}
