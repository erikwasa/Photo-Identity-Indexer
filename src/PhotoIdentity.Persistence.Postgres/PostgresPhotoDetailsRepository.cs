using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoDetailsRepository : IPhotoDetailsRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly PostgresPhotoCaptureMetadataRepository _captureMetadata;
    private readonly PostgresExtendedPhotoMetadataRepository _extendedMetadata;

    public PostgresPhotoDetailsRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _captureMetadata = new PostgresPhotoCaptureMetadataRepository(database);
        _extendedMetadata = new PostgresExtendedPhotoMetadataRepository(database);
    }

    public async Task<PhotoDetails?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        string? sourceKey = null;
        List<PhotoDetailsPerson> people = [];

        await using (NpgsqlConnection connection =
                     await _database.OpenConnectionAsync(cancellationToken))
        {
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """
                WITH latest_face_action AS (
                    SELECT
                        review_actions.face_occurrence_id,
                        review_actions.action_kind,
                        review_actions.person_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY review_actions.face_occurrence_id
                            ORDER BY id DESC) AS row_number
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
                    WHERE face_occurrences.asset_revision_id = @revision_id
                    GROUP BY latest_face_action.person_id
                ),
                latest_manual_action AS (
                    SELECT
                        photo_person_actions.person_id,
                        photo_person_actions.action_kind,
                        ROW_NUMBER() OVER (
                            PARTITION BY photo_person_actions.person_id
                            ORDER BY id DESC) AS row_number
                    FROM photo_person_actions
                    WHERE photo_person_actions.asset_revision_id = @revision_id
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
                    revision.id,
                    asset.source_key,
                    people.id,
                    people.display_name,
                    COALESCE(confirmed_face_people.confirmed_face_count, 0),
                    CASE WHEN manual_people.person_id IS NULL THEN FALSE ELSE TRUE END AS manual_presence
                FROM asset_revisions AS revision
                INNER JOIN assets AS asset
                    ON asset.id = revision.asset_id
                LEFT JOIN person_evidence ON TRUE
                LEFT JOIN people
                    ON people.id = person_evidence.person_id
                LEFT JOIN confirmed_face_people
                    ON confirmed_face_people.person_id = people.id
                LEFT JOIN manual_people
                    ON manual_people.person_id = people.id
                WHERE revision.id = @revision_id
                ORDER BY lower(people.display_name), people.id;
                """;
            command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));

            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sourceKey ??= reader.GetString(1);
                if (reader.IsDBNull(2))
                {
                    continue;
                }

                people.Add(new PhotoDetailsPerson(
                    PersonId.From(reader.GetGuid(2)),
                    reader.GetString(3),
                    checked((int)reader.GetInt64(4)),
                    reader.GetBoolean(5)));
            }
        }

        if (sourceKey is null)
        {
            return null;
        }

        PhotoCaptureMetadata? captureMetadata =
            await _captureMetadata.GetPhotoMetadataAsync(revisionId, cancellationToken);
        CatalogueExtendedPhotoMetadata? extendedMetadata = captureMetadata is null
            ? null
            : await _extendedMetadata.GetAsync(revisionId, cancellationToken);

        return new PhotoDetails(
            revisionId,
            sourceKey,
            people,
            captureMetadata,
            extendedMetadata);
    }
}
