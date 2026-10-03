using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoListCollectionCaptureTimeRepository
    : IPhotoListCollectionCaptureTimeRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPhotoListCollectionCaptureTimeRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<PhotoListCollectionCaptureTime>> GetCaptureTimesAsync(
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        if (revisionIds.Count == 0)
        {
            return [];
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH requested AS (
                SELECT requested.revision_id, requested.position
                FROM unnest(@revision_ids::uuid[]) WITH ORDINALITY
                    AS requested(revision_id, position)
            ),
            latest_capture_date_action AS (
                SELECT
                    action.asset_revision_id,
                    action.action_kind,
                    action.capture_year,
                    action.capture_month,
                    action.capture_day,
                    ROW_NUMBER() OVER (
                        PARTITION BY action.asset_revision_id
                        ORDER BY action.id DESC) AS row_number
                FROM photo_capture_date_actions AS action
                INNER JOIN requested
                    ON requested.revision_id = action.asset_revision_id
            )
            SELECT
                requested.revision_id,
                CASE
                    WHEN latest_capture_date_action.action_kind = 'set' THEN
                        make_date(
                            latest_capture_date_action.capture_year,
                            COALESCE(latest_capture_date_action.capture_month, 1),
                            COALESCE(latest_capture_date_action.capture_day, 1))::timestamp
                    WHEN photo_capture_metadata.taken_at_local IS NOT NULL THEN
                        photo_capture_metadata.taken_at_local
                    ELSE NULL
                END AS effective_taken_at_local
            FROM requested
            LEFT JOIN latest_capture_date_action
                ON latest_capture_date_action.asset_revision_id = requested.revision_id
               AND latest_capture_date_action.row_number = 1
            LEFT JOIN photo_capture_metadata
                ON photo_capture_metadata.asset_revision_id = requested.revision_id
            ORDER BY requested.position;
            """;
        command.Parameters.AddWithValue(
            "revision_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            revisionIds.Select(item => item.Value).ToArray());

        List<PhotoListCollectionCaptureTime> results = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PhotoListCollectionCaptureTime(
                AssetRevisionId.From(reader.GetGuid(0)),
                reader.IsDBNull(1)
                    ? null
                    : DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Unspecified)));
        }

        return results;
    }
}
