using Npgsql;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Preserves the one historical Places-tag migration regression while running it against the
/// PostgreSQL catalogue. This is migration evidence only; normal PostgreSQL operation never calls
/// this helper.
/// </summary>
public static class PostgresPhotoPlaceSchemaCompatibility
{
    public static async Task EnsureAndMigrateAsync(
        PostgresTestCatalogueDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await database.InitializeAsync(cancellationToken);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);

        List<LegacyPlaceAssignment> legacy = [];
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
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

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                legacy.Add(new LegacyPlaceAssignment(
                    reader.GetGuid(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }

        foreach (IGrouping<Guid, LegacyPlaceAssignment> group in legacy.GroupBy(row => row.RevisionId))
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
                .Where(candidate => !string.Equals(candidate.NormalizedValue, "places", StringComparison.Ordinal))
                .ToArray();

            if (chain && assignable.Length > 0)
            {
                LegacyPlaceAssignment deepest = assignable
                    .OrderByDescending(candidate => candidate.NormalizedValue.Length)
                    .First();
                await using NpgsqlCommand migrate = connection.CreateCommand();
                migrate.CommandText =
                    """
                    INSERT INTO photo_place_actions (
                        asset_revision_id, tag_id, action_kind, source_kind, provider, actor, created_at_utc)
                    SELECT
                        @revision_id, @tag_id, 'set', 'migration', NULL,
                        'legacy-places-migration', @created_at_utc
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM photo_place_actions
                        WHERE asset_revision_id = @revision_id);
                    """;
                migrate.Parameters.AddWithValue("revision_id", group.Key);
                migrate.Parameters.AddWithValue("tag_id", deepest.TagId);
                migrate.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
                await migrate.ExecuteNonQueryAsync(cancellationToken);
                continue;
            }

            string candidateValues = string.Join(
                '\n',
                candidates
                    .OrderBy(candidate => candidate.NormalizedValue, StringComparer.Ordinal)
                    .Select(candidate => candidate.DisplayValue));
            await using NpgsqlCommand conflict = connection.CreateCommand();
            conflict.CommandText =
                """
                INSERT INTO photo_place_migration_conflicts (
                    asset_revision_id, candidate_values, detected_at_utc,
                    resolved_at_utc, resolved_by, resolution_note)
                VALUES (@revision_id, @candidate_values, @detected_at_utc, NULL, NULL, NULL)
                ON CONFLICT(asset_revision_id) DO UPDATE SET
                    candidate_values = EXCLUDED.candidate_values,
                    detected_at_utc = EXCLUDED.detected_at_utc
                WHERE photo_place_migration_conflicts.resolved_at_utc IS NULL;
                """;
            conflict.Parameters.AddWithValue("revision_id", group.Key);
            conflict.Parameters.AddWithValue("candidate_values", candidateValues);
            conflict.Parameters.AddWithValue("detected_at_utc", DateTimeOffset.UtcNow);
            await conflict.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> HasPlaceHistoryAsync(
        NpgsqlConnection connection,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM photo_place_actions
                WHERE asset_revision_id = @revision_id);
            """;
        command.Parameters.AddWithValue("revision_id", revisionId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static bool IsAncestorOrSame(string ancestor, string descendant) =>
        string.Equals(ancestor, descendant, StringComparison.Ordinal) ||
        descendant.StartsWith($"{ancestor}/", StringComparison.Ordinal);

    private sealed record LegacyPlaceAssignment(
        Guid RevisionId,
        long TagId,
        string NormalizedValue,
        string DisplayValue);
}
