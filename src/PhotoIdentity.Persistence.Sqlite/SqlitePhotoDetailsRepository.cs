using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CataloguePhotoDetailsPerson(
    PersonId PersonId,
    string DisplayName,
    int ConfirmedFaceCount,
    bool ManualPresence);

public sealed record CataloguePhotoDetails(
    AssetRevisionId RevisionId,
    string SourceKey,
    IReadOnlyList<CataloguePhotoDetailsPerson> People);

/// <summary>
/// Reads revision-level catalogue details without opening the authoritative original.
/// SourceKey remains server-side input; browser-facing code must reduce it to a file name.
/// Confirmed face evidence and manual photo-level presence are consolidated without conflating them.
/// </summary>
public sealed class SqlitePhotoDetailsRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqlitePhotoDetailsRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CataloguePhotoDetails?> GetAsync(
        AssetRevisionId revisionId,
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
            confirmed_face_people AS (
                SELECT
                    latest_face_action.person_id,
                    COUNT(*) AS confirmed_face_count
                FROM face_occurrences
                INNER JOIN latest_face_action
                    ON latest_face_action.face_occurrence_id = face_occurrences.id
                   AND latest_face_action.row_number = 1
                   AND latest_face_action.action_kind = 'assign'
                INNER JOIN people
                    ON people.id = latest_face_action.person_id
                   AND people.merged_into_person_id IS NULL
                WHERE face_occurrences.asset_revision_id = $revision_id
                GROUP BY latest_face_action.person_id
            ),
            latest_manual_action AS (
                SELECT
                    photo_person_actions.person_id,
                    photo_person_actions.action_kind,
                    ROW_NUMBER() OVER (
                        PARTITION BY photo_person_actions.person_id
                        ORDER BY photo_person_actions.id DESC) AS row_number
                FROM photo_person_actions
                WHERE photo_person_actions.asset_revision_id = $revision_id
            ),
            manual_people AS (
                SELECT latest_manual_action.person_id
                FROM latest_manual_action
                INNER JOIN people
                    ON people.id = latest_manual_action.person_id
                   AND people.merged_into_person_id IS NULL
                WHERE latest_manual_action.row_number = 1
                  AND latest_manual_action.action_kind = 'add'
            ),
            person_evidence AS (
                SELECT person_id FROM confirmed_face_people
                UNION
                SELECT person_id FROM manual_people
            )
            SELECT
                asset_revisions.id,
                assets.source_key,
                people.id,
                people.display_name,
                COALESCE(confirmed_face_people.confirmed_face_count, 0),
                CASE WHEN manual_people.person_id IS NULL THEN 0 ELSE 1 END AS manual_presence
            FROM asset_revisions
            INNER JOIN assets
                ON assets.id = asset_revisions.asset_id
            LEFT JOIN person_evidence ON 1 = 1
            LEFT JOIN people
                ON people.id = person_evidence.person_id
            LEFT JOIN confirmed_face_people
                ON confirmed_face_people.person_id = people.id
            LEFT JOIN manual_people
                ON manual_people.person_id = people.id
            WHERE asset_revisions.id = $revision_id
            ORDER BY people.display_name COLLATE NOCASE, people.id;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());

        string? sourceKey = null;
        List<CataloguePhotoDetailsPerson> people = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sourceKey ??= reader.GetString(1);
            if (reader.IsDBNull(2))
            {
                continue;
            }

            people.Add(new CataloguePhotoDetailsPerson(
                PersonId.From(Guid.Parse(reader.GetString(2))),
                reader.GetString(3),
                checked((int)reader.GetInt64(4)),
                reader.GetInt64(5) != 0));
        }

        return sourceKey is null
            ? null
            : new CataloguePhotoDetails(revisionId, sourceKey, people);
    }
}
