using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresCreativeCollectionRecipeRepository : ICreativeCollectionRecipeRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaReady;

    public PostgresCreativeCollectionRecipeRepository(
        PostgresCatalogueDatabase database,
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

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO creative_collection_recipes (
                id, display_name, anchor_collection_id, target_count,
                moment_gap_minutes, moment_policy_version, context_policy_version,
                selection_policy_version, ordering_policy_version, novelty_enabled,
                created_at_utc, updated_at_utc)
            VALUES (
                @id, @display_name, @anchor_collection_id, @target_count,
                @moment_gap_minutes, @moment_policy_version, @context_policy_version,
                @selection_policy_version, @ordering_policy_version, @novelty_enabled,
                @created_at_utc, @updated_at_utc);
            """;
        AddRecipeParameters(command, id, anchorCollectionId, displayName, settings, now, includeCreatedAt: true);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetCoreAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection was not persisted.");
    }

    public async Task<CreativeCollectionRecipe> CreateSearchAsync(
        string name,
        CreativeCollectionSearchAnchor searchAnchor,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(searchAnchor);
        ArgumentNullException.ThrowIfNull(settings);
        searchAnchor.ValidateSupported();
        settings.ValidateSupported();
        string displayName = CreativeCollectionName.Parse(name).DisplayValue;
        CreativeCollectionId id = CreativeCollectionId.New();
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO creative_collection_recipes (
                id, display_name, anchor_collection_id, target_count,
                moment_gap_minutes, moment_policy_version, context_policy_version,
                selection_policy_version, ordering_policy_version, novelty_enabled,
                anchor_kind, search_query, search_mode, search_scope_collection_id,
                search_anchor_limit, anchor_policy_version,
                created_at_utc, updated_at_utc)
            VALUES (
                @id, @display_name, NULL, @target_count,
                @moment_gap_minutes, @moment_policy_version, @context_policy_version,
                @selection_policy_version, @ordering_policy_version, @novelty_enabled,
                'search', @search_query, @search_mode, @search_scope_collection_id,
                @search_anchor_limit, @anchor_policy_version,
                @created_at_utc, @updated_at_utc);
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        command.Parameters.AddWithValue("display_name", displayName);
        AddSearchAnchorParameters(command, searchAnchor);
        AddSettingsParameters(command, settings, now);
        command.Parameters.AddWithValue("created_at_utc", now);
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

        return await GetCoreAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection update could not be read back.");
    }

    public async Task<CreativeCollectionRecipe> UpdateSearchAsync(
        CreativeCollectionId id,
        string name,
        CreativeCollectionSearchAnchor searchAnchor,
        CreativeCollectionRecipeSettings settings,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(searchAnchor);
        ArgumentNullException.ThrowIfNull(settings);
        searchAnchor.ValidateSupported();
        settings.ValidateSupported();
        string displayName = CreativeCollectionName.Parse(name).DisplayValue;
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE creative_collection_recipes
            SET display_name = @display_name,
                search_query = @search_query,
                search_mode = @search_mode,
                search_scope_collection_id = @search_scope_collection_id,
                search_anchor_limit = @search_anchor_limit,
                anchor_policy_version = @anchor_policy_version,
                target_count = @target_count,
                moment_gap_minutes = @moment_gap_minutes,
                moment_policy_version = @moment_policy_version,
                context_policy_version = @context_policy_version,
                selection_policy_version = @selection_policy_version,
                ordering_policy_version = @ordering_policy_version,
                novelty_enabled = @novelty_enabled,
                updated_at_utc = @updated_at_utc
            WHERE id = @id
              AND anchor_kind = 'search';
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        command.Parameters.AddWithValue("display_name", displayName);
        AddSearchAnchorParameters(command, searchAnchor);
        AddSettingsParameters(command, settings, now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new KeyNotFoundException($"Search-anchored Creative Collection '{id}' was not found.");
        }

        return await GetCoreAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Creative Collection update could not be read back.");
    }

    public async Task<bool> DeleteAsync(
        CreativeCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await EnsureNamedSchemaAsync(cancellationToken);
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

            await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                LOCK TABLE creative_collection_recipes IN ACCESS EXCLUSIVE MODE;

                DO $migration$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'creative_collection_recipes'
                          AND column_name = 'id') THEN
                        ALTER TABLE creative_collection_recipes ADD COLUMN id uuid NULL;
                        ALTER TABLE creative_collection_recipes ADD COLUMN display_name text NULL;

                        UPDATE creative_collection_recipes AS recipe
                        SET id = recipe.anchor_collection_id,
                            display_name = left(collection.display_name, 111) || ' Creative'
                        FROM smart_collections AS collection
                        WHERE collection.id = recipe.anchor_collection_id;

                        UPDATE creative_collection_recipes
                        SET display_name = 'Creative Collection'
                        WHERE display_name IS NULL;

                        ALTER TABLE creative_collection_recipes
                            ALTER COLUMN id SET NOT NULL,
                            ALTER COLUMN display_name SET NOT NULL;

                        ALTER TABLE creative_collection_recipes
                            DROP CONSTRAINT creative_collection_recipes_pkey;
                        ALTER TABLE creative_collection_recipes
                            ADD CONSTRAINT creative_collection_recipes_pkey PRIMARY KEY (id);
                        ALTER TABLE creative_collection_recipes
                            ADD CONSTRAINT ck_creative_collection_recipes_display_name
                            CHECK (char_length(btrim(display_name)) BETWEEN 1 AND 120);

                        CREATE INDEX ix_creative_collection_recipes_anchor
                            ON creative_collection_recipes (anchor_collection_id, created_at_utc, id);
                    END IF;
                END
                $migration$;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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

    private static void AddSearchAnchorParameters(
        NpgsqlCommand command,
        CreativeCollectionSearchAnchor searchAnchor)
    {
        command.Parameters.AddWithValue("search_query", searchAnchor.Query);
        command.Parameters.AddWithValue("search_mode", searchAnchor.Mode);
        command.Parameters.AddWithValue(
            "search_scope_collection_id",
            NpgsqlDbType.Uuid,
            searchAnchor.ScopeCollectionId is SmartCollectionId scope
                ? scope.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "search_anchor_limit",
            NpgsqlDbType.Integer,
            searchAnchor.Limit);
        command.Parameters.AddWithValue("anchor_policy_version", searchAnchor.PolicyVersion);
    }

    private static CreativeCollectionRecipe ReadRecipe(NpgsqlDataReader reader)
    {
        SmartCollectionId? anchorCollectionId = reader.IsDBNull(2)
            ? null
            : SmartCollectionId.From(reader.GetGuid(2));
        string anchorKind = reader.GetString(12);
        string anchorPolicyVersion = reader.GetString(17);
        CreativeCollectionSearchAnchor? searchAnchor = null;
        if (string.Equals(anchorKind, CreativeCollectionAnchorKinds.Search, StringComparison.Ordinal))
        {
            searchAnchor = new CreativeCollectionSearchAnchor(
                reader.GetString(13),
                reader.GetString(14),
                reader.IsDBNull(15)
                    ? null
                    : SmartCollectionId.From(reader.GetGuid(15)),
                reader.GetInt32(16),
                anchorPolicyVersion);
        }
        else if (!string.Equals(anchorKind, CreativeCollectionAnchorKinds.SmartCollection, StringComparison.Ordinal) ||
                 !string.Equals(
                     anchorPolicyVersion,
                     CreativeCollectionAnchorPolicies.SmartCollectionV1,
                     StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Creative Collection anchor kind '{anchorKind}' or policy '{anchorPolicyVersion}' is not supported.");
        }

        CreativeCollectionRecipe recipe = new(
            CreativeCollectionId.From(reader.GetGuid(0)),
            reader.GetString(1),
            anchorCollectionId,
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetBoolean(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            searchAnchor);

        _ = CreativeCollectionName.Parse(recipe.Name);
        recipe.ValidateAnchorSupported();
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
               updated_at_utc,
               anchor_kind,
               search_query,
               search_mode,
               search_scope_collection_id,
               search_anchor_limit,
               anchor_policy_version
        """;
}
