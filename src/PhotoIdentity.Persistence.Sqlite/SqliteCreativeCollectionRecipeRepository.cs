using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteCreativeCollectionRecipeRepository : ICreativeCollectionRecipeRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaReady;

    public SqliteCreativeCollectionRecipeRepository(
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CreativeCollectionRecipe>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        return await ListCoreAsync(anchorCollectionId: null, cancellationToken);
    }

    public async Task<IReadOnlyList<CreativeCollectionRecipe>> ListForAnchorAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        return await ListCoreAsync(anchorCollectionId, cancellationToken);
    }

    public async Task<CreativeCollectionRecipe?> GetAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        return await GetCoreAsync(id, cancellationToken);
    }

    public async Task<CreativeCollectionRecipe?> GetAsync(
        SmartCollectionId anchorCollectionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        IReadOnlyList<CreativeCollectionRecipe> recipes =
            await ListCoreAsync(anchorCollectionId, cancellationToken);
        return recipes.FirstOrDefault();
    }

    public async Task<CreativeCollectionRecipe> CreateAsync(
        SmartCollectionId anchorCollectionId,
        string name,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateSupported();
        string displayName = CreativeCollectionName.Parse(name).DisplayValue;
        CreativeCollectionId id = CreativeCollectionId.New();
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO creative_collection_recipes (
                id, display_name, anchor_collection_id, target_count,
                moment_gap_minutes, moment_policy_version, context_policy_version,
                selection_policy_version, ordering_policy_version, novelty_enabled,
                created_at_utc, updated_at_utc)
            VALUES (
                $id, $display_name, $anchor_collection_id, $target_count,
                $moment_gap_minutes, $moment_policy_version, $context_policy_version,
                $selection_policy_version, $ordering_policy_version, $novelty_enabled,
                $created_at_utc, $updated_at_utc);
            """;
        AddRecipeParameters(command, id, anchorCollectionId, displayName, settings, now, includeCreatedAt: true);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetCoreAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection was not persisted.");
    }

    public async Task<CreativeCollectionRecipe> UpdateAsync(
        CreativeCollectionId id,
        string name,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(settings);
        settings.ValidateSupported();
        string displayName = CreativeCollectionName.Parse(name).DisplayValue;
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE creative_collection_recipes
            SET display_name = $display_name,
                target_count = $target_count,
                moment_gap_minutes = $moment_gap_minutes,
                moment_policy_version = $moment_policy_version,
                context_policy_version = $context_policy_version,
                selection_policy_version = $selection_policy_version,
                ordering_policy_version = $ordering_policy_version,
                novelty_enabled = $novelty_enabled,
                updated_at_utc = $updated_at_utc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        command.Parameters.AddWithValue("$display_name", displayName);
        AddSettingsParameters(command, settings, now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new KeyNotFoundException($"Creative Collection '{id}' was not found.");
        }

        return await GetCoreAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection update could not be read back.");
    }

    public async Task<bool> DeleteAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM creative_collection_recipes WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<CreativeCollectionRecipe> UpsertAsync(
        SmartCollectionId anchorCollectionId,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        CreativeCollectionRecipe? existing =
            (await ListCoreAsync(anchorCollectionId, cancellationToken)).FirstOrDefault();
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
        await EnsureNamedSchemaAsync(cancellationToken);
        CreativeCollectionRecipe? existing =
            (await ListCoreAsync(anchorCollectionId, cancellationToken)).FirstOrDefault();
        return existing is not null && await DeleteAsync(existing.Id, cancellationToken);
    }

    private async Task EnsureNamedSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            bool hasId = false;
            using (SqliteCommand columns = connection.CreateCommand())
            {
                columns.CommandText = "PRAGMA table_info(creative_collection_recipes);";
                await using SqliteDataReader reader = await columns.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (string.Equals(reader.GetString(1), "id", StringComparison.OrdinalIgnoreCase))
                    {
                        hasId = true;
                        break;
                    }
                }
            }

            if (!hasId)
            {
                using (SqliteCommand disable = connection.CreateCommand())
                {
                    disable.CommandText = "PRAGMA foreign_keys = OFF;";
                    await disable.ExecuteNonQueryAsync(cancellationToken);
                }

                try
                {
                    using SqliteTransaction transaction = connection.BeginTransaction();
                    using SqliteCommand migration = connection.CreateCommand();
                    migration.Transaction = transaction;
                    migration.CommandText = """
                        CREATE TABLE creative_collection_recipes_named (
                            id TEXT NOT NULL PRIMARY KEY,
                            display_name TEXT NOT NULL CHECK (length(trim(display_name)) BETWEEN 1 AND 120),
                            anchor_collection_id TEXT NOT NULL,
                            target_count INTEGER NOT NULL CHECK (target_count BETWEEN 1 AND 1000),
                            moment_gap_minutes INTEGER NOT NULL CHECK (moment_gap_minutes BETWEEN 1 AND 720),
                            moment_policy_version TEXT NOT NULL CHECK (length(moment_policy_version) > 0),
                            context_policy_version TEXT NOT NULL CHECK (length(context_policy_version) > 0),
                            selection_policy_version TEXT NOT NULL CHECK (length(selection_policy_version) > 0),
                            ordering_policy_version TEXT NOT NULL CHECK (length(ordering_policy_version) > 0),
                            novelty_enabled INTEGER NOT NULL DEFAULT 0 CHECK (novelty_enabled IN (0, 1)),
                            created_at_utc TEXT NOT NULL,
                            updated_at_utc TEXT NOT NULL,
                            FOREIGN KEY (anchor_collection_id) REFERENCES smart_collections (id) ON DELETE CASCADE
                        );

                        INSERT INTO creative_collection_recipes_named (
                            id, display_name, anchor_collection_id, target_count,
                            moment_gap_minutes, moment_policy_version, context_policy_version,
                            selection_policy_version, ordering_policy_version, novelty_enabled,
                            created_at_utc, updated_at_utc)
                        SELECT
                            recipe.anchor_collection_id,
                            substr(collection.display_name, 1, 111) || ' Creative',
                            recipe.anchor_collection_id,
                            recipe.target_count,
                            recipe.moment_gap_minutes,
                            recipe.moment_policy_version,
                            recipe.context_policy_version,
                            recipe.selection_policy_version,
                            recipe.ordering_policy_version,
                            recipe.novelty_enabled,
                            recipe.created_at_utc,
                            recipe.updated_at_utc
                        FROM creative_collection_recipes AS recipe
                        INNER JOIN smart_collections AS collection
                            ON collection.id = recipe.anchor_collection_id;

                        DROP TABLE creative_collection_recipes;
                        ALTER TABLE creative_collection_recipes_named RENAME TO creative_collection_recipes;
                        CREATE INDEX ix_creative_collection_recipes_anchor
                            ON creative_collection_recipes (anchor_collection_id, created_at_utc, id);
                        """;
                    await migration.ExecuteNonQueryAsync(cancellationToken);
                    transaction.Commit();
                }
                finally
                {
                    using SqliteCommand enable = connection.CreateCommand();
                    enable.CommandText = "PRAGMA foreign_keys = ON;";
                    await enable.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private async Task<CreativeCollectionRecipe?> GetCoreAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectColumns}
            FROM creative_collection_recipes
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecipe(reader) : null;
    }

    private async Task<IReadOnlyList<CreativeCollectionRecipe>> ListCoreAsync(
        SmartCollectionId? anchorCollectionId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = anchorCollectionId is null
            ? $"""
                {SelectColumns}
                FROM creative_collection_recipes
                ORDER BY lower(display_name), created_at_utc, id;
                """
            : $"""
                {SelectColumns}
                FROM creative_collection_recipes
                WHERE anchor_collection_id = $anchor_collection_id
                ORDER BY created_at_utc, id;
                """;
        if (anchorCollectionId is SmartCollectionId anchor)
        {
            command.Parameters.AddWithValue("$anchor_collection_id", anchor.Value.ToString("D"));
        }

        List<CreativeCollectionRecipe> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
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
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT display_name FROM smart_collections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", anchorCollectionId.Value.ToString("D"));
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
        SqliteCommand command,
        CreativeCollectionId id,
        SmartCollectionId anchorCollectionId,
        string displayName,
        CreativeCollectionRecipeSettings settings,
        DateTimeOffset now,
        bool includeCreatedAt)
    {
        command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$anchor_collection_id", anchorCollectionId.Value.ToString("D"));
        AddSettingsParameters(command, settings, now);
        if (includeCreatedAt)
        {
            command.Parameters.AddWithValue("$created_at_utc", now.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private static void AddSettingsParameters(
        SqliteCommand command,
        CreativeCollectionRecipeSettings settings,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("$target_count", settings.TargetCount);
        command.Parameters.AddWithValue("$moment_gap_minutes", settings.MomentGapMinutes);
        command.Parameters.AddWithValue("$moment_policy_version", settings.MomentPolicyVersion);
        command.Parameters.AddWithValue("$context_policy_version", settings.ContextPolicyVersion);
        command.Parameters.AddWithValue("$selection_policy_version", settings.SelectionPolicyVersion);
        command.Parameters.AddWithValue("$ordering_policy_version", settings.OrderingPolicyVersion);
        command.Parameters.AddWithValue("$novelty_enabled", settings.NoveltyEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at_utc", now.ToString("O", CultureInfo.InvariantCulture));
    }

    private static CreativeCollectionRecipe ReadRecipe(SqliteDataReader reader)
    {
        CreativeCollectionRecipe recipe = new(
            CreativeCollectionId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            SmartCollectionId.From(Guid.Parse(reader.GetString(2))),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetInt32(9) != 0,
            DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

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
