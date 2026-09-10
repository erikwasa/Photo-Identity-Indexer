namespace PhotoIdentity_Integration_Tests;

public sealed class PostgresRuntimeApplicationTestsMigration
{
    [Fact]
    public async Task SQLite_catalogue_migration_is_included_in_live_postgres_runtime_acceptance()
    {
        await new CatalogueMigrationCommandTests()
            .Migrate_preserves_stable_history_repairs_sequences_and_is_repeatable_WhenLivePostgresIsConfigured();
    }
}
