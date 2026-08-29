using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SmartCollectionNameConflictException : Exception
{
    public SmartCollectionNameConflictException(string name)
        : base($"A smart collection named '{name}' already exists.")
    {
    }
}

/// <summary>
/// Persists normalized smart-collection filter definitions. Membership is never persisted;
/// callers evaluate the stored filter against the current catalogue through
/// <see cref="SqliteSmartCollectionQueryRepository"/>.
/// </summary>
public sealed class SqliteSmartCollectionRepository
{
    private const int FilterSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqliteSmartCollectionRepository(
        SqliteCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<SmartCollectionDefinition> CreateAsync(
        string name,
        SmartCollectionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        SmartCollectionName canonicalName = SmartCollectionName.Parse(name);
        SmartCollectionFilter canonicalFilter = CanonicalizeFilter(filter);
        SmartCollectionId id = SmartCollectionId.New();
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO smart_collections (
                id,
                normalized_name,
                display_name,
                filter_schema_version,
                filter_json,
                created_at_utc,
                updated_at_utc)
            VALUES (
                $id,
                $normalized_name,
                $display_name,
                $filter_schema_version,
                $filter_json,
                $created_at_utc,
                $updated_at_utc);
            """;
        AddDefinitionParameters(command, id, canonicalName, canonicalFilter, now, now);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new SmartCollectionNameConflictException(canonicalName.DisplayValue);
        }

        return new SmartCollectionDefinition(
            id,
            canonicalName.DisplayValue,
            canonicalFilter,
            now,
            now);
    }

    public async Task<IReadOnlyList<SmartCollectionDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name, filter_schema_version, filter_json, created_at_utc, updated_at_utc
            FROM smart_collections
            ORDER BY normalized_name, id;
            """;

        List<SmartCollectionDefinition> definitions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            definitions.Add(ReadDefinition(reader));
        }

        return definitions;
    }

    public async Task<SmartCollectionDefinition?> GetAsync(
        SmartCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return await GetAsync(connection, transaction: null, id, cancellationToken);
    }

