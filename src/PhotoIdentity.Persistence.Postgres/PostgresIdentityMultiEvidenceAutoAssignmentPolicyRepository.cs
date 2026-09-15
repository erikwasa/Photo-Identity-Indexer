using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository :
    IIdentityMultiEvidenceAutoAssignmentPolicyRepository
{
    public const string DefaultActor = "system:default";

    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository(
        PostgresCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReviewIdentityMultiEvidenceAutoAssignmentConfiguration> GetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        await EnsureDefaultAsync(connection, transaction, modelId, modelHash, cancellationToken);
        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration result = await ReadAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            forUpdate: false,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ReviewIdentityMultiEvidenceAutoAssignmentConfiguration> UpdateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        bool enabled,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        await EnsureDefaultAsync(connection, transaction, modelId, modelHash, cancellationToken);

        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration current = await ReadAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            forUpdate: true,
            cancellationToken);
        string algorithmVersion = ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial.Version;
        if (current.Enabled == enabled
            && string.Equals(current.AlgorithmPolicyVersion, algorithmVersion, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return current;
        }

        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration updated = new(
            checked(current.Version + 1),
            enabled,
            algorithmVersion,
            normalizedActor,
            _timeProvider.GetUtcNow().ToUniversalTime());
        updated.Validate();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE identity_multi_evidence_auto_assignment_policies
            SET policy_version = @policy_version,
                enabled = @enabled,
                algorithm_policy_version = @algorithm_policy_version,
                updated_by = @updated_by,
                updated_at_utc = @updated_at_utc
            WHERE model_id = @model_id
              AND model_hash = @model_hash;
            """;
        AddModelParameters(command, modelId, modelHash);
        AddPolicyParameters(command, updated);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The multi-evidence automatic-assignment policy could not be updated.");
        }

        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration persisted = await ReadAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            forUpdate: false,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    private async Task EnsureDefaultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO identity_multi_evidence_auto_assignment_policies (
                model_id,
                model_hash,
                policy_version,
                enabled,
                algorithm_policy_version,
                updated_by,
                updated_at_utc)
            VALUES (
                @model_id,
                @model_hash,
                1,
                FALSE,
                @algorithm_policy_version,
                @updated_by,
                @updated_at_utc)
            ON CONFLICT (model_id, model_hash) DO NOTHING;
            """;
        AddModelParameters(command, modelId, modelHash);
        command.Parameters.AddWithValue(
            "algorithm_policy_version",
            ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial.Version);
        command.Parameters.AddWithValue("updated_by", DefaultActor);
        command.Parameters.AddWithValue("updated_at_utc", _timeProvider.GetUtcNow().ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ReviewIdentityMultiEvidenceAutoAssignmentConfiguration> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                policy_version,
                enabled,
                algorithm_policy_version,
                updated_by,
                updated_at_utc
            FROM identity_multi_evidence_auto_assignment_policies
            WHERE model_id = @model_id
              AND model_hash = @model_hash
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        AddModelParameters(command, modelId, modelHash);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The multi-evidence automatic-assignment policy could not be initialized.");
        }

        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration result = new(
            reader.GetInt32(0),
            reader.GetBoolean(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
        result.Validate();
        return result;
    }

    private static async Task EnsureSchemaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = MultiEvidencePolicySchema.Sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddModelParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
    }

    private static void AddPolicyParameters(
        NpgsqlCommand command,
        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration policy)
    {
        command.Parameters.AddWithValue("policy_version", policy.Version);
        command.Parameters.AddWithValue("enabled", policy.Enabled);
        command.Parameters.AddWithValue("algorithm_policy_version", policy.AlgorithmPolicyVersion);
        command.Parameters.AddWithValue("updated_by", policy.UpdatedBy);
        command.Parameters.AddWithValue("updated_at_utc", policy.UpdatedAtUtc.ToUniversalTime());
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    internal static class MultiEvidencePolicySchema
    {
        public const string Sql =
            """
            CREATE TABLE IF NOT EXISTS identity_multi_evidence_auto_assignment_policies (
                model_id text NOT NULL CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                policy_version integer NOT NULL CHECK (policy_version >= 1),
                enabled boolean NOT NULL,
                algorithm_policy_version text NOT NULL CHECK (btrim(algorithm_policy_version) <> ''),
                updated_by text NOT NULL CHECK (btrim(updated_by) <> ''),
                updated_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (model_id, model_hash)
            );
            """;
    }
}
