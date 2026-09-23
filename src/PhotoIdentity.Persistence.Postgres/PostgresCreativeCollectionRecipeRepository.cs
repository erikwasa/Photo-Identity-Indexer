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

    public Task<IReadOnlyList<CreativeCollectionRecipe>> ListAsync(
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(anchorCollectionId: null, cancellationToken);

    public Task<IReadOnlyList<CreativeCollectionRecipe>> ListForAnchorAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default) =>
        ListCoreAsync(anchorCollectionId, cancellationToken);

    public async Task<CreativeCollectionRecipe?> GetAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectColumns}
            FROM creative_collection_recipes
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecipe(reader) : null;
    }

    public async Task<CreativeCollectionRecipe?> GetAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CreativeCollectionRecipe> recipes =
            await ListForAnchorAsync(anchorCollectionId, cancellationToken);
        return recipes.FirstOrDefault();
    }

    public async Task<CreativeCollectionRecipe> CreateAsync(
        SmartCollectionId anchorCollectionId,
        string name,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateSupported();
        string displayName = CreativeCollectionName.Parse(name).DisplayValue;
        CreativeCollectionId id = CreativeCollectionId.New();
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO creative_collection_recipes (
                id,
                display_name,
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
                @id,
                @display_name,
                @anchor_collection_id,
                @target_count,
                @moment_gap_minutes,
                @moment_policy_version,
                @context_policy_version,
                @selection_policy_version,
                @ordering_policy_version,
                @novelty_enabled,
                @created_at_utc,
                @updated_at_utc);
            """;
        AddRecipeParameters(command, id, anchorCollectionId, displayName, settings, now, includeCreatedAt: true);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection was not persisted.");
    }

    public async Task<CreativeCollectionRecipe> UpdateAsync(
        CreativeCollectionId id,
        string name,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateSupported();
        string displayName = CreativeCollectionName.Parse(name).DisplayValue;
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE creative_collection_recipes
            SET display_name = @display_name,
                target_count = @target_count,
                moment_gap_minutes = @moment_gap_minutes,
                moment_policy_version = @moment_policy_version,
                context_policy_version = @context_policy_version,
                selection_policy_version = @selection_policy_version,
                ordering_policy_version = @ordering_policy_version,
                novelty_enabled = @novelty_enabled,
                updated_at_utc = @updated_at_utc
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        command.Parameters.AddWithValue("display_name", displayName);
        AddSettingsParameters(command, settings, now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new KeyNotFoundException($"Creative Collection '{id}' was not found.");
        }

        return await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection update could not be read back.");
    }

    public async Task<bool> DeleteAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM creative_collection_recipes WHERE id = @id;";
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<CreativeCollectionRecipe> UpsertAsync(
        SmartCollectionId anchorCollectionId,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        CreativeCollectionRecipe? existing = await GetAsync(anchorCollectionId, cancellationToken);
        if (existing is not null)
        {
            return await UpdateAsync(existing.Id, existing.Name, settings, cancellationToken);
        }

        return await CreateAsync(
            anchorCollectionId,
            await FallbackNameAsync(anchorCollectionId, cancellationToken),
            settings,
            cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default)
    {
        CreativeCollectionRecipe? existing = await GetAsync(anchorCollectionId, cancellationToken);
        return existing is not null && await DeleteAsync(existing.Id, cancellationToken);
    }

    private async Task<IReadOnlyList<CreativeCollectionRecipe>> ListCoreAsync(
        SmartCollectionId? anchorCollectionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = anchorCollectionId is null
            ? $"""
                {SelectColumns}
                FROM creative_collection_recipes
                ORDER BY lower(display_name), created_at_utc, id;
                """
            : $"""
                {SelectColumns}
                FROM creative_collection_recipes
                WHERE anchor_collection_id = @anchor_collection_id
                ORDER BY created_at_utc, id;
                """;
        if (anchorCollectionId is SmartCollectionId anchor)
        {
            command.Parameters.AddWithValue("anchor_collection_id", NpgsqlDbType.Uuid, anchor.Value);
        }

        List<CreativeCollectionRecipe> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadRecipe(reader));
        }

        return result;
    }

    private async Task<string> FallbackNameAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT display_name FROM smart_collections WHERE id = @id;";
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, anchorCollectionId.Value);
        string anchorName = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "Smart Collection";
        return FallbackName(anchorName);
    }

    private static string FallbackName(string anchorName)
    {
        const string suffix = " Creative";
        string trimmed = anchorName.Trim();
        int prefixLength = Math.Min(trimmed.Length, CreativeCollectionName.MaximumLength - suffix.Length);
        return $"{trimmed[..prefixLength]}{suffix}";
    }

    private static void AddRecipeParameters(
        NpgsqlCommand command,
        CreativeCollectionId id,
        SmartCollectionId anchorCollectionId,
        string displayName,
        CreativeCollectionRecipeSettings settings,
        DateTimeOffset now,
        bool includeCreatedAt)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("anchor_collection_id", NpgsqlDbType.Uuid, anchorCollectionId.Value);
        AddSettingsParameters(command, settings, now);
        if (includeCreatedAt)
        {
            command.Parameters.AddWithValue("created_at_utc", now);
        }
    }

    private static void AddSettingsParameters(
        NpgsqlCommand command,
        CreativeCollectionRecipeSettings settings,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("target_count", NpgsqlDbType.Integer, settings.TargetCount);
        command.Parameters.AddWithValue("moment_gap_minutes", NpgsqlDbType.Integer, settings.MomentGapMinutes);
        command.Parameters.AddWithValue("moment_policy_version", settings.MomentPolicyVersion);
        command.Parameters.AddWithValue("context_policy_version", settings.ContextPolicyVersion);
        command.Parameters.AddWithValue("selection_policy_version", settings.SelectionPolicyVersion);
        command.Parameters.AddWithValue("ordering_policy_version", settings.OrderingPolicyVersion);
        command.Parameters.AddWithValue("novelty_enabled", NpgsqlDbType.Boolean, settings.NoveltyEnabled);
        command.Parameters.AddWithValue("updated_at_utc", now);
    }

    private static CreativeCollectionRecipe ReadRecipe(NpgsqlDataReader reader)
    {
        CreativeCollectionRecipe recipe = new(
            CreativeCollectionId.From(reader.GetGuid(0)),
            reader.GetString(1),
            SmartCollectionId.From(reader.GetGuid(2)),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetBoolean(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetFieldValue<DateTimeOffset>(11));

        _ = CreativeCollectionName.Parse(recipe.Name);
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

    private const string SelectColumns = """
        SELECT id,
               display_name,
               anchor_collection_id,
               target_count,
               moment_gap_minutes,
               moment_policy_version,
               context_policy_version,
               selection_policy_version,
               ordering_policy_version,
               novelty_enabled,
               created_at_utc,
               updated_at_utc
        """;
}
