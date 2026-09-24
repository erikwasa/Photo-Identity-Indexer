using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPersonFamilySchemaBootstrapTests
{
    [Fact]
    public async Task Catalogue_initialization_creates_family_schema_before_repository_use()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_person_family_schema_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };

            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            PostgresInitializationResult first = await database.TryInitializeAsync();
            Assert.Null(first.Error);
            Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion, first.Health.SchemaVersion);

            // Repeated initialization must remain safe with the shared idempotent family schema.
            PostgresInitializationResult second = await database.TryInitializeAsync();
            Assert.Null(second.Error);
            Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion, second.Health.SchemaVersion);

            await using NpgsqlConnection verification = new(testBuilder.ConnectionString);
            await verification.OpenAsync();
            await using NpgsqlCommand read = verification.CreateCommand();
            read.CommandText = """
                SELECT
                    to_regclass('public.person_birth_metadata') IS NOT NULL,
                    to_regclass('public.person_relationships') IS NOT NULL,
                    EXISTS (
                        SELECT 1
                        FROM pg_trigger
                        WHERE tgname = 'trg_person_family_metadata_after_merge'
                          AND NOT tgisinternal);
                """;

            await using NpgsqlDataReader reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.True(reader.GetBoolean(1));
            Assert.True(reader.GetBoolean(2));
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
