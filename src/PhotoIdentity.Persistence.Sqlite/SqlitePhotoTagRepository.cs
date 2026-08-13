using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueManualPhotoTag(
    string NormalizedName,
    string DisplayName,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

/// <summary>
/// Stores maintainer-owned photo tags independently from future model-produced tag evidence.
/// Manual state is derived from append-only add/remove actions for one immutable asset revision.
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
        string tagName,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoTagName tag = PhotoTagName.Parse(tagName);
        string normalizedActor = NormalizeActor(actor);
        string now = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        using (SqliteCommand insertTag = connection.CreateCommand())
        {
            insertTag.Transaction = transaction;
            insertTag.CommandText = """
                INSERT OR IGNORE INTO photo_tags (
                    normalized_name, display_name, created_by, created_at_utc)
                VALUES ($normalized_name, $display_name, $actor, $created_at_utc);
                """;
            insertTag.Parameters.AddWithValue("$normalized_name", tag.NormalizedName);
            insertTag.Parameters.AddWithValue("$display_name", tag.DisplayName);
            insertTag.Parameters.AddWithValue("$actor", normalizedActor);
            insertTag.Parameters.AddWithValue("$created_at_utc", now);
            await insertTag.ExecuteNonQueryAsync(cancellationToken);
        }

        long tagId = await ReadTagIdAsync(connection, transaction, tag.NormalizedName, cancellationToken)
            ?? throw new InvalidOperationException("The canonical photo tag could not be read after insertion.");
        string? latestAction = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            tagId,
            cancellationToken);

        if (!string.Equals(latestAction, "add", StringComparison.Ordinal))
        {
            await InsertActionAsync(
                connection,
                transaction,
                revisionId,
                tagId,
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
        string tagName,
        string actor,
        CancellationToken cancellationToken = default)
    {
        PhotoTagName tag = PhotoTagName.Parse(tagName);
        string normalizedActor = NormalizeActor(actor);
        string now = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);

        long? tagId = await ReadTagIdAsync(connection, transaction, tag.NormalizedName, cancellationToken);
        if (tagId is not null)
        {
            string? latestAction = await ReadLatestActionAsync(
                connection,
                transaction,
                revisionId,
                tagId.Value,
                cancellationToken);
            if (string.Equals(latestAction, "add", StringComparison.Ordinal))
            {
                await InsertActionAsync(
                    connection,
                    transaction,
                    revisionId,
                    tagId.Value,
                    "remove",
                    normalizedActor,
                    now,
                    cancellationToken);
            }
        }

        transaction.Commit();
        return await ReadEffectiveTagsAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<IReadOnlyList<CatalogueManualPhotoTag>> ReadEffectiveTagsAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
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
            tags.Add(new CatalogueManualPhotoTag(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }

        return tags;
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

    private static async Task<long?> ReadTagIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalizedName,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM photo_tags WHERE normalized_name = $normalized_name;";
        command.Parameters.AddWithValue("$normalized_name", normalizedName);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
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
}
