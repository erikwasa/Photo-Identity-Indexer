using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresCatalogueSourceRepository(PostgresCatalogueDatabase database) : ICatalogueSourceRepository
{
    public async Task<ArchiveCatalogueSource> GetOrCreateLocalFolderSourceAsync(
        string rootLocator, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
        string root = Path.GetFullPath(rootLocator);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources(id, kind, root_locator, created_at_utc)
            VALUES (@id, 'local-folder', @root, @created)
            ON CONFLICT(kind, root_locator) DO UPDATE SET kind = excluded.kind
            RETURNING id, kind, root_locator, created_at_utc;
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("root", root);
        command.Parameters.AddWithValue("created", createdAtUtc.ToUniversalTime());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Catalogue source registration returned no row.");
        return new(SourceId.From(reader.GetGuid(0)), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3));
    }
}
