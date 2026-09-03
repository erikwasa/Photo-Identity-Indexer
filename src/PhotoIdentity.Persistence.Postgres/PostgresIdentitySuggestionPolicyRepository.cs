using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresIdentitySuggestionPolicyRepository :
    IIdentitySuggestionPolicyRepository
{
    public const string DefaultActor = "system:default";

    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresIdentitySuggestionPolicyRepository(
        PostgresCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReviewIdentitySuggestionPolicy> GetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureDefaultAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        ReviewIdentitySuggestionPolicy policy = await ReadAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            forUpdate: false,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return policy;
    }

    public async Task<ReviewIdentitySuggestionPolicy> UpdateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        bool autoAssignEnabled,
        double highScoreThreshold,
        double highMarginThreshold,
        double mediumScoreThreshold,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureDefaultAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);

        ReviewIdentitySuggestionPolicy current = await ReadAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            forUpdate: true,
            cancellationToken);

        ReviewIdentitySuggestionPolicy proposed = new(
            current.Version,
            autoAssignEnabled,
            highScoreThreshold,
            highMarginThreshold,
            mediumScoreThreshold,
            normalizedActor,
            _timeProvider.GetUtcNow().ToUniversalTime());
        proposed.Validate();

        if (current.AutoAssignEnabled == proposed.AutoAssignEnabled
            && current.HighScoreThreshold.Equals(proposed.HighScoreThreshold)
            && current.HighMarginThreshold.Equals(proposed.HighMarginThreshold)
            && current.MediumScoreThreshold.Equals(proposed.MediumScoreThreshold))
        {
            await transaction.CommitAsync(cancellationToken);
            return current;
        }

        ReviewIdentitySuggestionPolicy updated = proposed with
        {
            Version = checked(current.Version + 1),
        };

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE identity_suggestion_policies
            SET policy_version = @policy_version,
                auto_assign_enabled = @auto_assign_enabled,
                high_score_threshold = @high_score_threshold,
                high_margin_threshold = @high_margin_threshold,
                medium_score_threshold = @medium_score_threshold,
                updated_by = @updated_by,
                updated_at_utc = @updated_at_utc
            WHERE model_id = @model_id
              AND model_hash = @model_hash;
            """;
        AddModelParameters(command, modelId, modelHash);
        AddPolicyParameters(command, updated);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The exact-model identity suggestion policy could not be updated.");
        }

        ReviewIdentitySuggestionPolicy persisted = await ReadAsync(
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
            INSERT INTO identity_suggestion_policies (
                model_id,
                model_hash,
                policy_version,
                auto_assign_enabled,
                high_score_threshold,
                high_margin_threshold,
                medium_score_threshold,
                updated_by,
                updated_at_utc)
            VALUES (
                @model_id,
                @model_hash,
                1,
                FALSE,
                @high_score_threshold,
                @high_margin_threshold,
                @medium_score_threshold,
                @updated_by,
                @updated_at_utc)
            ON CONFLICT (model_id, model_hash) DO NOTHING;
            """;
        AddModelParameters(command, modelId, modelHash);
        command.Parameters.AddWithValue(
            "high_score_threshold",
            ReviewIdentitySuggestionPolicy.DefaultHighScoreThreshold);
        command.Parameters.AddWithValue(
            "high_margin_threshold",
            ReviewIdentitySuggestionPolicy.DefaultHighMarginThreshold);
        command.Parameters.AddWithValue(
            "medium_score_threshold",
            ReviewIdentitySuggestionPolicy.DefaultMediumScoreThreshold);
        command.Parameters.AddWithValue("updated_by", DefaultActor);
        command.Parameters.AddWithValue(
            "updated_at_utc",
            _timeProvider.GetUtcNow().ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ReviewIdentitySuggestionPolicy> ReadAsync(
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
                auto_assign_enabled,
                high_score_threshold,
                high_margin_threshold,
                medium_score_threshold,
                updated_by,
                updated_at_utc
            FROM identity_suggestion_policies
            WHERE model_id = @model_id
              AND model_hash = @model_hash
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        AddModelParameters(command, modelId, modelHash);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The exact-model identity suggestion policy could not be initialized.");
        }

        ReviewIdentitySuggestionPolicy policy = new(
            reader.GetInt32(0),
            reader.GetBoolean(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6));
        policy.Validate();
        return policy;
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
        ReviewIdentitySuggestionPolicy policy)
    {
        command.Parameters.AddWithValue("policy_version", policy.Version);
        command.Parameters.AddWithValue("auto_assign_enabled", policy.AutoAssignEnabled);
        command.Parameters.AddWithValue("high_score_threshold", policy.HighScoreThreshold);
        command.Parameters.AddWithValue("high_margin_threshold", policy.HighMarginThreshold);
        command.Parameters.AddWithValue("medium_score_threshold", policy.MediumScoreThreshold);
        command.Parameters.AddWithValue("updated_by", policy.UpdatedBy);
        command.Parameters.AddWithValue("updated_at_utc", policy.UpdatedAtUtc.ToUniversalTime());
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
