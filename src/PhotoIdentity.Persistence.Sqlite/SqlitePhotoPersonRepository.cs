using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueManualPhotoPerson(
    PersonId PersonId,
    string DisplayName,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

/// <summary>
/// Stores maintainer-owned person-presence statements for an immutable photo revision.
/// These actions are intentionally separate from face occurrences, crops, embeddings,
/// review actions and identity suggestions.
/// </summary>
public sealed class SqlitePhotoPersonRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqlitePhotoPersonRepository(SqliteCatalogueDatabase database, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoPerson>> GetManualPeopleAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPersonSchema.EnsureAsync(connection, transaction: null, cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction: null, revisionId, cancellationToken);
        return await ReadEffectivePeopleAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoPerson>> AddManualPersonAsync(
        AssetRevisionId revisionId,
        PersonId personId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = NormalizeActor(actor);
        string now = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await SqlitePhotoPersonSchema.EnsureAsync(connection, transaction, cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        string? latestAction = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            personId,
            cancellationToken);
        if (!string.Equals(latestAction, "add", StringComparison.Ordinal))
        {
            await InsertActionAsync(
                connection,
                transaction,
                revisionId,
                personId,
                "add",
                normalizedActor,
                now,
                cancellationToken);
        }

        transaction.Commit();
        return await ReadEffectivePeopleAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoPerson>> RemoveManualPersonAsync(
        AssetRevisionId revisionId,
        PersonId personId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = NormalizeActor(actor);
        string now = _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await SqlitePhotoPersonSchema.EnsureAsync(connection, transaction, cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        string? latestAction = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            personId,
            cancellationToken);
        if (string.Equals(latestAction, "add", StringComparison.Ordinal))
        {
            await InsertActionAsync(
                connection,
                transaction,
                revisionId,
                personId,
                "remove",
                normalizedActor,
                now,
                cancellationToken);
        }

        transaction.Commit();
        return await ReadEffectivePeopleAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<IReadOnlyList<CatalogueManualPhotoPerson>> ReadEffectivePeopleAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_actions AS (
                SELECT
                    photo_person_actions.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY person_id
                        ORDER BY id DESC) AS row_number
                FROM photo_person_actions
                WHERE asset_revision_id = $revision_id
            )
            SELECT
                people.id,
                people.display_name,
                latest_actions.actor,
                latest_actions.created_at_utc
            FROM latest_actions
            INNER JOIN people ON people.id = latest_actions.person_id
            WHERE latest_actions.row_number = 1
              AND latest_actions.action_kind = 'add'
              AND people.merged_into_person_id IS NULL
              AND people.display_name IS NOT NULL
              AND TRIM(people.display_name) <> ''
            ORDER BY people.display_name COLLATE NOCASE, people.id;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());

        List<CatalogueManualPhotoPerson> people = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            people.Add(new CatalogueManualPhotoPerson(
                PersonId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }

        return people;
    }

    private static async Task<string?> ReadLatestActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind
            FROM photo_person_actions
            WHERE asset_revision_id = $revision_id
              AND person_id = $person_id
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task InsertActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        PersonId personId,
        string actionKind,
        string actor,
        string createdAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO photo_person_actions (
                asset_revision_id, person_id, action_kind, actor, created_at_utc)
            VALUES ($revision_id, $person_id, $action_kind, $actor, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$action_kind", actionKind);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task RequireActivePersonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT display_name, merged_into_person_id
            FROM people
            WHERE id = $person_id;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException($"Person '{personId}' was not found.");
        }

        if (!reader.IsDBNull(1))
        {
            throw new InvalidOperationException(
                $"Person '{personId}' has been merged and cannot receive a manual photo assignment.");
        }

        if (reader.IsDBNull(0) || string.IsNullOrWhiteSpace(reader.GetString(0)))
        {
            throw new InvalidOperationException($"Person '{personId}' does not have an active display name.");
        }
    }

    private static string NormalizeActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor is required.", nameof(actor));
        }

        string normalized = actor.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException("Actor must be 200 characters or fewer.", nameof(actor));
        }

        return normalized;
    }
}

internal static class SqlitePhotoPersonSchema
{
    public static async Task EnsureAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_person_actions (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                asset_revision_id TEXT NOT NULL,
                person_id TEXT NOT NULL,
                action_kind TEXT NOT NULL CHECK (action_kind IN ('add', 'remove')),
                actor TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS ix_photo_person_actions_revision_history
                ON photo_person_actions (asset_revision_id, person_id, id DESC);
            CREATE INDEX IF NOT EXISTS ix_photo_person_actions_person_history
                ON photo_person_actions (person_id, asset_revision_id, id DESC);

            CREATE TRIGGER IF NOT EXISTS trg_photo_person_actions_transfer_merge
            AFTER UPDATE OF merged_into_person_id ON people
            WHEN OLD.merged_into_person_id IS NULL
             AND NEW.merged_into_person_id IS NOT NULL
            BEGIN
                INSERT INTO photo_person_actions (
                    asset_revision_id,
                    person_id,
                    action_kind,
                    actor,
                    created_at_utc)
                SELECT
                    source_action.asset_revision_id,
                    NEW.merged_into_person_id,
                    'add',
                    'person-merge',
                    strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                FROM photo_person_actions AS source_action
                WHERE source_action.person_id = NEW.id
                  AND source_action.action_kind = 'add'
                  AND source_action.id = (
                      SELECT MAX(source_latest.id)
                      FROM photo_person_actions AS source_latest
                      WHERE source_latest.asset_revision_id = source_action.asset_revision_id
                        AND source_latest.person_id = NEW.id)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM photo_person_actions AS target_action
                      WHERE target_action.asset_revision_id = source_action.asset_revision_id
                        AND target_action.person_id = NEW.merged_into_person_id
                        AND target_action.action_kind = 'add'
                        AND target_action.id = (
                            SELECT MAX(target_latest.id)
                            FROM photo_person_actions AS target_latest
                            WHERE target_latest.asset_revision_id = source_action.asset_revision_id
                              AND target_latest.person_id = NEW.merged_into_person_id));
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
