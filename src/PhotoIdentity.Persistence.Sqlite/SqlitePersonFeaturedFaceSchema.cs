using Microsoft.Data.Sqlite;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Idempotently creates the durable person-to-featured-face preference used by person-oriented presentation.
/// </summary>
public static class SqlitePersonFeaturedFaceSchema
{
    public static async Task EnsureAsync(
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS person_featured_faces (
                person_id TEXT NOT NULL PRIMARY KEY,
                face_occurrence_id TEXT NOT NULL,
                changed_at_utc TEXT NOT NULL,
                FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE CASCADE,
                FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_person_featured_faces_face
                ON person_featured_faces (face_occurrence_id, person_id);

            CREATE TRIGGER IF NOT EXISTS trg_person_featured_faces_after_merge
            AFTER UPDATE OF merged_into_person_id ON people
            WHEN OLD.merged_into_person_id IS NULL
             AND NEW.merged_into_person_id IS NOT NULL
            BEGIN
                INSERT OR IGNORE INTO person_featured_faces (
                    person_id,
                    face_occurrence_id,
                    changed_at_utc)
                SELECT
                    NEW.merged_into_person_id,
                    source.face_occurrence_id,
                    source.changed_at_utc
                FROM person_featured_faces AS source
                WHERE source.person_id = NEW.id
                  AND NOT EXISTS (
                      SELECT 1
                      FROM person_featured_faces AS target
                      WHERE target.person_id = NEW.merged_into_person_id)
                  AND EXISTS (
                      SELECT 1
                      FROM review_actions AS latest
                      WHERE latest.id = (
                          SELECT candidate.id
                          FROM review_actions AS candidate
                          WHERE candidate.face_occurrence_id = source.face_occurrence_id
                            AND candidate.action_kind IN ('assign', 'unknown', 'reject')
                            AND candidate.reversed_at_utc IS NULL
                          ORDER BY candidate.id DESC
                          LIMIT 1)
                        AND latest.action_kind = 'assign'
                        AND latest.person_id = NEW.merged_into_person_id);

                DELETE FROM person_featured_faces
                WHERE person_id = NEW.id;
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