    internal static async Task<SmartCollectionDefinition?> GetAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        SmartCollectionId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, display_name, filter_schema_version, filter_json, created_at_utc, updated_at_utc
            FROM smart_collections
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDefinition(reader)
            : null;
    }

    public async Task<SmartCollectionDefinition?> UpdateAsync(
        SmartCollectionId id,
        string name,
        SmartCollectionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        SmartCollectionName canonicalName = SmartCollectionName.Parse(name);
        SmartCollectionFilter canonicalFilter = CanonicalizeFilter(filter);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE smart_collections
            SET normalized_name = $normalized_name,
                display_name = $display_name,
                filter_schema_version = $filter_schema_version,
                filter_json = $filter_json,
                updated_at_utc = $updated_at_utc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$normalized_name", canonicalName.NormalizedValue);
        command.Parameters.AddWithValue("$display_name", canonicalName.DisplayValue);
        command.Parameters.AddWithValue("$filter_schema_version", FilterSchemaVersion);
        command.Parameters.AddWithValue("$filter_json", SerializeFilter(canonicalFilter));
        command.Parameters.AddWithValue("$updated_at_utc", now.ToString("O", CultureInfo.InvariantCulture));

        int updated;
        try
        {
            updated = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new SmartCollectionNameConflictException(canonicalName.DisplayValue);
        }

        if (updated == 0)
        {
            return null;
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        SmartCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM smart_collections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void AddDefinitionParameters(
        SqliteCommand command,
        SmartCollectionId id,
        SmartCollectionName name,
        SmartCollectionFilter filter,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$normalized_name", name.NormalizedValue);
        command.Parameters.AddWithValue("$display_name", name.DisplayValue);
        command.Parameters.AddWithValue("$filter_schema_version", FilterSchemaVersion);
        command.Parameters.AddWithValue("$filter_json", SerializeFilter(filter));
        command.Parameters.AddWithValue("$created_at_utc", createdAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated_at_utc", updatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    internal static SmartCollectionDefinition ReadDefinition(SqliteDataReader reader)
    {
        int filterSchemaVersion = reader.GetInt32(2);
        if (filterSchemaVersion is not 1 and not FilterSchemaVersion)
        {
            throw new InvalidDataException(
                $"Smart collection filter schema version {filterSchemaVersion} is not supported.");
        }

        return new SmartCollectionDefinition(
            SmartCollectionId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            DeserializeFilter(reader.GetString(3)),
            ParseTimestamp(reader.GetString(4)),
            ParseTimestamp(reader.GetString(5)));
    }

    private static SmartCollectionFilter CanonicalizeFilter(SmartCollectionFilter filter) => new(
        filter.People.OrderBy(person => person.ToString(), StringComparer.Ordinal),
        filter.PeopleMatch,
        filter.Tags.OrderBy(tag => tag, StringComparer.Ordinal),
        filter.TagMatch,
        filter.Location,
        filter.Taken,
        filter.LocationPlace);

    private static string SerializeFilter(SmartCollectionFilter filter)
    {
        PersistedFilter payload = new(
            filter.People.Select(person => person.ToString()).ToArray(),
            filter.PeopleMatch,
            filter.Tags.ToArray(),
            filter.TagMatch,
            filter.Location is null && filter.LocationPlace is null
                ? null
                : new PersistedLocation(
                    filter.LocationPlace,
                    filter.Location?.South,
                    filter.Location?.West,
                    filter.Location?.North,
                    filter.Location?.East),
            filter.Taken is null
                ? null
                : new PersistedTaken(
                    filter.Taken.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    filter.Taken.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static SmartCollectionFilter DeserializeFilter(string json)
    {
        PersistedFilter payload = JsonSerializer.Deserialize<PersistedFilter>(json, JsonOptions)
            ?? throw new InvalidDataException("Smart collection filter JSON is empty.");

        SmartCollectionGeoBounds? bounds = ParseBounds(payload.Location);
        return new SmartCollectionFilter(
            payload.People.Select(ParsePersonId),
            payload.PeopleMatch,
            payload.Tags,
            payload.TagMatch,
            bounds,
            payload.Taken is null
                ? null
                : new SmartCollectionDateRange(
                    ParseDate(payload.Taken.From),
                    ParseDate(payload.Taken.To)),
            payload.Location?.Place);
    }

    private static SmartCollectionGeoBounds? ParseBounds(PersistedLocation? location)
    {
        if (location is null)
        {
            return null;
        }

        bool any = location.South.HasValue || location.West.HasValue ||
            location.North.HasValue || location.East.HasValue;
        bool all = location.South.HasValue && location.West.HasValue &&
            location.North.HasValue && location.East.HasValue;
        if (any && !all)
        {
            throw new InvalidDataException("Stored Smart Collection GPS bounds are incomplete.");
        }

        return all
            ? new SmartCollectionGeoBounds(
                location.South!.Value,
                location.West!.Value,
                location.North!.Value,
                location.East!.Value)
            : null;
    }

    private static PhotoIdentity.Core.Identifiers.PersonId ParsePersonId(string value)
    {
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            throw new InvalidDataException($"Stored smart collection person identifier '{value}' is invalid.");
        }

        return PhotoIdentity.Core.Identifiers.PersonId.From(parsed);
    }

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(
        value,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None);

    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);

    internal static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS smart_collections (
                id TEXT NOT NULL PRIMARY KEY,
                normalized_name TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                filter_schema_version INTEGER NOT NULL CHECK (filter_schema_version IN (1, 2)),
                filter_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                CHECK (length(normalized_name) BETWEEN 1 AND 120),
                CHECK (length(display_name) BETWEEN 1 AND 120),
                CHECK (length(filter_json) > 0));
            CREATE INDEX IF NOT EXISTS ix_smart_collections_name
                ON smart_collections (normalized_name, id);
            UPDATE smart_collections
            SET filter_schema_version = 2
            WHERE filter_schema_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record PersistedFilter(
        string[] People,
        string PeopleMatch,
        string[] Tags,
        string TagMatch,
        PersistedLocation? Location,
        PersistedTaken? Taken);

    private sealed record PersistedLocation(
        string? Place = null,
        double? South = null,
        double? West = null,
        double? North = null,
        double? East = null);

    private sealed record PersistedTaken(
        string From,
        string To);
}
