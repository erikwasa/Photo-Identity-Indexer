using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persists local favorite-person preferences independently from identity evidence and model scoring.
/// </summary>
public sealed class SqliteFavoritePeopleRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteFavoritePeopleRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlySet<PersonId>> GetFavoritePersonIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT person_id FROM person_favorites ORDER BY person_id;";

        HashSet<PersonId> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(PersonId.From(Guid.Parse(reader.GetString(0))));
        }

        return result;
    }

    public async Task SetFavoriteAsync(
        PersonId personId,
        bool isFavorite,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await RequireActivePersonAsync(connection, transaction, personId, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isFavorite)
        {
            command.CommandText = """
                INSERT INTO person_favorites (person_id, favorited_at_utc)
                VALUES ($person_id, $favorited_at_utc)
                ON CONFLICT(person_id) DO UPDATE SET
                    favorited_at_utc = excluded.favorited_at_utc;
                """;
            command.Parameters.AddWithValue("$person_id", personId.ToString());
            command.Parameters.AddWithValue("$favorited_at_utc", Format(changedAtUtc));
        }
        else
        {
            command.CommandText = "DELETE FROM person_favorites WHERE person_id = $person_id;";
            command.Parameters.AddWithValue("$person_id", personId.ToString());
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    public async Task ConsolidateMergeAsync(
        PersonId sourcePersonId,
        PersonId targetPersonId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        bool shouldFavoriteTarget;
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM person_favorites
                    WHERE person_id IN ($source_person_id, $target_person_id));
                """;
            read.Parameters.AddWithValue("$source_person_id", sourcePersonId.ToString());
            read.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
            object? value = await read.ExecuteScalarAsync(cancellationToken);
            shouldFavoriteTarget = Convert.ToInt64(
                value,
                System.Globalization.CultureInfo.InvariantCulture) == 1;
        }

        using (SqliteCommand deleteSource = connection.CreateCommand())
        {
            deleteSource.Transaction = transaction;
            deleteSource.CommandText = "DELETE FROM person_favorites WHERE person_id = $source_person_id;";
            deleteSource.Parameters.AddWithValue("$source_person_id", sourcePersonId.ToString());
            await deleteSource.ExecuteNonQueryAsync(cancellationToken);
        }

        if (shouldFavoriteTarget)
        {
            using SqliteCommand favoriteTarget = connection.CreateCommand();
            favoriteTarget.Transaction = transaction;
            favoriteTarget.CommandText = """
                INSERT INTO person_favorites (person_id, favorited_at_utc)
                VALUES ($target_person_id, $favorited_at_utc)
                ON CONFLICT(person_id) DO UPDATE SET
                    favorited_at_utc = excluded.favorited_at_utc;
                """;
            favoriteTarget.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
            favoriteTarget.Parameters.AddWithValue("$favorited_at_utc", Format(changedAtUtc));
            await favoriteTarget.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS person_favorites (
                person_id TEXT NOT NULL PRIMARY KEY,
                favorited_at_utc TEXT NOT NULL,
                FOREIGN KEY (person_id) REFERENCES people (id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
