using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoPersonRepository : IPhotoPersonRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoPersonRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, null, revisionId, cancellationToken);
        return await ReadEffectivePeopleAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueManualPhotoPerson>> AddManualPersonAsync(
        AssetRevisionId revisionId,
        PersonId personId,
        string actor,
        CancellationToken cancellationToken = default) =>
        await ApplyActionAsync(revisionId, personId, actor, "add", cancellationToken);

    public async Task<IReadOnlyList<CatalogueManualPhotoPerson>> RemoveManualPersonAsync(
        AssetRevisionId revisionId,
        PersonId personId,
        string actor,
        CancellationToken cancellationToken = default) =>
        await ApplyActionAsync(revisionId, personId, actor, "remove", cancellationToken);

    private async Task<IReadOnlyList<CatalogueManualPhotoPerson>> ApplyActionAsync(
        AssetRevisionId revisionId,
        PersonId personId,
        string actor,
        string action,
        CancellationToken cancellationToken)
    {
        string normalizedActor = NormalizeActor(actor);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        await EnsureActivePersonAsync(connection, transaction, personId, cancellationToken);

        string? latestAction = await ReadLatestActionAsync(
            connection,
            transaction,
            revisionId,
            personId,
            cancellationToken);
        if ((action == "add" && latestAction != "add") ||
            (action == "remove" && latestAction == "add"))
        {
            await using NpgsqlCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO photo_person_actions (
                    asset_revision_id, person_id, action_kind, actor, created_at_utc)
                VALUES (@revision_id, @person_id, @action_kind, @actor, @created_at_utc);
                """;
            insert.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
            insert.Parameters.AddWithValue("person_id", Guid.Parse(personId.ToString()));
            insert.Parameters.AddWithValue("action_kind", action);
            insert.Parameters.AddWithValue("actor", normalizedActor);
            insert.Parameters.AddWithValue("created_at_utc", _timeProvider.GetUtcNow().ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await ReadEffectivePeopleAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<IReadOnlyList<CatalogueManualPhotoPerson>> ReadEffectivePeopleAsync(
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
                        PARTITION BY person_id
                        ORDER BY id DESC) AS row_number
                FROM photo_person_actions AS action
                WHERE asset_revision_id = @revision_id)
            SELECT
                person.id,
                person.display_name,
                action.actor,
                action.created_at_utc
            FROM latest_actions AS action
            INNER JOIN people AS person ON person.id = action.person_id
            WHERE action.row_number = 1
              AND action.action_kind = 'add'
              AND person.merged_into_person_id IS NULL
              AND person.display_name IS NOT NULL
              AND btrim(person.display_name) <> ''
            ORDER BY person.display_name, person.id;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<CatalogueManualPhotoPerson> people = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            people.Add(new CatalogueManualPhotoPerson(
                PersonId.From(reader.GetGuid(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return people;
    }

    private static async Task<string?> ReadLatestActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind
            FROM photo_person_actions
            WHERE asset_revision_id = @revision_id AND person_id = @person_id
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("person_id", Guid.Parse(personId.ToString()));
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

    private static async Task EnsureActivePersonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT display_name, merged_into_person_id
            FROM people
            WHERE id = @person_id
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("person_id", Guid.Parse(personId.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
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
            throw new InvalidOperationException(
                $"Person '{personId}' does not have an active display name.");
        }
    }

    private static string NormalizeActor(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        string value = actor.Trim();
        return value.Length <= 200
            ? value
            : throw new ArgumentException("Actor must be 200 characters or fewer.", nameof(actor));
    }
}
