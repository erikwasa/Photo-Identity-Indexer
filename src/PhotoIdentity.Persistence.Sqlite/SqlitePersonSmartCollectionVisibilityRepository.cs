using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persists the presentation-only preference that removes a person from normal Smart Collection discovery.
/// </summary>
public sealed class SqlitePersonSmartCollectionVisibilityRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqlitePersonSmartCollectionVisibilityRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlySet<PersonId>> GetHiddenPersonIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT visibility.person_id
            FROM person_smart_collection_visibility AS visibility
            INNER JOIN people AS person ON person.id = visibility.person_id
            WHERE visibility.hidden_from_smart_collections = 1
              AND person.merged_into_person_id IS NULL
            ORDER BY visibility.person_id;
            """;

        HashSet<PersonId> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(PersonId.From(Guid.Parse(reader.GetString(0))));
        }

        return result;
    }

    public async Task SetHiddenAsync(
        PersonId personId,
        bool hiddenFromSmartCollections,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        if (hiddenFromSmartCollections)
        {
            command.CommandText = """
                INSERT INTO person_smart_collection_visibility (
                    person_id,
                    hidden_from_smart_collections,
                    changed_at_utc)
                VALUES ($person_id, 1, $changed_at_utc)
                ON CONFLICT(person_id) DO UPDATE SET
                    hidden_from_smart_collections = 1,
                    changed_at_utc = excluded.changed_at_utc;
                """;
            command.Parameters.AddWithValue("$person_id", personId.ToString());
            command.Parameters.AddWithValue("$changed_at_utc", Format(changedAtUtc));
        }
        else
        {
            command.CommandText = """
                DELETE FROM person_smart_collection_visibility
                WHERE person_id = $person_id;
                """;
            command.Parameters.AddWithValue("$person_id", personId.ToString());
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
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
            SELECT 1
            FROM people
            WHERE id = $person_id
              AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException($"Active person {personId} was not found.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
