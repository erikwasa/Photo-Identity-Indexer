using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Persists the single permanent archive source and recursively included folders in PostgreSQL.
/// </summary>
public sealed class PostgresArchiveCoverageRepository : IArchiveCoverageRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveCoverageRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveCoverageState?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);

        ArchiveCatalogueSource? source = await ReadConfiguredSourceAsync(
            connection,
            transaction: null,
            cancellationToken);
        if (source is null)
        {
            return null;
        }

        return new ArchiveCoverageState(
            source,
            await ReadIncludedFoldersAsync(
                connection,
                transaction: null,
                source.SourceId,
                cancellationToken));
    }

    public async Task<ArchiveCoverageState> ConfigureAndIncludeAsync(
        ArchiveCatalogueSource source,
        string relativeFolder,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        string normalizedFolder =
            ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        DateTimeOffset configuredAt = configuredAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await UpsertSourceAsync(connection, transaction, source, cancellationToken);

        ArchiveCatalogueSource? configured = await ReadConfiguredSourceAsync(
            connection,
            transaction,
            cancellationToken);
        if (configured is null)
        {
            await using NpgsqlCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO archive_configuration (
                    id,
                    source_id,
                    configured_at_utc)
                VALUES (
                    1,
                    @source_id,
                    @configured_at_utc);
                """;
            insert.Parameters.AddWithValue(
                "source_id",
                Guid.Parse(source.SourceId.ToString()));
            insert.Parameters.AddWithValue(
                "configured_at_utc",
                configuredAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            configured = source;
        }
        else if (configured.SourceId != source.SourceId)
        {
            throw new InvalidOperationException(
                "This catalogue is already configured for a different permanent archive source.");
        }

        IReadOnlyList<string> current = await ReadIncludedFoldersAsync(
            connection,
            transaction,
            configured.SourceId,
            cancellationToken);
        IReadOnlyList<string> normalized = ArchiveCoverage.NormalizeIncludedFolders(
            current.Append(normalizedFolder));

        await WriteIncludedFoldersAsync(
            connection,
            transaction,
            configured.SourceId,
            normalized,
            configuredAt,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ArchiveCoverageState(configured, normalized);
    }

    public async Task<ArchiveCoverageState> ReplaceIncludedFoldersAsync(
        IEnumerable<string> relativeFolders,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativeFolders);
        IReadOnlyList<string> normalized =
            ArchiveCoverage.NormalizeIncludedFolders(relativeFolders);
        DateTimeOffset configuredAt = configuredAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        ArchiveCatalogueSource configured = await ReadConfiguredSourceAsync(
            connection,
            transaction,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The permanent archive has not been configured yet.");

        await WriteIncludedFoldersAsync(
            connection,
            transaction,
            configured.SourceId,
            normalized,
            configuredAt,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ArchiveCoverageState(configured, normalized);
    }

    private static async Task UpsertSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ArchiveCatalogueSource source,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO sources (
                id,
                kind,
                root_locator,
                created_at_utc)
            VALUES (
                @id,
                @kind,
                @root_locator,
                @created_at_utc)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                root_locator = excluded.root_locator;
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(source.SourceId.ToString()));
        command.Parameters.AddWithValue("kind", source.Kind);
        command.Parameters.AddWithValue("root_locator", source.RootLocator);
        command.Parameters.AddWithValue(
            "created_at_utc",
            source.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ArchiveCatalogueSource?> ReadConfiguredSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                source.id,
                source.kind,
                source.root_locator,
                source.created_at_utc
            FROM archive_configuration AS archive
            INNER JOIN sources AS source
                ON source.id = archive.source_id
            WHERE archive.id = 1;
            """;

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveCatalogueSource(
            SourceId.From(reader.GetGuid(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    private static async Task<IReadOnlyList<string>> ReadIncludedFoldersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        SourceId sourceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT relative_path
            FROM archive_included_folders
            WHERE source_id = @source_id
            ORDER BY relative_path;
            """;
        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));

        List<string> folders = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            folders.Add(reader.GetString(0));
        }

        return folders;
    }

    private static async Task WriteIncludedFoldersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SourceId sourceId,
        IReadOnlyList<string> includedFolders,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken)
    {
        await using (NpgsqlCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                """
                DELETE FROM archive_included_folders
                WHERE source_id = @source_id;
                """;
            delete.Parameters.AddWithValue(
                "source_id",
                Guid.Parse(sourceId.ToString()));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (string folder in includedFolders)
        {
            await using NpgsqlCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO archive_included_folders (
                    source_id,
                    relative_path,
                    included_at_utc)
                VALUES (
                    @source_id,
                    @relative_path,
                    @included_at_utc);
                """;
            insert.Parameters.AddWithValue(
                "source_id",
                Guid.Parse(sourceId.ToString()));
            insert.Parameters.AddWithValue(
                "relative_path",
                folder);
            insert.Parameters.AddWithValue(
                "included_at_utc",
                configuredAtUtc.ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
