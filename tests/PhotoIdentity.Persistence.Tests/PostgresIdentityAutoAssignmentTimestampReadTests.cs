using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresIdentityAutoAssignmentTimestampReadTests
{
    [Fact]
    public async Task ApplyAsync_DoesNotRejectActiveRunBecauseOfTimestampClrMapping_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_multi_auto_timestamp_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString) { Pooling = false };
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

            DateTimeOffset now = new(2026, 9, 16, 20, 0, 0, TimeSpan.Zero);
            ModelId modelId = new("timestamp-mapping-sface");
            Sha256Digest modelHash = new(new string('b', 64));

            PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository policies = new(
                database,
                new FixedTimeProvider(now));
            await policies.UpdateAsync(
                modelId,
                modelHash,
                enabled: true,
                actor: "maintainer");

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText =
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
                        @model_id,
                        @model_hash,
                        1,
                        'running',
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
                        'maintainer',
                        @requested_at_utc,
                        @requested_at_utc,
                        NULL,
                        @requested_at_utc,
                        NULL);
                    """;
                command.Parameters.AddWithValue("id", Guid.NewGuid());
                command.Parameters.AddWithValue("model_id", modelId.ToString());
                command.Parameters.AddWithValue("model_hash", modelHash.ToString());
                command.Parameters.AddWithValue("requested_at_utc", now.AddMinutes(1));
                await command.ExecuteNonQueryAsync();
            }

            ReviewIdentitySuggestionPolicy identityPolicy = new(
                Version: 1,
                AutoAssignEnabled: true,
                HighScoreThreshold: 0.70,
                HighMarginThreshold: 0.10,
                MediumScoreThreshold: 0.50,
                UpdatedBy: "maintainer",
                UpdatedAtUtc: now);
            PostgresIdentityAutoAssignmentService service = new(
                database,
                new FixedTimeProvider(now.AddMinutes(2)));

            ReviewIdentityAutoAssignmentSummary summary = await service.ApplyAsync(
                modelId,
                modelHash,
                identityPolicy);

            Assert.Equal(0, summary.CandidateCount);
            Assert.Equal(0, summary.AssignedCount);
            Assert.Equal(0, summary.SkippedCount);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        const char quote = (char)34;
        string quoteString = quote.ToString();
        return quoteString + identifier.Replace(quoteString, quoteString + quoteString, StringComparison.Ordinal) + quoteString;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
