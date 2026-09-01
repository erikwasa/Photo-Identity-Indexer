using Npgsql;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresCatalogueDatabaseTests
{
    [Fact]
    public async Task TryInitializeAsync_ReportsUnavailable_ForUnreachableServer()
    {
        await using PostgresCatalogueDatabase database = new(
            "Host=127.0.0.1;Port=1;Database=photoidentity;Username=test;Password=test;Pooling=false;Timeout=1");

        PostgresInitializationResult result = await database.TryInitializeAsync();

        Assert.Equal("unavailable", result.Health.Status);
        Assert.True(result.Health.Configured);
        Assert.Null(result.Health.SchemaVersion);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InitializeAsync_IsVersionedAndIdempotent_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_test_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);

        NpgsqlConnectionStringBuilder adminBuilder =
            new(adminConnectionString)
            {
                Pooling = false,
            };

        await using NpgsqlConnection adminConnection =
            new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();

        await using (NpgsqlCommand createDatabase =
                     adminConnection.CreateCommand())
        {
            createDatabase.CommandText =
                $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder =
                new(adminConnectionString)
                {
                    Database = databaseName,
                    Pooling = false,
                };

            await using PostgresCatalogueDatabase database =
                new(testBuilder.ConnectionString);

            PostgresInitializationResult first =
                await database.TryInitializeAsync();
            PostgresInitializationResult second =
                await database.TryInitializeAsync();

            Assert.Null(first.Error);
            Assert.Equal("ready", first.Health.Status);
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                first.Health.SchemaVersion);

            Assert.Null(second.Error);
            Assert.Equal("ready", second.Health.Status);
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                second.Health.SchemaVersion);

            await using NpgsqlConnection verificationConnection =
                new(testBuilder.ConnectionString);
            await verificationConnection.OpenAsync();

            await using NpgsqlCommand readMigration =
                verificationConnection.CreateCommand();
            readMigration.CommandText =
                """
                SELECT COUNT(*)
                FROM photo_identity_schema_migrations
                WHERE version = @version;
                """;
            readMigration.Parameters.AddWithValue(
                "version",
                PostgresCatalogueDatabase.CurrentSchemaVersion);

            object? count = await readMigration.ExecuteScalarAsync();
            Assert.Equal(1L, Convert.ToInt64(count));
        }
        finally
        {
            await using (NpgsqlCommand terminateConnections =
                         adminConnection.CreateCommand())
            {
                terminateConnections.CommandText =
                    """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid();
                    """;
                terminateConnections.Parameters.AddWithValue(
                    "database_name",
                    databaseName);
                await terminateConnections.ExecuteNonQueryAsync();
            }

            await using NpgsqlCommand dropDatabase =
                adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName};";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        $""{identifier.Replace(""", """", StringComparison.Ordinal)}"";
}
