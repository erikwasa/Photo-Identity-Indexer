using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Counts distinct immutable photo revisions where an active person currently appears through
/// confirmed face assignment and/or effective manual photo-level presence.
/// </summary>
public sealed class SqlitePersonPhotoCountRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqlitePersonPhotoCountRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyDictionary<PersonId, int>> GetActivePhotoCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPersonSchema.EnsureAsync(connection, transaction: null, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_face_action AS (
                SELECT
                    review_actions.face_occurrence_id,
                    review_actions.action_kind,
                    review_actions.person_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY review_actions.face_occurrence_id
                        ORDER BY review_actions.id DESC) AS row_number
                FROM review_actions
                WHERE review_actions.action_kind IN ('assign', 'unknown', 'reject')
                  AND review_actions.reversed_at_utc IS NULL
            ),
            confirmed_photo_people AS (
                SELECT DISTINCT
                    face_occurrences.asset_revision_id,
                    latest_face_action.person_id
                FROM face_occurrences
                INNER JOIN latest_face_action
                    ON latest_face_action.face_occurrence_id = face_occurrences.id
                   AND latest_face_action.row_number = 1
                   AND latest_face_action.action_kind = 'assign'
                WHERE latest_face_action.person_id IS NOT NULL
            ),
            latest_manual_action AS (
                SELECT
                    photo_person_actions.asset_revision_id,
                    photo_person_actions.person_id,
                    photo_person_actions.action_kind,
                    ROW_NUMBER() OVER (
                        PARTITION BY photo_person_actions.asset_revision_id, photo_person_actions.person_id
                        ORDER BY photo_person_actions.id DESC) AS row_number
                FROM photo_person_actions
            ),
            manual_photo_people AS (
                SELECT asset_revision_id, person_id
                FROM latest_manual_action
                WHERE row_number = 1
                  AND action_kind = 'add'
            ),
            effective_photo_people AS (
                SELECT asset_revision_id, person_id FROM confirmed_photo_people
                UNION
                SELECT asset_revision_id, person_id FROM manual_photo_people
            )
            SELECT
                effective_photo_people.person_id,
                COUNT(*)
            FROM effective_photo_people
            INNER JOIN people
                ON people.id = effective_photo_people.person_id
               AND people.merged_into_person_id IS NULL
               AND people.display_name IS NOT NULL
               AND TRIM(people.display_name) <> ''
            GROUP BY effective_photo_people.person_id;
            """;

        Dictionary<PersonId, int> counts = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[PersonId.From(Guid.Parse(reader.GetString(0)))] = checked((int)reader.GetInt64(1));
        }

        return counts;
    }
}
