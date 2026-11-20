using System.Data;
using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Internal, single-pair transactional implementation of reversible case-only
/// archive retirement. Deliberately NOT registered in API, CLI or hosted jobs.
/// Production application requires separate review of all active query surfaces.
/// No originals, revisions, faces, annotations or review actions are deleted.
/// </summary>
public sealed class PostgresArchiveCaseAliasRetirementService
{
    private const string AutoActor = "identity-matcher:auto";
    private const string MultiEvidenceAutoActor = "identity-matcher:auto-multi-evidence";
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveCaseAliasRetirementService(PostgresCatalogueDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<ArchiveAliasRetirementOutcome> RetireAsync(
        ArchiveCaseAliasRetirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        await LockSourceAsync(connection, transaction, request.SourceId, cancellationToken);
        AssetSnapshot[] assets = await ReadAndLockAssetsAsync(
            connection, transaction, request, cancellationToken);
        VerifyPinnedIdentity(request, assets);
        AssetSnapshot survivor = assets.Single(a => a.Id == request.SurvivorAssetId);
        AssetSnapshot secondary = assets.Single(a => a.Id == request.SecondaryAssetId);

        RetirementMapping? mapping = await ReadMappingAsync(
            connection, transaction, request.SecondaryAssetId, cancellationToken);
        if (mapping is not null)
        {
            VerifyMapping(request, mapping);
            if (mapping.IsRestored)
            {
                throw new InvalidOperationException(
                    "The secondary asset has already been retired and restored. " +
                    "A subsequent retirement requires an explicitly new audited workflow.");
            }
            if (survivor.DeletedAtUtc is not null || secondary.DeletedAtUtc is null)
            {
                throw new InvalidOperationException("Existing retirement mapping is inconsistent.");
            }

            await transaction.CommitAsync(cancellationToken);
            return ArchiveAliasRetirementOutcome.AlreadyRetired;
        }

        if (survivor.DeletedAtUtc is not null || secondary.DeletedAtUtc is not null)
        {
            throw new InvalidOperationException("Both candidate assets must currently be present.");
        }
        if (survivor.CreatedAtUtc >= secondary.CreatedAtUtc)
        {
            throw new InvalidOperationException("The survivor must be strictly older than the secondary.");
        }
        if (!string.Equals(survivor.VerificationState, "verified", StringComparison.Ordinal) ||
            !string.Equals(secondary.VerificationState, "verified", StringComparison.Ordinal) ||
            survivor.VerifiedRevisionId != request.SurvivorVerifiedRevisionId ||
            secondary.VerifiedRevisionId != request.SecondaryVerifiedRevisionId ||
            !string.Equals(survivor.ContentSha256, request.ExpectedSha256, StringComparison.Ordinal) ||
            !string.Equals(secondary.ContentSha256, request.ExpectedSha256, StringComparison.Ordinal) ||
            survivor.SizeBytes != request.ExpectedSizeBytes ||
            secondary.SizeBytes != request.ExpectedSizeBytes)
        {
            throw new InvalidOperationException("The verified revision/hash/size evidence changed.");
        }

        // A third source-key spelling, or a video at this normalized path,
        // makes both the selection and the scan remapping ambiguous.
        if (await ReadNormalizedGroupCountAsync(
            connection, transaction, request, cancellationToken) != 2)
        {
            throw new InvalidOperationException("The normalized source locator is not an exact pair.");
        }

        if (await HasSourceExclusionAsync(connection, transaction, request, cancellationToken))
        {
            throw new InvalidOperationException("A privacy exclusion exists for this source locator.");
        }
        if (await HasProtectedSecondaryHistoryAsync(
            connection, transaction, request.SecondaryAssetId, cancellationToken))
        {
            throw new InvalidOperationException(
                "The secondary asset has human/unknown review or protected revision-linked history.");
        }
        if (!await EffectiveCurrentAnnotationsMatchAsync(
            connection, transaction, request, cancellationToken))
        {
            throw new InvalidOperationException(
                "Current effective tags, manual people, Place or confirmed face identities differ.");
        }

        DateTimeOffset retiredAt = DateTimeOffset.UtcNow;
        using (NpgsqlCommand markSecondary = connection.CreateCommand())
        {
            markSecondary.Transaction = transaction;
            markSecondary.CommandText = """
                UPDATE assets SET deleted_at_utc = @retired_at
                WHERE id = @secondary_asset_id
                  AND source_id = @source_id
                  AND deleted_at_utc IS NULL;
                """;
            markSecondary.Parameters.AddWithValue("retired_at", retiredAt);
            markSecondary.Parameters.AddWithValue("secondary_asset_id", request.SecondaryAssetId);
            markSecondary.Parameters.AddWithValue("source_id", request.SourceId);
            if (await markSecondary.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Secondary asset changed during retirement.");
            }
        }

        using (NpgsqlCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO archive_case_alias_retirements (
                    secondary_asset_id, survivor_asset_id, source_id,
                    secondary_source_key, survivor_source_key, expected_sha256,
                    retired_at_utc, retired_by)
                VALUES (
                    @secondary, @survivor, @source,
                    @secondary_key, @survivor_key, @hash, @retired_at, @actor);
                """;
            insert.Parameters.AddWithValue("secondary", request.SecondaryAssetId);
            insert.Parameters.AddWithValue("survivor", request.SurvivorAssetId);
            insert.Parameters.AddWithValue("source", request.SourceId);
            insert.Parameters.AddWithValue("secondary_key", request.SecondarySourceKey);
            insert.Parameters.AddWithValue("survivor_key", request.SurvivorSourceKey);
            insert.Parameters.AddWithValue("hash", request.ExpectedSha256);
            insert.Parameters.AddWithValue("retired_at", retiredAt);
            insert.Parameters.AddWithValue("actor", request.Actor);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ArchiveAliasRetirementOutcome.Retired;
    }

    /// <summary>
    /// Explicit rollback of a previously retired pair. The full revision and
    /// annotation history was never detached; this restores active visibility.
    /// This is also deliberately not exposed through any operator surface.
    /// </summary>
    public async Task<ArchiveAliasRetirementOutcome> RestoreAsync(
        ArchiveCaseAliasRetirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        await LockSourceAsync(connection, transaction, request.SourceId, cancellationToken);
        AssetSnapshot[] assets = await ReadAndLockAssetsAsync(
            connection, transaction, request, cancellationToken);
        VerifyPinnedIdentity(request, assets);
        RetirementMapping? mapping = await ReadMappingAsync(
            connection, transaction, request.SecondaryAssetId, cancellationToken);
        if (mapping is null)
        {
            throw new InvalidOperationException("No retirement mapping exists for the secondary asset.");
        }
        VerifyMapping(request, mapping);

        AssetSnapshot survivor = assets.Single(a => a.Id == request.SurvivorAssetId);
        AssetSnapshot secondary = assets.Single(a => a.Id == request.SecondaryAssetId);
        if (survivor.DeletedAtUtc is not null)
        {
            throw new InvalidOperationException("The survivor is missing; restoration was not applied.");
        }

        if (mapping.IsRestored)
        {
            if (secondary.DeletedAtUtc is not null)
            {
                throw new InvalidOperationException("Previously restored asset is unexpectedly missing.");
            }
            await transaction.CommitAsync(cancellationToken);
            return ArchiveAliasRetirementOutcome.AlreadyRestored;
        }
        if (secondary.DeletedAtUtc is null)
        {
            throw new InvalidOperationException("Retired secondary asset is unexpectedly present.");
        }
        if (await HasSourceExclusionAsync(connection, transaction, request, cancellationToken))
        {
            throw new InvalidOperationException(
                "The source locator is now excluded; restoration is prohibited.");
        }

        using (NpgsqlCommand restore = connection.CreateCommand())
        {
            restore.Transaction = transaction;
            restore.CommandText = """
                UPDATE archive_case_alias_retirements
                SET restored_at_utc = @restored_at, restored_by = @actor
                WHERE secondary_asset_id = @secondary
                  AND survivor_asset_id = @survivor
                  AND restored_at_utc IS NULL;

                UPDATE assets SET deleted_at_utc = NULL
                WHERE id = @secondary
                  AND source_id = @source
                  AND deleted_at_utc IS NOT NULL;
                """;
            restore.Parameters.AddWithValue("restored_at", DateTimeOffset.UtcNow);
            restore.Parameters.AddWithValue("actor", request.Actor);
            restore.Parameters.AddWithValue("secondary", request.SecondaryAssetId);
            restore.Parameters.AddWithValue("survivor", request.SurvivorAssetId);
            restore.Parameters.AddWithValue("source", request.SourceId);
            // Execute the two commands separately for accurate affected-row
            // accounting; rollback the transaction on any mismatch.
            restore.CommandText = """
                UPDATE archive_case_alias_retirements
                SET restored_at_utc = @restored_at, restored_by = @actor
                WHERE secondary_asset_id = @secondary
                  AND survivor_asset_id = @survivor
                  AND restored_at_utc IS NULL;
                """;
            if (await restore.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Retirement mapping changed during restore.");
            }
            restore.CommandText = """
                UPDATE assets SET deleted_at_utc = NULL
                WHERE id = @secondary AND source_id = @source
                  AND deleted_at_utc IS NOT NULL;
                """;
            if (await restore.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Secondary asset changed during restore.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return ArchiveAliasRetirementOutcome.Restored;
    }

    private static void ValidateRequest(ArchiveCaseAliasRetirementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SourceId == Guid.Empty ||
            request.SurvivorAssetId == Guid.Empty ||
            request.SecondaryAssetId == Guid.Empty ||
            request.SurvivorVerifiedRevisionId == Guid.Empty ||
            request.SecondaryVerifiedRevisionId == Guid.Empty ||
            request.SurvivorAssetId == request.SecondaryAssetId ||
            request.SurvivorVerifiedRevisionId == request.SecondaryVerifiedRevisionId ||
            request.ExpectedSizeBytes < 0 ||
            request.ExpectedSha256 is null ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                request.ExpectedSha256, "^[0-9a-f]{64}$") ||
            string.IsNullOrWhiteSpace(request.Actor) ||
            string.IsNullOrWhiteSpace(request.SurvivorSourceKey) ||
            string.IsNullOrWhiteSpace(request.SecondarySourceKey) ||
            string.Equals(request.SurvivorSourceKey, request.SecondarySourceKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                NormalizeKey(request.SurvivorSourceKey),
                NormalizeKey(request.SecondarySourceKey),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid or ambiguous archive alias retirement request.", nameof(request));
        }
    }

    private static string NormalizeKey(string key) =>
        key.Replace('\\', '/').ToLowerInvariant();

    private static async Task LockSourceAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid sourceId, CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT id FROM sources WHERE id = @source FOR UPDATE;";
        command.Parameters.AddWithValue("source", sourceId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid)
        {
            throw new InvalidOperationException("Archive source no longer exists.");
        }
    }

    private static async Task<AssetSnapshot[]> ReadAndLockAssetsAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        ArchiveCaseAliasRetirementRequest request, CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT
                asset.id, asset.source_id, asset.source_key, asset.created_at_utc,
                asset.deleted_at_utc, asset.media_kind,
                observation.verification_state, observation.verified_revision_id,
                revision.content_sha256, revision.size_bytes
            FROM assets AS asset
            LEFT JOIN archive_source_observations AS observation
              ON observation.asset_id = asset.id
            LEFT JOIN asset_revisions AS revision
              ON revision.id = observation.verified_revision_id
             AND revision.asset_id = asset.id
            WHERE asset.id IN (@survivor, @secondary)
            ORDER BY asset.id
            FOR UPDATE OF asset;
            """;
        command.Parameters.AddWithValue("survivor", request.SurvivorAssetId);
        command.Parameters.AddWithValue("secondary", request.SecondaryAssetId);
        List<AssetSnapshot> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AssetSnapshot(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3).ToUniversalTime(),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4).ToUniversalTime(),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9)));
        }
        if (rows.Count != 2)
        {
            throw new InvalidOperationException("Both original asset identities must still exist.");
        }
        return [.. rows];
    }

    private static void VerifyPinnedIdentity(
        ArchiveCaseAliasRetirementRequest request, IReadOnlyList<AssetSnapshot> snapshots)
    {
        AssetSnapshot survivor = snapshots.Single(a => a.Id == request.SurvivorAssetId);
        AssetSnapshot secondary = snapshots.Single(a => a.Id == request.SecondaryAssetId);
        if (survivor.SourceId != request.SourceId ||
            secondary.SourceId != request.SourceId ||
            !string.Equals(survivor.SourceKey, request.SurvivorSourceKey, StringComparison.Ordinal) ||
            !string.Equals(secondary.SourceKey, request.SecondarySourceKey, StringComparison.Ordinal) ||
            survivor.CreatedAtUtc != request.SurvivorCreatedAtUtc.ToUniversalTime() ||
            secondary.CreatedAtUtc != request.SecondaryCreatedAtUtc.ToUniversalTime() ||
            survivor.MediaKind != "photo" || secondary.MediaKind != "photo")
        {
            throw new InvalidOperationException(
                "The immutable candidate identities, source paths, or creation timestamps changed.");
        }
    }

    private static async Task<long> ReadNormalizedGroupCountAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        ArchiveCaseAliasRetirementRequest request, CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT count(*) FROM assets
            WHERE source_id = @source
              AND lower(replace(source_key, chr(92), '/')) = @normalized;
            """;
        command.Parameters.AddWithValue("source", request.SourceId);
        command.Parameters.AddWithValue("normalized", NormalizeKey(request.SurvivorSourceKey));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<RetirementMapping?> ReadMappingAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, Guid secondaryId,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT survivor_asset_id, source_id, secondary_source_key,
                survivor_source_key, expected_sha256, restored_at_utc IS NOT NULL
            FROM archive_case_alias_retirements
            WHERE secondary_asset_id = @secondary
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("secondary", secondaryId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) { return null; }
        return new RetirementMapping(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetBoolean(5));
    }

    private static void VerifyMapping(
        ArchiveCaseAliasRetirementRequest request, RetirementMapping mapping)
    {
        if (mapping.SurvivorAssetId != request.SurvivorAssetId ||
            mapping.SourceId != request.SourceId ||
            !string.Equals(mapping.SecondarySourceKey, request.SecondarySourceKey,
                StringComparison.Ordinal) ||
            !string.Equals(mapping.SurvivorSourceKey, request.SurvivorSourceKey,
                StringComparison.Ordinal) ||
            !string.Equals(mapping.ContentHash, request.ExpectedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Retirement mapping does not match the pinned request.");
        }
    }

    private static async Task<bool> HasSourceExclusionAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        ArchiveCaseAliasRetirementRequest request, CancellationToken cancellationToken)
    {
        using NpgsqlCommand exists = connection.CreateCommand();
        exists.Transaction = tx;
        exists.CommandText = "SELECT to_regclass('source_copy_exclusions') IS NOT NULL;";
        bool tableExists = (bool)(await exists.ExecuteScalarAsync(cancellationToken))!;
        if (!tableExists) { return false; }

        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM source_copy_exclusions
                WHERE source_id = @source
                  AND lower(replace(source_key, chr(92), '/')) = @normalized
            );
            """;
        command.Parameters.AddWithValue("source", request.SourceId);
        command.Parameters.AddWithValue("normalized", NormalizeKey(request.SurvivorSourceKey));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> HasProtectedSecondaryHistoryAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid secondaryId, CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            WITH revisions AS (
                SELECT id FROM asset_revisions WHERE asset_id = @asset
            ), faces AS (
                SELECT id FROM face_occurrences WHERE asset_revision_id IN (SELECT id FROM revisions)
            )
            SELECT
                EXISTS (SELECT 1 FROM photo_tag_actions WHERE asset_revision_id IN (SELECT id FROM revisions))
             OR EXISTS (SELECT 1 FROM photo_person_actions WHERE asset_revision_id IN (SELECT id FROM revisions))
             OR EXISTS (SELECT 1 FROM photo_place_actions WHERE asset_revision_id IN (SELECT id FROM revisions) AND source_kind='manual')
             OR EXISTS (SELECT 1 FROM photo_capture_date_actions WHERE asset_revision_id IN (SELECT id FROM revisions))
             OR EXISTS (SELECT 1 FROM photo_presentation_preference_actions WHERE asset_revision_id IN (SELECT id FROM revisions))
             OR EXISTS (SELECT 1 FROM photo_list_collection_items WHERE asset_revision_id IN (SELECT id FROM revisions))
             OR EXISTS (SELECT 1 FROM person_featured_faces WHERE face_occurrence_id IN (SELECT id FROM faces))
             OR EXISTS (
                 SELECT 1 FROM review_actions
                 WHERE face_occurrence_id IN (SELECT id FROM faces)
                   AND actor NOT IN (@auto_actor, @multi_actor))
             OR EXISTS (
                 SELECT 1 FROM person_labels
                 WHERE face_occurrence_id IN (SELECT id FROM faces)
                   AND assigned_by NOT IN (@auto_actor, @multi_actor));
            """;
        command.Parameters.AddWithValue("asset", secondaryId);
        command.Parameters.AddWithValue("auto_actor", AutoActor);
        command.Parameters.AddWithValue("multi_actor", MultiEvidenceAutoActor);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> EffectiveCurrentAnnotationsMatchAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        ArchiveCaseAliasRetirementRequest request, CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            WITH current AS (
                SELECT a.id AS asset_id, o.verified_revision_id AS revision_id
                FROM assets AS a
                JOIN archive_source_observations AS o ON o.asset_id = a.id
                WHERE a.id IN (@survivor, @secondary)
            ), effective AS (
                SELECT current.asset_id,
                    ARRAY(
                        SELECT latest.tag_id
                        FROM (
                            SELECT DISTINCT ON (action.tag_id)
                                action.tag_id, action.action_kind
                            FROM photo_tag_actions AS action
                            WHERE action.asset_revision_id = current.revision_id
                            ORDER BY action.tag_id, action.id DESC
                        ) AS latest
                        WHERE latest.action_kind = 'add'
                        ORDER BY latest.tag_id
                    ) AS tag_ids,
                    ARRAY(
                        SELECT latest.person_id
                        FROM (
                            SELECT DISTINCT ON (action.person_id)
                                action.person_id, action.action_kind
                            FROM photo_person_actions AS action
                            WHERE action.asset_revision_id = current.revision_id
                            ORDER BY action.person_id, action.id DESC
                        ) AS latest
                        INNER JOIN people AS person
                            ON person.id = latest.person_id
                           AND person.merged_into_person_id IS NULL
                        WHERE latest.action_kind = 'add'
                        ORDER BY latest.person_id
                    ) AS manual_person_ids,
                    (
                        SELECT CASE WHEN action.action_kind='set'
                                    THEN action.tag_id END
                        FROM photo_place_actions AS action
                        WHERE action.asset_revision_id = current.revision_id
                        ORDER BY action.id DESC LIMIT 1
                    ) AS place_tag_id,
                    (
                        SELECT CASE WHEN action.action_kind='set'
                                    THEN action.source_kind END
                        FROM photo_place_actions AS action
                        WHERE action.asset_revision_id = current.revision_id
                        ORDER BY action.id DESC LIMIT 1
                    ) AS place_source_kind,
                    ARRAY(
                        SELECT DISTINCT confirmed.person_id
                        FROM (
                            SELECT DISTINCT ON (face.id)
                                face.id, action.action_kind, action.person_id
                            FROM face_occurrences AS face
                            JOIN review_actions AS action ON action.face_occurrence_id = face.id
                            WHERE face.asset_revision_id = current.revision_id
                              AND action.reversed_at_utc IS NULL
                              AND action.action_kind IN ('assign','unknown','reject')
                            ORDER BY face.id, action.id DESC
                        ) AS confirmed
                        JOIN people AS person
                          ON person.id = confirmed.person_id
                         AND person.merged_into_person_id IS NULL
                        WHERE confirmed.action_kind='assign'
                        ORDER BY confirmed.person_id
                    ) AS confirmed_face_people
                FROM current
            )
            SELECT
                survivor.tag_ids = secondary.tag_ids
                AND survivor.manual_person_ids = secondary.manual_person_ids
                AND survivor.place_tag_id IS NOT DISTINCT FROM secondary.place_tag_id
                AND survivor.place_source_kind IS NOT DISTINCT FROM secondary.place_source_kind
                AND survivor.confirmed_face_people = secondary.confirmed_face_people
            FROM effective AS survivor
            JOIN effective AS secondary ON secondary.asset_id = @secondary
            WHERE survivor.asset_id = @survivor;
            """;
        command.Parameters.AddWithValue("survivor", request.SurvivorAssetId);
        command.Parameters.AddWithValue("secondary", request.SecondaryAssetId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private sealed record AssetSnapshot(
        Guid Id, Guid SourceId, string SourceKey,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? DeletedAtUtc,
        string MediaKind, string? VerificationState,
        Guid? VerifiedRevisionId, string? ContentSha256, long? SizeBytes);

    private sealed record RetirementMapping(
        Guid SurvivorAssetId, Guid SourceId, string SecondarySourceKey,
        string SurvivorSourceKey, string ContentHash, bool IsRestored);
}

/// <summary>
/// Immutable evidence pinned from an explicit earlier-survivor candidate.
/// Not read from a CSV by this service: the caller must supply each field.
/// The transaction re-reads the authoritative current catalogue.
/// </summary>
public sealed record ArchiveCaseAliasRetirementRequest(
    Guid SourceId,
    Guid SurvivorAssetId,
    Guid SecondaryAssetId,
    string SurvivorSourceKey,
    string SecondarySourceKey,
    Guid SurvivorVerifiedRevisionId,
    Guid SecondaryVerifiedRevisionId,
    DateTimeOffset SurvivorCreatedAtUtc,
    DateTimeOffset SecondaryCreatedAtUtc,
    string ExpectedSha256,
    long ExpectedSizeBytes,
    string Actor);

public enum ArchiveAliasRetirementOutcome
{
    Retired,
    AlreadyRetired,
    Restored,
    AlreadyRestored
}
