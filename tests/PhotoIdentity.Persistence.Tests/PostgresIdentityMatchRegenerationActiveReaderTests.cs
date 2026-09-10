using Npgsql;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresIdentityMatchRegenerationActiveReaderTests
{
    [Fact]
    public async Task GetNextActiveAsync_ClosesReaderBeforeTransactionCommit_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_regeneration_active_{Guid.NewGuid():N}";
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
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            Assert.Null(initialization.Error);

            Guid runId = Guid.NewGuid();
            DateTimeOffset now = new(2026, 9, 10, 20, 0, 0, TimeSpan.Zero);
            await using (NpgsqlConnection seedConnection = new(testBuilder.ConnectionString))
            {
                await seedConnection.OpenAsync();
                await using NpgsqlCommand seed = seedConnection.CreateCommand();
                seed.CommandText =
                    """
                    INSERT INTO identity_match_regeneration_runs (
                        id,
                        model_id,
                        model_hash,
                        policy_version,
                        status,
                        evidence_review_action_id,
                        evidence_suggestion_review_action_id,
                        evidence_person_merge_action_id,
                        evidence_embedding_id,
                        target_count,
                        processed_target_count,
                        suggested_target_count,
                        suggestion_count,
                        automatically_assigned_count,
                        error_count,
                        requested_by,
                        requested_at_utc,
                        started_at_utc,
                        completed_at_utc,
                        updated_at_utc,
                        error)
                    VALUES (
                        @id,
                        'migration-review-model',
                        @model_hash,
                        1,
                        'pending',
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        'migration-review',
                        @now,
                        NULL,
                        NULL,
                        @now,
                        NULL);
                    """;
                seed.Parameters.AddWithValue("id", runId);
                seed.Parameters.AddWithValue("model_hash", new string('a', 64));
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            IIdentityMatchRegenerationRepository repository =
                new PostgresIdentityMatchRegenerationRepository(database);

            ReviewIdentityMatchRegenerationRun active =
                Assert.IsType<ReviewIdentityMatchRegenerationRun>(
                    await repository.GetNextActiveAsync());

            Assert.Equal(runId, active.Id);
            Assert.Equal(ReviewIdentityMatchRegenerationStatuses.Pending, active.Status);
        }
        finally
        {
            await using (NpgsqlCommand terminateConnections = adminConnection.CreateCommand())
            {
                terminateConnections.CommandText =
                    """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid();
                    """;
                terminateConnections.Parameters.AddWithValue("database_name", databaseName);
                await terminateConnections.ExecuteNonQueryAsync();
            }

            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName};";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        const char quote = (char)34;
        string quoteString = quote.ToString();
        string escaped = identifier.Replace(
            quoteString,
            quoteString + quoteString,
            StringComparison.Ordinal);
        return quoteString + escaped + quoteString;
    }
}
