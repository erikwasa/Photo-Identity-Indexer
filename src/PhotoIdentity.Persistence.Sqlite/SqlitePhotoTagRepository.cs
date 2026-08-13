using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueManualPhotoTag(
    long TagId,
    string NormalizedValue,
    string Value,
    string Name,
    long? ParentTagId,
    string? ParentValue,
    string? Color,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record CataloguePhotoTagDefinition(
    long TagId,
    string NormalizedValue,
    string Value,
    string Name,
    long? ParentTagId,
    string? ParentValue,
    string? Color);

/// <summary>
/// Stores maintainer-owned photo tags independently from future model-produced tag evidence.
/// Manual state is derived from append-only add/remove actions for one immutable asset revision.
/// Canonical tag rows form an Immich-compatible slash-separated hierarchy while SQLite remains
/// Photo Identity's source of truth.
/// </summary>
public sealed class SqlitePhotoTagRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqlitePhotoTagRepository(SqliteCatalogueDatabase database, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CataloguePhotoTagDefinition>> GetCanonicalTagsAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        IReadOnlyList<CanonicalTagRow> rows = await ReadCanonicalTagRowsAsync(connection, cancellationToken);
        Dictionary<string, CanonicalTagRow> byNormalizedValue = rows.ToDictionary(
            row => row.NormalizedValue,
            StringComparer.Ordinal);
        return rows.Select(row => ToDefinition(row, byNormalizedValue)).ToArray();
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoTag>> GetManualTagsAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction: null, revisionId, cancellationToken);
        return await ReadEffectiveTagsAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoTag>> AddManualTagAsync(
        AssetRevisionId revisionId,
        string tagValue,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoTagPath requestedPath = PhotoTagPath.Parse(tagValue);
        string normalizedActor = NormalizeActor(actor);
        string now = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        CanonicalTagRow finalTag = await EnsureCanonicalPathAsync(
            connection,
            transaction,
            requestedPath,
            normalizedActor,
            now,
            cancellationToken);
        string? latestAction = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            finalTag.Id,
            cancellationToken);

        if (!string.Equals(latestAction, "add", StringComparison.Ordinal))
        {
            await InsertActionAsync(
                connection,
                transaction,
                revisionId,
                finalTag.Id,
                "add",
                normalizedActor,
                now,
                cancellationToken);
        }

        transaction.Commit();
        return await ReadEffectiveTagsAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoTag>> RemoveManualTagAsync(
        AssetRevisionId revisionId,
        string tagValue,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoTagPath path = PhotoTagPath.Parse(tagValue);
        string normalizedActor = NormalizeActor(actor);
        string now = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        CanonicalTagRow? tag = await ReadTagRowAsync(
            connection,
            transaction,
            path.NormalizedValue,
            cancellationToken);
        if (tag is not null)
        {
            string? latestAction = await ReadLatestActionAsync(
                connection,
                transaction,
                revisionId,
                tag.Id,
                cancellationToken);
            if (string.Equals(latestAction, "add", StringComparison.Ordinal))
            {
                await InsertActionAsync(
                    connection,
                    transaction,
                    revisionId,
                    tag.Id,
                    "remove",
                    normalizedActor,
                    now,
                    cancellationToken);
            }
        }

        transaction.Commit();
        return await ReadEffectiveTagsAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<CanonicalTagRow> EnsureCanonicalPathAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PhotoTagPath requestedPath,
        string actor,
        string createdAtUtc,
        CancellationToken cancellationToken)
    {
        CanonicalTagRow? existingFinal = await ReadTagRowAsync(
            connection,
            transaction,
            requestedPath.NormalizedValue,
            cancellationToken);
        PhotoTagPath path = existingFinal is null
            ? requestedPath
            : PhotoTagPath.Parse(existingFinal.DisplayValue);

        string? normalizedParent = null;
        string? displayParent = null;
        CanonicalTagRow? current = null;

        foreach (PhotoTagName segment in path.Segments)
        {
            string normalizedValue = normalizedParent is null
                ? segment.NormalizedName
                : $"{normalizedParent}{PhotoTagPath.Separator}{segment.NormalizedName}";
            current = await ReadTagRowAsync(
                connection,
                transaction,
                normalizedValue,
                cancellationToken);

            if (current is null)
            {
                string displayValue = displayParent is null
                    ? segment.DisplayName
                    : $"{displayParent}{PhotoTagPath.Separator}{segment.DisplayName}";
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO photo_tags (
                        normalized_name, display_name, created_by, created_at_utc)
                    VALUES ($normalized_name, $display_name, $actor, $created_at_utc);
                    """;
                insert.Parameters.AddWithValue("$normalized_name", normalizedValue);
                insert.Parameters.AddWithValue("$display_name", displayValue);
                insert.Parameters.AddWithValue("$actor", actor);
                insert.Parameters.AddWithValue("$created_at_utc", createdAtUtc);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                current = await ReadTagRowAsync(
                    connection,
                    transaction,
                    normalizedValue,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The canonical photo tag could not be read after insertion.");
            }

            normalizedParent = current.NormalizedValue;
            displayParent = current.DisplayValue;
        }

        return current ?? throw new InvalidOperationException("The canonical photo tag path was empty.");
    }

    private static async Task<IReadOnlyList<CatalogueManualPhotoTag>> ReadEffectiveTagsAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CanonicalTagRow> canonicalRows = await ReadCanonicalTagRowsAsync(
            connection,
            cancellationToken);
        Dictionary<string, CanonicalTagRow> byNormalizedValue = canonicalRows.ToDictionary(
            row => row.NormalizedValue,
            StringComparer.Ordinal);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_actions AS (
                SELECT
                    photo_tag_actions.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY tag_id
                        ORDER BY id DESC) AS row_number
                FROM photo_tag_actions
                WHERE asset_revision_id = $revision_id
            )
            SELECT
                photo_tags.id,
                photo_tags.normalized_name,
                photo_tags.display_name,
                latest_actions.actor,
                latest_actions.created_at_utc
            FROM latest_actions
            INNER JOIN photo_tags ON photo_tags.id = latest_actions.tag_id
            WHERE latest_actions.row_number = 1
              AND latest_actions.action_kind = 'add'
            ORDER BY photo_tags.display_name COLLATE NOCASE, photo_tags.normalized_name;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());

        List<CatalogueManualPhotoTag> tags = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            string normalizedValue = reader.GetString(1);
            string displayValue = reader.GetString(2);
            CanonicalTagRow? parent = Parent(normalizedValue, byNormalizedValue);
            tags.Add(new CatalogueManualPhotoTag(
                id,
                normalizedValue,
                displayValue,
                LeafName(displayValue),
                parent?.Id,
                parent?.DisplayValue,
                Color: null,
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        }

        return tags;
    }

    private static async Task<IReadOnlyList<CanonicalTagRow>> ReadCanonicalTagRowsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            ORDER BY display_name COLLATE NOCASE, normalized_name;
            """;
        List<CanonicalTagRow> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CanonicalTagRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private static CataloguePhotoTagDefinition ToDefinition(
        CanonicalTagRow row,
        IReadOnlyDictionary<string, CanonicalTagRow> byNormalizedValue)
    {
        CanonicalTagRow? parent = Parent(row.NormalizedValue, byNormalizedValue);
        return new CataloguePhotoTagDefinition(
            row.Id,
            row.NormalizedValue,
            row.DisplayValue,
            LeafName(row.DisplayValue),
            parent?.Id,
            parent?.DisplayValue,
            Color: null);
    }

    private static CanonicalTagRow? Parent(
        string normalizedValue,
        IReadOnlyDictionary<string, CanonicalTagRow> byNormalizedValue)
    {
        int separator = normalizedValue.LastIndexOf(PhotoTagPath.Separator);
        if (separator < 0)
        {
            return null;
        }

        return byNormalizedValue.TryGetValue(normalizedValue[..separator], out CanonicalTagRow? parent)
            ? parent
            : null;
    }

    private static string LeafName(string displayValue)
    {
        int separator = displayValue.LastIndexOf(PhotoTagPath.Separator);
        return separator < 0 ? displayValue : displayValue[(separator + 1)..];
    }

    private static async Task EnsureRevisionExistsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM asset_revisions WHERE id = $revision_id;";
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        long count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (count == 0)
        {
            throw new KeyNotFoundException($"Asset revision '{revisionId}' was not found.");
        }
    }

    private static async Task<CanonicalTagRow?> ReadTagRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, normalized_name, display_name
            FROM photo_tags
            WHERE normalized_name = $normalized_name;
            """;
        command.Parameters.AddWithValue("$normalized_name", normalizedValue);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CanonicalTagRow(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static async Task<string?> ReadLatestActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        long tagId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind
            FROM photo_tag_actions
            WHERE asset_revision_id = $revision_id AND tag_id = $tag_id
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$tag_id", tagId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task InsertActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        long tagId,
        string actionKind,
        string actor,
        string createdAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO photo_tag_actions (
                asset_revision_id, tag_id, action_kind, actor, created_at_utc)
            VALUES ($revision_id, $tag_id, $action_kind, $actor, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$tag_id", tagId);
        command.Parameters.AddWithValue("$action_kind", actionKind);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeActor(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        string normalized = actor.Trim();
        if (normalized.Length > 120)
        {
            throw new ArgumentException("Photo-tag actor cannot exceed 120 characters.", nameof(actor));
        }

        return normalized;
    }

    private sealed record CanonicalTagRow(long Id, string NormalizedValue, string DisplayValue);
}
