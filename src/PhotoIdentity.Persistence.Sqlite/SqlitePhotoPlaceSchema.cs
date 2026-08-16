using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Places;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Compatibility-safe schema guard and legacy-assignment migrator for first-class Places.
/// Catalogue schema v14 formalizes these structures, while the guard remains idempotent for
/// direct repository use and normalizes any pre-release v14 preview shape before data access.
/// </summary>
public static class SqlitePhotoPlaceSchema
{
    public static async Task EnsureAndMigrateAsync(
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await EnsureAndMigrateAsync(connection, cancellationToken);
    }

    internal static async Task EnsureAndMigrateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using (SqliteCommand schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS photo_place_actions (
                    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    asset_revision_id TEXT NOT NULL,
                    tag_id INTEGER NULL,
                    action_kind TEXT NOT NULL CHECK (action_kind IN ('set', 'clear')),
                    source_kind TEXT NOT NULL CHECK (source_kind IN ('manual', 'automatic', 'migration')),
                    provider TEXT NULL,
                    actor TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                    FOREIGN KEY (tag_id) REFERENCES photo_tags (id) ON DELETE RESTRICT,
                    CHECK (
                        (action_kind = 'set' AND tag_id IS NOT NULL)
                        OR (action_kind = 'clear' AND tag_id IS NULL)
                    )
                );

                CREATE INDEX IF NOT EXISTS ix_photo_place_actions_revision_history
                    ON photo_place_actions (asset_revision_id, id DESC);
                CREATE INDEX IF NOT EXISTS ix_photo_place_actions_tag_history
                    ON photo_place_actions (tag_id, asset_revision_id, id DESC);

                CREATE TABLE IF NOT EXISTS photo_place_migration_conflicts (
                    asset_revision_id TEXT NOT NULL PRIMARY KEY,
                    candidate_values TEXT NOT NULL,
                    detected_at_utc TEXT NOT NULL,
                    resolved_at_utc TEXT NULL,
                    resolved_by TEXT NULL,
                    resolution_note TEXT NULL,
                    FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                    CHECK (length(candidate_values) > 0)
                );
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await HasColumnAsync(connection, "photo_place_actions", "provider", cancellationToken))
        {
            await NormalizePreviewSchemaAsync(connection, cancellationToken);
        }

