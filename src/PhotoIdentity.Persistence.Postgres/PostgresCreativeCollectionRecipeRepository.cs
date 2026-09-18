using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresCreativeCollectionRecipeRepository : ICreativeCollectionRecipeRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresCreativeCollectionRecipeRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<CreativeCollectionRecipe?> GetAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT anchor_collection_id,
                   target_count,
                   moment_gap_minutes,
                   moment_policy_version,
                   context_policy_version,
                   selection_policy_version,
                   ordering_policy_version,
                   novelty_enabled,
                   created_at_utc,
                   updated_at_utc
            FROM creative_collection_recipes
            WHERE anchor_collection_id = @anchor_collection_id;
            """;
        command.Parameters.AddWithValue("anchor_collection_id", NpgsqlDbType.Uuid, anchorCollectionId.Value);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRecipe(reader)
            : null;
    }

    public async Task<CreativeCollectionRecipe> UpsertAsync(
        SmartCollectionId anchorCollectionId,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateSupported();
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO creative_collection_recipes (
                anchor_collection_id,
                target_count,
                moment_gap_minutes,
                moment_policy_version,
                context_policy_version,
                selection_policy_version,
                ordering_policy_version,
                novelty_enabled,
                created_at_utc,
                updated_at_utc)
            VALUES (
                @anchor_collection_id,
                @target_count,
                @moment_gap_minutes,
                @moment_policy_version,
                @context_policy_version,
                @selection_policy_version,
                @ordering_policy_version,
                @novelty_enabled,
                @created_at_utc,
                @updated_at_utc)
            ON CONFLICT(anchor_collection_id) DO UPDATE SET
                target_count = EXCLUDED.target_count,
                moment_gap_minutes = EXCLUDED.moment_gap_minutes,
                moment_policy_version = EXCLUDED.moment_policy_version,
                context_policy_version = EXCLUDED.context_policy_version,
                selection_policy_version = EXCLUDED.selection_policy_version,
                ordering_policy_version = EXCLUDED.ordering_policy_version,
                novelty_enabled = EXCLUDED.novelty_enabled,
                updated_at_utc = EXCLUDED.updated_at_utc;
            """;
        command.Parameters.AddWithValue("anchor_collection_id", NpgsqlDbType.Uuid, anchorCollectionId.Value);
        command.Parameters.AddWithValue("target_count", NpgsqlDbType.Integer, settings.TargetCount);
        command.Parameters.AddWithValue("moment_gap_minutes", NpgsqlDbType.Integer, settings.MomentGapMinutes);
        command.Parameters.AddWithValue("moment_policy_version", settings.MomentPolicyVersion);
        command.Parameters.AddWithValue("context_policy_version", settings.ContextPolicyVersion);
        command.Parameters.AddWithValue("selection_policy_version", settings.SelectionPolicyVersion);
        command.Parameters.AddWithValue("ordering_policy_version", settings.OrderingPolicyVersion);
        command.Parameters.AddWithValue("novelty_enabled", NpgsqlDbType.Boolean, settings.NoveltyEnabled);
        command.Parameters.AddWithValue("created_at_utc", now);
        command.Parameters.AddWithValue("updated_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(anchorCollectionId, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection recipe was not persisted.");
    }

    public async Task<bool> DeleteAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM creative_collection_recipes WHERE anchor_collection_id = @anchor_collection_id;";
        command.Parameters.AddWithValue("anchor_collection_id", NpgsqlDbType.Uuid, anchorCollectionId.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static CreativeCollectionRecipe ReadRecipe(NpgsqlDataReader reader)
    {
        CreativeCollectionRecipe recipe = new(
            SmartCollectionId.From(reader.GetGuid(0)),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetBoolean(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9));

        new CreativeCollectionRecipeSettings(
            recipe.TargetCount,
            recipe.MomentGapMinutes,
            recipe.MomentPolicyVersion,
            recipe.ContextPolicyVersion,
            recipe.SelectionPolicyVersion,
            recipe.OrderingPolicyVersion,
            recipe.NoveltyEnabled).ValidateSupported();
        return recipe;
    }
}
