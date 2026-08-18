using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persists an optional explicit representative face and resolves a safe deterministic fallback.
/// </summary>
public sealed class SqlitePersonFeaturedFaceRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqlitePersonFeaturedFaceRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CataloguePersonRepresentativeFace?> ResolveAsync(
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await SqlitePersonFeaturedFaceSchema.EnsureAsync(_database, cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await RequireActivePersonAsync(connection, transaction: null, personId, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
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
                    WHEN featured.face_occurrence_id = face_occurrences.id THEN 1
                    ELSE 0
                END AS is_explicit
            FROM face_occurrences
            INNER JOIN asset_revisions
                ON asset_revisions.id = face_occurrences.asset_revision_id
            INNER JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
            LEFT JOIN person_featured_faces AS featured
                ON featured.person_id = $person_id
               AND featured.face_occurrence_id = face_occurrences.id
            WHERE latest_action.action_kind = 'assign'
              AND latest_action.person_id = $person_id
            ORDER BY
                is_explicit DESC,
                face_occurrences.created_at_utc,
                face_occurrences.id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CataloguePersonRepresentativeFace(
            personId,
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
            reader.GetInt32(1) == 1);
    }

    public async Task<IReadOnlyDictionary<PersonId, CataloguePersonRepresentativeFace>> ResolveAllAsync(
        CancellationToken cancellationToken = default)
    {
        await SqlitePersonFeaturedFaceSchema.EnsureAsync(_database, cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
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
                        WHEN featured.face_occurrence_id = face_occurrences.id THEN 1
                        ELSE 0
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
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            PersonId personId = PersonId.From(Guid.Parse(reader.GetString(0)));
            representatives[personId] = new CataloguePersonRepresentativeFace(
                personId,
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
                reader.GetInt32(2) == 1);
        }

        return representatives;
    }

    public async Task SetFeaturedFaceAsync(
        PersonId personId,
        FaceOccurrenceId faceId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await SqlitePersonFeaturedFaceSchema.EnsureAsync(_database, cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
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

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO person_featured_faces (
                person_id,
                face_occurrence_id,
                changed_at_utc)
            VALUES ($person_id, $face_id, $changed_at_utc)
            ON CONFLICT(person_id) DO UPDATE SET
                face_occurrence_id = excluded.face_occurrence_id,
                changed_at_utc = excluded.changed_at_utc;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$changed_at_utc", Format(changedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    public async Task ClearFeaturedFaceAsync(
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        await SqlitePersonFeaturedFaceSchema.EnsureAsync(_database, cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM person_featured_faces WHERE person_id = $person_id;";
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    private static async Task<bool> IsCurrentlyAssignedToPersonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId personId,
        FaceOccurrenceId faceId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE
                WHEN review_actions.action_kind = 'assign'
                 AND review_actions.person_id = $person_id
                THEN 1 ELSE 0
            END
            FROM review_actions
            WHERE review_actions.face_occurrence_id = $face_id
              AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
              AND review_actions.reversed_at_utc IS NULL
            ORDER BY review_actions.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task RequireActivePersonAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM people
            WHERE id = $person_id
              AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException($"Active person {personId} was not found.");
        }
    }

    private static async Task RequireFaceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM face_occurrences WHERE id = $face_id;";
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException($"Face occurrence {faceId} was not found.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
