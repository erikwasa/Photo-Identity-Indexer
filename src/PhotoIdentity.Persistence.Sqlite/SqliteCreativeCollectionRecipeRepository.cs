using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteCreativeCollectionRecipeRepository : ICreativeCollectionRecipeRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqliteCreativeCollectionRecipeRepository(
        SqliteCatalogueDatabase database,
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
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
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
            WHERE anchor_collection_id = $anchor_collection_id;
            """;
        command.Parameters.AddWithValue("$anchor_collection_id", anchorCollectionId.Value.ToString("D"));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
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

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
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
                $anchor_collection_id,
                $target_count,
                $moment_gap_minutes,
                $moment_policy_version,
                $context_policy_version,
                $selection_policy_version,
                $ordering_policy_version,
                $novelty_enabled,
                $created_at_utc,
                $updated_at_utc)
            ON CONFLICT(anchor_collection_id) DO UPDATE SET
                target_count = excluded.target_count,
                moment_gap_minutes = excluded.moment_gap_minutes,
                moment_policy_version = excluded.moment_policy_version,
                context_policy_version = excluded.context_policy_version,
                selection_policy_version = excluded.selection_policy_version,
                ordering_policy_version = excluded.ordering_policy_version,
                novelty_enabled = excluded.novelty_enabled,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$anchor_collection_id", anchorCollectionId.Value.ToString("D"));
        command.Parameters.AddWithValue("$target_count", settings.TargetCount);
        command.Parameters.AddWithValue("$moment_gap_minutes", settings.MomentGapMinutes);
        command.Parameters.AddWithValue("$moment_policy_version", settings.MomentPolicyVersion);
        command.Parameters.AddWithValue("$context_policy_version", settings.ContextPolicyVersion);
        command.Parameters.AddWithValue("$selection_policy_version", settings.SelectionPolicyVersion);
        command.Parameters.AddWithValue("$ordering_policy_version", settings.OrderingPolicyVersion);
        command.Parameters.AddWithValue("$novelty_enabled", settings.NoveltyEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$created_at_utc", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated_at_utc", now.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(anchorCollectionId, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection recipe was not persisted.");
    }

    public async Task<bool> DeleteAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM creative_collection_recipes WHERE anchor_collection_id = $anchor_collection_id;";
        command.Parameters.AddWithValue("$anchor_collection_id", anchorCollectionId.Value.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static CreativeCollectionRecipe ReadRecipe(SqliteDataReader reader)
    {
        CreativeCollectionRecipe recipe = new(
            SmartCollectionId.From(Guid.Parse(reader.GetString(0))),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7) != 0,
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

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
