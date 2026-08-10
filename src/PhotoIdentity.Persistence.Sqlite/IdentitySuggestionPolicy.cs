using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public static class IdentitySuggestionConfidenceGroups
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public sealed record IdentitySuggestionPolicy(
    int Version,
    bool AutoAssignEnabled,
    double HighScoreThreshold,
    double HighMarginThreshold,
    double MediumScoreThreshold,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc)
{
    public const double DefaultHighScoreThreshold = 0.70;
    public const double DefaultHighMarginThreshold = 0.10;
    public const double DefaultMediumScoreThreshold = 0.50;

    public void Validate()
    {
        if (Version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Version), "Policy version must be positive.");
        }

        ValidateScore(HighScoreThreshold, nameof(HighScoreThreshold));
        ValidateScore(MediumScoreThreshold, nameof(MediumScoreThreshold));
        if (MediumScoreThreshold > HighScoreThreshold)
        {
            throw new ArgumentException(
                "The Medium score threshold cannot be greater than the High score threshold.");
        }

        if (!double.IsFinite(HighMarginThreshold)
            || HighMarginThreshold < 0
            || HighMarginThreshold > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HighMarginThreshold),
                "The High rank-1/rank-2 margin threshold must be between 0 and 2.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(UpdatedBy);
    }

    public string Classify(double score, double? scoreMargin)
    {
        if (!double.IsFinite(score))
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Suggestion score must be finite.");
        }

        if (scoreMargin is double margin && (!double.IsFinite(margin) || margin < 0 || margin > 2))
        {
            throw new ArgumentOutOfRangeException(nameof(scoreMargin), "Suggestion score margin must be between 0 and 2.");
        }

        if (score >= HighScoreThreshold
            && scoreMargin is double highMargin
            && highMargin >= HighMarginThreshold)
        {
            return IdentitySuggestionConfidenceGroups.High;
        }

        return score >= MediumScoreThreshold
            ? IdentitySuggestionConfidenceGroups.Medium
            : IdentitySuggestionConfidenceGroups.Low;
    }

    private static void ValidateScore(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Score threshold must be between 0 and 1.");
        }
    }
}

/// <summary>
/// Stores one versioned confidence policy per exact embedding-model revision. Thresholds
/// are deliberately not shared across revisions because score calibration is model scoped.
/// Policy versions are monotonic within each model revision so automatic assignments can
/// retain the exact decision evidence after future threshold changes.
/// </summary>
public sealed class SqliteIdentitySuggestionPolicyRepository
{
    public const string DefaultActor = "system:default";

    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqliteIdentitySuggestionPolicyRepository(
        SqliteCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IdentitySuggestionPolicy> GetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureDefaultAsync(connection, transaction, modelId, modelHash, cancellationToken);
        IdentitySuggestionPolicy policy = await ReadAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        transaction.Commit();
        return policy;
    }

    public async Task<IdentitySuggestionPolicy> UpdateAsync(
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
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureDefaultAsync(connection, transaction, modelId, modelHash, cancellationToken);
        IdentitySuggestionPolicy current = await ReadAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);

        IdentitySuggestionPolicy proposed = new(
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
            transaction.Commit();
            return current;
        }

        IdentitySuggestionPolicy updated = proposed with { Version = checked(current.Version + 1) };
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE identity_suggestion_policies
            SET policy_version = $policy_version,
                auto_assign_enabled = $auto_assign_enabled,
                high_score_threshold = $high_score_threshold,
                high_margin_threshold = $high_margin_threshold,
                medium_score_threshold = $medium_score_threshold,
                updated_by = $updated_by,
                updated_at_utc = $updated_at_utc
            WHERE model_id = $model_id
              AND model_hash = $model_hash;
            """;
        AddModelParameters(command, modelId, modelHash);
        AddPolicyParameters(command, updated);
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        return updated;
    }

    private async Task EnsureDefaultAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO identity_suggestion_policies (
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
                $model_id,
                $model_hash,
                1,
                0,
                $high_score_threshold,
                $high_margin_threshold,
                $medium_score_threshold,
                $updated_by,
                $updated_at_utc);
            """;
        AddModelParameters(command, modelId, modelHash);
        command.Parameters.AddWithValue("$high_score_threshold", IdentitySuggestionPolicy.DefaultHighScoreThreshold);
        command.Parameters.AddWithValue("$high_margin_threshold", IdentitySuggestionPolicy.DefaultHighMarginThreshold);
        command.Parameters.AddWithValue("$medium_score_threshold", IdentitySuggestionPolicy.DefaultMediumScoreThreshold);
        command.Parameters.AddWithValue("$updated_by", DefaultActor);
        command.Parameters.AddWithValue("$updated_at_utc", Format(_timeProvider.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IdentitySuggestionPolicy> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                policy_version,
                auto_assign_enabled,
                high_score_threshold,
                high_margin_threshold,
                medium_score_threshold,
                updated_by,
                updated_at_utc
            FROM identity_suggestion_policies
            WHERE model_id = $model_id
              AND model_hash = $model_hash;
            """;
        AddModelParameters(command, modelId, modelHash);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The exact-model identity suggestion policy could not be initialized.");
        }

        IdentitySuggestionPolicy policy = new(
            reader.GetInt32(0),
            reader.GetInt32(1) != 0,
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetString(5),
            Parse(reader.GetString(6)));
        policy.Validate();
        return policy;
    }

    private static void AddModelParameters(
        SqliteCommand command,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
    }

    private static void AddPolicyParameters(SqliteCommand command, IdentitySuggestionPolicy policy)
    {
        command.Parameters.AddWithValue("$policy_version", policy.Version);
        command.Parameters.AddWithValue("$auto_assign_enabled", policy.AutoAssignEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$high_score_threshold", policy.HighScoreThreshold);
        command.Parameters.AddWithValue("$high_margin_threshold", policy.HighMarginThreshold);
        command.Parameters.AddWithValue("$medium_score_threshold", policy.MediumScoreThreshold);
        command.Parameters.AddWithValue("$updated_by", policy.UpdatedBy);
        command.Parameters.AddWithValue("$updated_at_utc", Format(policy.UpdatedAtUtc));
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
