using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepositoryTests
{
    [Fact]
    public async Task GetAndUpdateAsync_PreserveExactModelDefaultOffPolicy_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_multi_policy_{Guid.NewGuid():N}";
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
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                initialization.Health.SchemaVersion);

            IIdentityMultiEvidenceAutoAssignmentPolicyRepository repository =
                new PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository(database);
            ModelId modelId = new("multi-policy-model");
            Sha256Digest modelHash = new(new string('a', 64));
            Sha256Digest otherHash = new(new string('b', 64));

            ReviewIdentityMultiEvidenceAutoAssignmentConfiguration initial =
                await repository.GetAsync(modelId, modelHash);
            Assert.Equal(1, initial.Version);
            Assert.False(initial.Enabled);
            Assert.Equal(
                ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial.Version,
                initial.AlgorithmPolicyVersion);
            Assert.Equal(
                PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository.DefaultActor,
                initial.UpdatedBy);

            ReviewIdentityMultiEvidenceAutoAssignmentConfiguration otherRevision =
                await repository.GetAsync(modelId, otherHash);
            Assert.Equal(1, otherRevision.Version);
            Assert.False(otherRevision.Enabled);

            ReviewIdentityMultiEvidenceAutoAssignmentConfiguration updated =
                await repository.UpdateAsync(
                    modelId,
                    modelHash,
                    enabled: true,
                    actor: "maintainer");
            Assert.Equal(2, updated.Version);
            Assert.True(updated.Enabled);
            Assert.Equal("maintainer", updated.UpdatedBy);

            ReviewIdentityMultiEvidenceAutoAssignmentConfiguration noOp =
                await repository.UpdateAsync(
                    modelId,
                    modelHash,
                    enabled: true,
                    actor: "different-actor");
            Assert.Equal(updated, noOp);

            IIdentityMultiEvidenceAutoAssignmentPolicyRepository recreated =
                new PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository(database);
            ReviewIdentityMultiEvidenceAutoAssignmentConfiguration durable =
                await recreated.GetAsync(modelId, modelHash);
            Assert.Equal(updated, durable);

            ReviewIdentityMultiEvidenceAutoAssignmentConfiguration stillIndependent =
                await recreated.GetAsync(modelId, otherHash);
            Assert.Equal(1, stillIndependent.Version);
            Assert.False(stillIndependent.Enabled);

            await Assert.ThrowsAsync<ArgumentException>(() => repository.UpdateAsync(
                modelId,
                modelHash,
                enabled: true,
                actor: "   "));
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
