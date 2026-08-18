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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
