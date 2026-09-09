using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoTagRepository : IPhotoTagRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoTagRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CataloguePhotoTagDefinition>> GetCanonicalTagsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            ORDER BY display_name, normalized_name;
            """;
        IReadOnlyList<TagRow> rows = await ReadRowsAsync(command, cancellationToken);
        Dictionary<string, TagRow> byValue = rows.ToDictionary(row => row.NormalizedValue, StringComparer.Ordinal);
        return rows.Select(row => ToDefinition(row, byValue)).ToArray();
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoTag>> GetManualTagsAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, null, revisionId, cancellationToken);
        return await ReadEffectiveTagsAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoTag>> AddManualTagAsync(
        AssetRevisionId revisionId,
        string tagValue,
        string actor,
        CancellationToken cancellationToken = default) =>
        await ApplyActionAsync(revisionId, tagValue, actor, "add", cancellationToken);

    public async Task<IReadOnlyList<CatalogueManualPhotoTag>> RemoveManualTagAsync(
        AssetRevisionId revisionId,
        string tagValue,
        string actor,
        CancellationToken cancellationToken = default) =>
        await ApplyActionAsync(revisionId, tagValue, actor, "remove", cancellationToken);

    private async Task<IReadOnlyList<CatalogueManualPhotoTag>> ApplyActionAsync(
        AssetRevisionId revisionId,
        string tagValue,
        string actor,
        string action,
        CancellationToken cancellationToken)
    {
        PhotoTagPath path = PhotoTagPath.Parse(tagValue);
        string normalizedActor = NormalizeActor(actor);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        TagRow? tag = action == "add"
            ? await EnsureCanonicalPathAsync(connection, transaction, path, normalizedActor, cancellationToken)
            : await ReadTagAsync(connection, transaction, path.NormalizedValue, cancellationToken);
        if (tag is not null)
        {
            string? latestAction = await ReadLatestActionAsync(
                connection,
                transaction,
                revisionId,
                tag.Id,
                cancellationToken);
            if ((action == "add" && latestAction != "add") ||
                (action == "remove" && latestAction == "add"))
            {
                await using NpgsqlCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photo_tag_actions (
                        asset_revision_id, tag_id, action_kind, actor, created_at_utc)
                    VALUES (@revision_id, @tag_id, @action_kind, @actor, @created_at_utc);
                    """;
                insert.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
                insert.Parameters.AddWithValue("tag_id", tag.Id);
                insert.Parameters.AddWithValue("action_kind", action);
                insert.Parameters.AddWithValue("actor", normalizedActor);
                insert.Parameters.AddWithValue("created_at_utc", _timeProvider.GetUtcNow().ToUniversalTime());
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return await ReadEffectiveTagsAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<TagRow?> EnsureCanonicalPathAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PhotoTagPath path,
        string actor,
        CancellationToken cancellationToken)
    {
        string? normalizedParent = null;
        string? displayParent = null;
        TagRow? current = null;
        foreach (PhotoTagName segment in path.Segments)
        {
            string normalizedValue = normalizedParent is null
                ? segment.NormalizedName
                : $"{normalizedParent}{PhotoTagPath.Separator}{segment.NormalizedName}";
            current = await ReadTagAsync(connection, transaction, normalizedValue, cancellationToken);
            if (current is null)
            {
                string displayValue = displayParent is null
                    ? segment.DisplayName
                    : $"{displayParent}{PhotoTagPath.Separator}{segment.DisplayName}";
                await using NpgsqlCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photo_tags (
                        normalized_name, display_name, created_by, created_at_utc)
                    VALUES (@normalized_name, @display_name, @created_by, @created_at_utc)
                    RETURNING id;
                    """;
                insert.Parameters.AddWithValue("normalized_name", normalizedValue);
                insert.Parameters.AddWithValue("display_name", displayValue);
                insert.Parameters.AddWithValue("created_by", actor);
                insert.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
                long id = (long)(await insert.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("The PostgreSQL photo tag could not be inserted."));
                current = new TagRow(id, normalizedValue, displayValue);
            }

            normalizedParent = current.NormalizedValue;
            displayParent = current.DisplayValue;
        }

        return current;
    }

    private static async Task<IReadOnlyList<CatalogueManualPhotoTag>> ReadEffectiveTagsAsync(
        NpgsqlConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_actions AS (
                SELECT
                    action.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY tag_id
                        ORDER BY id DESC) AS row_number
                FROM photo_tag_actions AS action
                WHERE asset_revision_id = @revision_id)
            SELECT
                tag.id,
                tag.normalized_name,
                tag.display_name,
                action.actor,
                action.created_at_utc
            FROM latest_actions AS action
            INNER JOIN photo_tags AS tag ON tag.id = action.tag_id
            WHERE action.row_number = 1
              AND action.action_kind = 'add'
            ORDER BY tag.display_name, tag.normalized_name;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<(long Id, string NormalizedValue, string DisplayValue, string Actor, DateTimeOffset CreatedAt)> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        List<CatalogueManualPhotoTag> tags = [];
        foreach ((long id, string normalizedValue, string displayValue, string actor, DateTimeOffset createdAt) in rows)
        {
            TagRow? parent = await FindParentAsync(connection, normalizedValue, cancellationToken);
            tags.Add(new CatalogueManualPhotoTag(
                id,
                normalizedValue,
                displayValue,
                LeafName(displayValue),
                parent?.Id,
                parent?.DisplayValue,
                null,
                actor,
                createdAt));
        }

        return tags;
    }

    private static async Task<TagRow?> FindParentAsync(
        NpgsqlConnection connection,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        int separator = normalizedValue.LastIndexOf(PhotoTagPath.Separator);
        return separator < 0
            ? null
            : await ReadTagAsync(connection, null, normalizedValue[..separator], cancellationToken);
    }

    private static async Task<IReadOnlyList<TagRow>> ReadRowsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<TagRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new TagRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    private static async Task<TagRow?> ReadTagAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            WHERE normalized_name = @normalized_name;
            """;
        command.Parameters.AddWithValue("normalized_name", normalizedValue);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new TagRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<string?> ReadLatestActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        long tagId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind
            FROM photo_tag_actions
            WHERE asset_revision_id = @revision_id AND tag_id = @tag_id
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("tag_id", tagId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task EnsureRevisionExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM asset_revisions WHERE id = @revision_id);";
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        if (!((bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            throw new KeyNotFoundException($"Asset revision '{revisionId}' was not found.");
        }
    }

    private static CataloguePhotoTagDefinition ToDefinition(
        TagRow row,
        IReadOnlyDictionary<string, TagRow> byValue)
    {
        TagRow? parent = Parent(row.NormalizedValue, byValue);
        return new CataloguePhotoTagDefinition(
            row.Id,
            row.NormalizedValue,
            row.DisplayValue,
            LeafName(row.DisplayValue),
            parent?.Id,
            parent?.DisplayValue,
            null);
    }

    private static TagRow? Parent(
        string normalizedValue,
        IReadOnlyDictionary<string, TagRow> byValue)
    {
        int separator = normalizedValue.LastIndexOf(PhotoTagPath.Separator);
        return separator < 0 || !byValue.TryGetValue(normalizedValue[..separator], out TagRow? parent)
            ? null
            : parent;
    }

    private static string LeafName(string value)
    {
        int separator = value.LastIndexOf(PhotoTagPath.Separator);
        return separator < 0 ? value : value[(separator + 1)..];
    }

    private static string NormalizeActor(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        string value = actor.Trim();
        return value.Length <= 120
            ? value
            : throw new ArgumentException("Photo-tag actor cannot exceed 120 characters.", nameof(actor));
    }

    private sealed record TagRow(long Id, string NormalizedValue, string DisplayValue);
}
