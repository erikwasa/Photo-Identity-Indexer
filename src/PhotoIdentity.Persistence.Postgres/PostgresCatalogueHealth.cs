namespace PhotoIdentity.Persistence.Postgres;

public sealed record PostgresCatalogueHealth(
    bool Configured,
    string Status,
    int? SchemaVersion)
{
    public static PostgresCatalogueHealth NotConfigured { get; } =
        new(false, "not_configured", null);

    public static PostgresCatalogueHealth Ready(int schemaVersion) =>
        new(true, "ready", schemaVersion);

    public static PostgresCatalogueHealth Unavailable { get; } =
        new(true, "unavailable", null);

    public static PostgresCatalogueHealth AuthenticationFailed { get; } =
        new(true, "authentication_failed", null);

    public static PostgresCatalogueHealth MigrationFailed { get; } =
        new(true, "migration_failed", null);
}

public sealed record PostgresInitializationResult(
    PostgresCatalogueHealth Health,
    Exception? Error);
