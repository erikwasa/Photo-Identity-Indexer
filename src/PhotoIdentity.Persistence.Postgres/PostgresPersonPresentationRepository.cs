using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPersonPresentationRepository :
    IPersonPhotoCountRepository,
    IFavoritePeopleRepository,
    IPersonSmartCollectionVisibilityRepository,
    IPersonFeaturedFaceRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPersonPresentationRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyDictionary<PersonId, int>> GetActivePhotoCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
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
               AND btrim(people.display_name) <> ''
            GROUP BY effective_photo_people.person_id;
            """;

        Dictionary<PersonId, int> counts = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[PersonId.From(reader.GetGuid(0))] = checked((int)reader.GetInt64(1));
        }

        return counts;
    }

    public async Task<IReadOnlySet<PersonId>> GetFavoritePersonIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT person_id FROM person_favorites ORDER BY person_id;";

        HashSet<PersonId> result = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(PersonId.From(reader.GetGuid(0)));
        }

        return result;
    }

    public async Task SetFavoriteAsync(
        PersonId personId,
        bool isFavorite,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset changedAt = changedAtUtc.ToUniversalTime();
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isFavorite)
        {
            command.CommandText = """
                INSERT INTO person_favorites (person_id, favorited_at_utc)
                VALUES (@person_id, @favorited_at_utc)
                ON CONFLICT (person_id) DO UPDATE SET
                    favorited_at_utc = EXCLUDED.favorited_at_utc;
                """;
            command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
            command.Parameters.AddWithValue("favorited_at_utc", NpgsqlDbType.TimestampTz, changedAt);
        }
        else
        {
            command.CommandText = "DELETE FROM person_favorites WHERE person_id = @person_id;";
            command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<PersonId>> GetHiddenPersonIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT visibility.person_id
            FROM person_smart_collection_visibility AS visibility
            INNER JOIN people AS person ON person.id = visibility.person_id
            WHERE visibility.hidden_from_smart_collections
              AND person.merged_into_person_id IS NULL
            ORDER BY visibility.person_id;
            """;

        HashSet<PersonId> result = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(PersonId.From(reader.GetGuid(0)));
        }

        return result;
    }

    public async Task SetHiddenAsync(
        PersonId personId,
        bool hiddenFromSmartCollections,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset changedAt = changedAtUtc.ToUniversalTime();
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        if (hiddenFromSmartCollections)
        {
            command.CommandText = """
                INSERT INTO person_smart_collection_visibility (
                    person_id,
                    hidden_from_smart_collections,
                    changed_at_utc)
                VALUES (@person_id, TRUE, @changed_at_utc)
                ON CONFLICT (person_id) DO UPDATE SET
                    hidden_from_smart_collections = TRUE,
                    changed_at_utc = EXCLUDED.changed_at_utc;
                """;
            command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
            command.Parameters.AddWithValue("changed_at_utc", NpgsqlDbType.TimestampTz, changedAt);
        }
        else
        {
            command.CommandText = """
                DELETE FROM person_smart_collection_visibility
                WHERE person_id = @person_id;
                """;
            command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CataloguePersonRepresentativeFace?> ResolveAsync(
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, transaction: null, personId, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_action AS (
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
            )
            SELECT
                face_occurrences.id,
                CASE
                    WHEN featured.face_occurrence_id = face_occurrences.id THEN TRUE
                    ELSE FALSE
                END AS is_explicit
            FROM face_occurrences
            INNER JOIN asset_revisions
                ON asset_revisions.id = face_occurrences.asset_revision_id
            INNER JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
            LEFT JOIN person_featured_faces AS featured
                ON featured.person_id = @person_id
               AND featured.face_occurrence_id = face_occurrences.id
            WHERE latest_action.action_kind = 'assign'
              AND latest_action.person_id = @person_id
            ORDER BY
                is_explicit DESC,
                face_occurrences.created_at_utc,
                face_occurrences.id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CataloguePersonRepresentativeFace(
            personId,
            FaceOccurrenceId.From(reader.GetGuid(0)),
            reader.GetBoolean(1));
    }

    public async Task<IReadOnlyDictionary<PersonId, CataloguePersonRepresentativeFace>> ResolveAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_action AS (
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
            ranked_faces AS (
                SELECT
                    latest_action.person_id,
                    face_occurrences.id AS face_id,
                    CASE
                        WHEN featured.face_occurrence_id = face_occurrences.id THEN TRUE
                        ELSE FALSE
                    END AS is_explicit,
                    ROW_NUMBER() OVER (
                        PARTITION BY latest_action.person_id
                        ORDER BY
                            CASE
                                WHEN featured.face_occurrence_id = face_occurrences.id THEN 1
                                ELSE 0
                            END DESC,
                            face_occurrences.created_at_utc,
                            face_occurrences.id) AS representative_rank
                FROM face_occurrences
                INNER JOIN asset_revisions
                    ON asset_revisions.id = face_occurrences.asset_revision_id
                INNER JOIN latest_action
                    ON latest_action.face_occurrence_id = face_occurrences.id
                   AND latest_action.row_number = 1
                INNER JOIN people AS person
                    ON person.id = latest_action.person_id
                   AND person.merged_into_person_id IS NULL
                LEFT JOIN person_featured_faces AS featured
                    ON featured.person_id = latest_action.person_id
                   AND featured.face_occurrence_id = face_occurrences.id
                WHERE latest_action.action_kind = 'assign'
                  AND latest_action.person_id IS NOT NULL
            )
            SELECT person_id, face_id, is_explicit
            FROM ranked_faces
            WHERE representative_rank = 1;
            """;

        Dictionary<PersonId, CataloguePersonRepresentativeFace> representatives = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            PersonId personId = PersonId.From(reader.GetGuid(0));
            representatives[personId] = new CataloguePersonRepresentativeFace(
                personId,
                FaceOccurrenceId.From(reader.GetGuid(1)),
                reader.GetBoolean(2));
        }

        return representatives;
    }

    public async Task SetFeaturedFaceAsync(
        PersonId personId,
        FaceOccurrenceId faceId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset changedAt = changedAtUtc.ToUniversalTime();
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);
        await RequireFaceAsync(connection, transaction, faceId, cancellationToken);

        if (!await IsCurrentlyAssignedToPersonAsync(
                connection,
                transaction,
                personId,
                faceId,
                cancellationToken))
        {
            throw new ArgumentException(
                "The featured face must currently be assigned to the selected person.",
                nameof(faceId));
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO person_featured_faces (
                person_id,
                face_occurrence_id,
                changed_at_utc)
            VALUES (@person_id, @face_id, @changed_at_utc)
            ON CONFLICT (person_id) DO UPDATE SET
                face_occurrence_id = EXCLUDED.face_occurrence_id,
                changed_at_utc = EXCLUDED.changed_at_utc;
            """;
        command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        command.Parameters.AddWithValue("face_id", NpgsqlDbType.Uuid, faceId.Value);
        command.Parameters.AddWithValue("changed_at_utc", NpgsqlDbType.TimestampTz, changedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearFeaturedFaceAsync(
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM person_featured_faces WHERE person_id = @person_id;";
        command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> IsCurrentlyAssignedToPersonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId personId,
        FaceOccurrenceId faceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE
                WHEN review_actions.action_kind = 'assign'
                 AND review_actions.person_id = @person_id
                THEN TRUE ELSE FALSE
            END
            FROM review_actions
            WHERE review_actions.face_occurrence_id = @face_id
              AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
              AND review_actions.reversed_at_utc IS NULL
            ORDER BY review_actions.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        command.Parameters.AddWithValue("face_id", NpgsqlDbType.Uuid, faceId.Value);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is bool assigned && assigned;
    }

    private static async Task RequireActivePersonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM people
            WHERE id = @person_id
              AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue("person_id", NpgsqlDbType.Uuid, personId.Value);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Active person {0} was not found.", personId));
        }
    }

    private static async Task RequireFaceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM face_occurrences WHERE id = @face_id;";
        command.Parameters.AddWithValue("face_id", NpgsqlDbType.Uuid, faceId.Value);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Face occurrence {0} was not found.", faceId));
        }
    }
}