        List<LegacyPlaceAssignment> legacy = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                WITH latest_actions AS (
                    SELECT
                        photo_tag_actions.asset_revision_id,
                        photo_tag_actions.tag_id,
                        photo_tag_actions.action_kind,
                        ROW_NUMBER() OVER (
                            PARTITION BY photo_tag_actions.asset_revision_id, photo_tag_actions.tag_id
                            ORDER BY photo_tag_actions.id DESC) AS row_number
                    FROM photo_tag_actions
                )
                SELECT
                    latest_actions.asset_revision_id,
                    photo_tags.id,
                    photo_tags.normalized_name,
                    photo_tags.display_name
                FROM latest_actions
                INNER JOIN photo_tags ON photo_tags.id = latest_actions.tag_id
                WHERE latest_actions.row_number = 1
                  AND latest_actions.action_kind = 'add'
                  AND (
                      photo_tags.normalized_name = 'places'
                      OR photo_tags.normalized_name LIKE 'places/%')
                ORDER BY latest_actions.asset_revision_id, photo_tags.normalized_name;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                legacy.Add(new LegacyPlaceAssignment(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }

        foreach (IGrouping<string, LegacyPlaceAssignment> group in legacy.GroupBy(
                     row => row.RevisionId,
                     StringComparer.Ordinal))
        {
            if (await HasPlaceHistoryAsync(connection, group.Key, cancellationToken))
            {
                continue;
            }

            LegacyPlaceAssignment[] candidates = group
                .GroupBy(row => row.TagId)
                .Select(rows => rows.First())
                .ToArray();
            bool chain = candidates.All(left => candidates.All(right =>
                IsAncestorOrSame(left.NormalizedValue, right.NormalizedValue) ||
                IsAncestorOrSame(right.NormalizedValue, left.NormalizedValue)));
            LegacyPlaceAssignment[] assignable = candidates
                .Where(candidate => !string.Equals(
                    candidate.NormalizedValue,
                    PhotoPlacePath.RootNormalizedName,
                    StringComparison.Ordinal))
                .ToArray();

            if (chain && assignable.Length > 0)
            {
                LegacyPlaceAssignment deepest = assignable
                    .OrderByDescending(candidate => candidate.NormalizedValue.Length)
                    .First();
                using SqliteCommand migrate = connection.CreateCommand();
                migrate.CommandText = """
                    INSERT INTO photo_place_actions (
                        asset_revision_id, tag_id, action_kind, source_kind, provider, actor, created_at_utc)
                    SELECT
                        $revision_id, $tag_id, 'set', 'migration', NULL,
                        'legacy-places-migration', $created_at_utc
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM photo_place_actions
                        WHERE asset_revision_id = $revision_id);
                    """;
                migrate.Parameters.AddWithValue("$revision_id", group.Key);
                migrate.Parameters.AddWithValue("$tag_id", deepest.TagId);
                migrate.Parameters.AddWithValue("$created_at_utc", UtcNow());
                await migrate.ExecuteNonQueryAsync(cancellationToken);
                continue;
            }

            string candidateValues = string.Join(
                '\n',
                candidates
                    .OrderBy(candidate => candidate.NormalizedValue, StringComparer.Ordinal)
                    .Select(candidate => candidate.DisplayValue));
            using SqliteCommand conflict = connection.CreateCommand();
            conflict.CommandText = """
                INSERT INTO photo_place_migration_conflicts (
                    asset_revision_id, candidate_values, detected_at_utc,
                    resolved_at_utc, resolved_by, resolution_note)
                VALUES ($revision_id, $candidate_values, $detected_at_utc, NULL, NULL, NULL)
                ON CONFLICT(asset_revision_id) DO UPDATE SET
                    candidate_values = excluded.candidate_values,
                    detected_at_utc = excluded.detected_at_utc
                WHERE photo_place_migration_conflicts.resolved_at_utc IS NULL;
                """;
            conflict.Parameters.AddWithValue("$revision_id", group.Key);
            conflict.Parameters.AddWithValue("$candidate_values", candidateValues);
            conflict.Parameters.AddWithValue("$detected_at_utc", UtcNow());
            await conflict.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task NormalizePreviewSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE photo_place_actions_normalized (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                asset_revision_id TEXT NOT NULL,
                tag_id INTEGER NULL,
                action_kind TEXT NOT NULL CHECK (action_kind IN ('set', 'clear')),
                source_kind TEXT NOT NULL CHECK (source_kind IN ('manual', 'automatic', 'migration')),
                provider TEXT NULL,
                actor TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                FOREIGN KEY (tag_id) REFERENCES photo_tags (id) ON DELETE RESTRICT,
                CHECK (
                    (action_kind = 'set' AND tag_id IS NOT NULL)
                    OR (action_kind = 'clear' AND tag_id IS NULL)
                )
            );

            INSERT INTO photo_place_actions_normalized (
                id, asset_revision_id, tag_id, action_kind, source_kind,
                provider, actor, created_at_utc)
            SELECT
                id,
                asset_revision_id,
                tag_id,
                action_kind,
                CASE source_kind
                    WHEN 'legacy-migration' THEN 'migration'
                    ELSE source_kind
                END,
                NULL,
                actor,
                created_at_utc
            FROM photo_place_actions
            ORDER BY id;

            DROP TABLE photo_place_actions;
            ALTER TABLE photo_place_actions_normalized RENAME TO photo_place_actions;
            CREATE INDEX ix_photo_place_actions_revision_history
                ON photo_place_actions (asset_revision_id, id DESC);
            CREATE INDEX ix_photo_place_actions_tag_history
                ON photo_place_actions (tag_id, asset_revision_id, id DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    private static async Task<bool> HasPlaceHistoryAsync(
        SqliteConnection connection,
        string revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM photo_place_actions
            WHERE asset_revision_id = $revision_id;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) > 0;
    }

    private static bool IsAncestorOrSame(string ancestor, string descendant) =>
        string.Equals(ancestor, descendant, StringComparison.Ordinal) ||
        descendant.StartsWith($"{ancestor}/", StringComparison.Ordinal);

    private static string UtcNow() =>
        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private sealed record LegacyPlaceAssignment(
        string RevisionId,
        long TagId,
        string NormalizedValue,
        string DisplayValue);
}
