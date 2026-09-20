using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresSmartCollectionRepository : ISmartCollectionRepository
{
    private const int FilterSchemaVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresSmartCollectionRepository(
        PostgresCatalogueDatabase database,
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
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
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
                @id,
                @normalized_name,
                @display_name,
                @filter_schema_version,
                @filter_json,
                @created_at_utc,
                @updated_at_utc);
            """;
        AddDefinitionParameters(command, id, canonicalName, canonicalFilter, now, now);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new SmartCollectionNameConflictException(canonicalName.DisplayValue);
        }

        return new SmartCollectionDefinition(id, canonicalName.DisplayValue, canonicalFilter, now, now);
    }

    public async Task<IReadOnlyList<SmartCollectionDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name, filter_schema_version, filter_json, created_at_utc, updated_at_utc
            FROM smart_collections
            ORDER BY normalized_name, id;
            """;

        List<SmartCollectionDefinition> definitions = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
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
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, id, cancellationToken);
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
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE smart_collections
            SET normalized_name = @normalized_name,
                display_name = @display_name,
                filter_schema_version = @filter_schema_version,
                filter_json = @filter_json,
                updated_at_utc = @updated_at_utc
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        command.Parameters.AddWithValue("normalized_name", canonicalName.NormalizedValue);
        command.Parameters.AddWithValue("display_name", canonicalName.DisplayValue);
        command.Parameters.AddWithValue("filter_schema_version", FilterSchemaVersion);
        NpgsqlParameter filterJson = command.Parameters.Add("filter_json", NpgsqlDbType.Jsonb);
        filterJson.Value = SerializeFilter(canonicalFilter);
        command.Parameters.AddWithValue("updated_at_utc", now);

        int updated;
        try
        {
            updated = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new SmartCollectionNameConflictException(canonicalName.DisplayValue);
        }

        return updated == 0
            ? null
            : await GetAsync(connection, id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        SmartCollectionId id,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM smart_collections WHERE id = @id;";
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task<SmartCollectionDefinition?> GetAsync(
        NpgsqlConnection connection,
        SmartCollectionId id,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name, filter_schema_version, filter_json, created_at_utc, updated_at_utc
            FROM smart_collections
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadDefinition(reader)
            : null;
    }

    private static void AddDefinitionParameters(
        NpgsqlCommand command,
        SmartCollectionId id,
        SmartCollectionName name,
        SmartCollectionFilter filter,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id.Value);
        command.Parameters.AddWithValue("normalized_name", name.NormalizedValue);
        command.Parameters.AddWithValue("display_name", name.DisplayValue);
        command.Parameters.AddWithValue("filter_schema_version", FilterSchemaVersion);
        NpgsqlParameter filterJson = command.Parameters.Add("filter_json", NpgsqlDbType.Jsonb);
        filterJson.Value = SerializeFilter(filter);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc);
        command.Parameters.AddWithValue("updated_at_utc", updatedAtUtc);
    }

    private static SmartCollectionDefinition ReadDefinition(NpgsqlDataReader reader)
    {
        int filterSchemaVersion = reader.GetInt32(2);
        if (filterSchemaVersion is < 1 or > FilterSchemaVersion)
        {
            throw new InvalidDataException(
                $"Smart collection filter schema version {filterSchemaVersion} is not supported.");
        }

        return new SmartCollectionDefinition(
            SmartCollectionId.From(reader.GetGuid(0)),
            reader.GetString(1),
            DeserializeFilter(reader.GetString(3)),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    private static SmartCollectionFilter CanonicalizeFilter(SmartCollectionFilter filter) => new(
        people: filter.People.OrderBy(person => person.ToString(), StringComparer.Ordinal),
        peopleMatch: filter.PeopleMatch,
        tags: filter.Tags.OrderBy(tag => tag, StringComparer.Ordinal),
        tagMatch: filter.TagMatch,
        location: filter.Location,
        taken: filter.Taken,
        locationPlaces: filter.LocationPlaces);

    private static string SerializeFilter(SmartCollectionFilter filter)
    {
        PersistedFilter payload = new(
            filter.People.Select(person => person.ToString()).ToArray(),
            filter.PeopleMatch,
            filter.Tags.ToArray(),
            filter.TagMatch,
            filter.Location is null && filter.LocationPlaces.Count == 0
                ? null
                : new PersistedLocation(
                    Place: null,
                    South: filter.Location?.South,
                    West: filter.Location?.West,
                    North: filter.Location?.North,
                    East: filter.Location?.East,
                    Places: filter.LocationPlaces.ToArray()),
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
                : new SmartCollectionDateRange(ParseDate(payload.Taken.From), ParseDate(payload.Taken.To)),
            locationPlace: payload.Location?.Place,
            locationPlaces: payload.Location?.Places);
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

    private static PersonId ParsePersonId(string value)
    {
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            throw new InvalidDataException($"Stored smart collection person identifier '{value}' is invalid.");
        }

        return PersonId.From(parsed);
    }

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(
        value,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None);

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
        double? East = null,
        string[]? Places = null);

    private sealed record PersistedTaken(string From, string To);
}
