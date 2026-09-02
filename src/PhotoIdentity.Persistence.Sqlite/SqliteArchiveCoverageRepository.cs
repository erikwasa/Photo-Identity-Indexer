using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record ArchiveCoverageConfiguration(
    CatalogueSource Source,
    IReadOnlyList<string> IncludedFolders);

/// <summary>
/// Persists the single permanent local archive source and its recursively included folders.
/// </summary>
public sealed class SqliteArchiveCoverageRepository : IArchiveCoverageRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveCoverageRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    async Task<ArchiveCoverageState?> IArchiveCoverageRepository.GetAsync(
        CancellationToken cancellationToken)
    {
        ArchiveCoverageConfiguration? configured = await GetAsync(cancellationToken);
        return configured is null ? null : ToCoreState(configured);
    }

    async Task<ArchiveCoverageState> IArchiveCoverageRepository.ConfigureAndIncludeAsync(
        ArchiveCatalogueSource source,
        string relativeFolder,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken)
    {
        ArchiveCoverageConfiguration configured = await ConfigureAndIncludeAsync(
            new CatalogueSource(
                source.SourceId,
                source.Kind,
                source.RootLocator,
                source.CreatedAtUtc),
            relativeFolder,
            configuredAtUtc,
            cancellationToken);
        return ToCoreState(configured);
    }

    async Task<ArchiveCoverageState> IArchiveCoverageRepository.ReplaceIncludedFoldersAsync(
        IEnumerable<string> relativeFolders,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken)
    {
        ArchiveCoverageConfiguration configured = await ReplaceIncludedFoldersAsync(
            relativeFolders,
            configuredAtUtc,
            cancellationToken);
        return ToCoreState(configured);
    }

    public async Task<ArchiveCoverageConfiguration?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        CatalogueSource? source = await ReadConfiguredSourceAsync(connection, transaction: null, cancellationToken);
        if (source is null)
        {
            return null;
        }

        return new ArchiveCoverageConfiguration(
            source,
            await ReadIncludedFoldersAsync(connection, transaction: null, source.Id, cancellationToken));
    }

    public async Task<ArchiveCoverageConfiguration> ConfigureAndIncludeAsync(
        CatalogueSource source,
        string relativeFolder,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        string normalizedFolder = ArchiveCoverage.NormalizeRelativeFolder(relativeFolder);
        DateTimeOffset configuredAt = configuredAtUtc.ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        CatalogueSource? configured = await ReadConfiguredSourceAsync(connection, transaction, cancellationToken);
        if (configured is null)
        {
            using SqliteCommand insertConfiguration = connection.CreateCommand();
            insertConfiguration.Transaction = transaction;
            insertConfiguration.CommandText = """
                INSERT INTO archive_configuration (id, source_id, configured_at_utc)
                VALUES (1, $source_id, $configured_at_utc);
                """;
            insertConfiguration.Parameters.AddWithValue("$source_id", source.Id.ToString());
            insertConfiguration.Parameters.AddWithValue("$configured_at_utc", Format(configuredAt));
            await insertConfiguration.ExecuteNonQueryAsync(cancellationToken);
            configured = source;
        }
        else if (configured.Id != source.Id)
        {
            throw new InvalidOperationException(
                $"This catalogue is already configured for archive root '{configured.RootLocator}'. " +
                $"Refusing to replace it with '{source.RootLocator}'.");
        }

        IReadOnlyList<string> current = await ReadIncludedFoldersAsync(
            connection,
            transaction,
            configured.Id,
            cancellationToken);
        IReadOnlyList<string> normalized = ArchiveCoverage.NormalizeIncludedFolders(
            current.Append(normalizedFolder));

        await WriteIncludedFoldersAsync(
            connection,
            transaction,
            configured.Id,
            normalized,
            configuredAt,
            cancellationToken);

        transaction.Commit();
        return new ArchiveCoverageConfiguration(configured, normalized);
    }

    /// <summary>
    /// Replaces only the root-relative coverage list for the already configured permanent source.
    /// Source identity and catalogue assets are preserved; this operation only changes which folders
    /// future archive synchronization and processing runs consider included.
    /// </summary>
    public async Task<ArchiveCoverageConfiguration> ReplaceIncludedFoldersAsync(
        IEnumerable<string> relativeFolders,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativeFolders);
        IReadOnlyList<string> normalized = ArchiveCoverage.NormalizeIncludedFolders(relativeFolders);
        DateTimeOffset configuredAt = configuredAtUtc.ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        CatalogueSource configured = await ReadConfiguredSourceAsync(connection, transaction, cancellationToken)
            ?? throw new InvalidOperationException("The permanent archive has not been configured yet.");

        await WriteIncludedFoldersAsync(
            connection,
            transaction,
            configured.Id,
            normalized,
            configuredAt,
            cancellationToken);

        transaction.Commit();
        return new ArchiveCoverageConfiguration(configured, normalized);
    }

    private static async Task WriteIncludedFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceId sourceId,
        IReadOnlyList<string> includedFolders,
        DateTimeOffset configuredAtUtc,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM archive_included_folders WHERE source_id = $source_id;";
            delete.Parameters.AddWithValue("$source_id", sourceId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (string folder in includedFolders)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO archive_included_folders (source_id, relative_path, included_at_utc)
                VALUES ($source_id, $relative_path, $included_at_utc);
                """;
            insert.Parameters.AddWithValue("$source_id", sourceId.ToString());
            insert.Parameters.AddWithValue("$relative_path", folder);
            insert.Parameters.AddWithValue("$included_at_utc", Format(configuredAtUtc));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<CatalogueSource?> ReadConfiguredSourceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT source.id, source.kind, source.root_locator, source.created_at_utc
            FROM archive_configuration AS archive
            INNER JOIN sources AS source ON source.id = archive.source_id
            WHERE archive.id = 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogueSource(
                SourceId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.GetString(2),
                ParseTimestamp(reader.GetString(3)))
            : null;
    }

    private static async Task<IReadOnlyList<string>> ReadIncludedFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        SourceId sourceId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT relative_path
            FROM archive_included_folders
            WHERE source_id = $source_id
            ORDER BY relative_path;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());

        List<string> folders = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            folders.Add(reader.GetString(0));
        }

        return folders;
    }

    private static ArchiveCoverageState ToCoreState(
        ArchiveCoverageConfiguration configured) => new(
        new ArchiveCatalogueSource(
            configured.Source.Id,
            configured.Source.Kind,
            configured.Source.RootLocator,
            configured.Source.CreatedAtUtc),
        configured.IncludedFolders);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
