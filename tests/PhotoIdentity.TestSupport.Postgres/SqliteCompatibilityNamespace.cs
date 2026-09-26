namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Temporary source-compatibility marker for integration tests being moved from the retired
/// SQLite fixture surface to PostgreSQL. No SQLite implementation is referenced by this assembly.
/// WI-0149 removes the remaining historical Sqlite-prefixed identifiers after the adapter project
/// itself is deleted.
/// </summary>
public static class PostgresBackedTestCompatibilityMarker
{
}
