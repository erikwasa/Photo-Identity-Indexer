using PhotoIdentity.Testing.Postgres;

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

/// <summary>
/// Historical fixture name retained only for the legacy Places migration regression. The work is
/// performed against an isolated PostgreSQL catalogue by PostgresPhotoPlaceSchemaCompatibility.
/// </summary>
public static class SqlitePhotoPlaceSchema
{
    public static Task EnsureAndMigrateAsync(
        PostgresTestCatalogueDatabase database,
        CancellationToken cancellationToken = default) =>
        PostgresPhotoPlaceSchemaCompatibility.EnsureAndMigrateAsync(database, cancellationToken);
}
