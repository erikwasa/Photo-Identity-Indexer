using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record SmartCollectionPhoto(
    AssetRevisionId RevisionId,
    AssetId AssetId,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    DateTime? TakenAtLocal,
    double? Latitude,
    double? Longitude);

public sealed record SmartCollectionPhotoPage(
    IReadOnlyList<SmartCollectionPhoto> Items,
    int Offset,
    int Limit,
    int Total,
    SmartCollectionFilter Filter);

public sealed class SqliteSmartCollectionQueryRepository
{
    private const string CommonCtes = """
        WITH latest_review_action AS (
            SELECT review_actions.face_occurrence_id, review_actions.action_kind, review_actions.person_id,
                   ROW_NUMBER() OVER (PARTITION BY review_actions.face_occurrence_id ORDER BY review_actions.id DESC) AS row_number
            FROM review_actions
            WHERE review_actions.action_kind IN ('assign', 'unknown', 'reject')
              AND review_actions.reversed_at_utc IS NULL
        ),
        confirmed_revision_people AS (
            SELECT DISTINCT face_occurrences.asset_revision_id AS revision_id, latest_review_action.person_id
            FROM face_occurrences
            INNER JOIN latest_review_action ON latest_review_action.face_occurrence_id = face_occurrences.id
                AND latest_review_action.row_number = 1 AND latest_review_action.action_kind = 'assign'
            INNER JOIN people ON people.id = latest_review_action.person_id AND people.merged_into_person_id IS NULL
        ),
        latest_photo_person_action AS (
            SELECT photo_person_actions.asset_revision_id, photo_person_actions.person_id, photo_person_actions.action_kind,
                   ROW_NUMBER() OVER (PARTITION BY photo_person_actions.asset_revision_id, photo_person_actions.person_id ORDER BY photo_person_actions.id DESC) AS row_number
            FROM photo_person_actions
        ),
        active_manual_revision_people AS (
            SELECT latest_photo_person_action.asset_revision_id AS revision_id, latest_photo_person_action.person_id
            FROM latest_photo_person_action
            INNER JOIN people ON people.id = latest_photo_person_action.person_id AND people.merged_into_person_id IS NULL
            WHERE latest_photo_person_action.row_number = 1 AND latest_photo_person_action.action_kind = 'add'
        ),
        revision_people AS (
            SELECT revision_id, person_id FROM confirmed_revision_people
            UNION
            SELECT revision_id, person_id FROM active_manual_revision_people
        ),
        latest_tag_action AS (
            SELECT photo_tag_actions.asset_revision_id, photo_tag_actions.tag_id, photo_tag_actions.action_kind,
                   ROW_NUMBER() OVER (PARTITION BY photo_tag_actions.asset_revision_id, photo_tag_actions.tag_id ORDER BY photo_tag_actions.id DESC) AS row_number
            FROM photo_tag_actions
        ),
        active_revision_tags AS (
            SELECT latest_tag_action.asset_revision_id AS revision_id, photo_tags.normalized_name AS normalized_value
            FROM latest_tag_action
            INNER JOIN photo_tags ON photo_tags.id = latest_tag_action.tag_id
            WHERE latest_tag_action.row_number = 1 AND latest_tag_action.action_kind = 'add'
              AND photo_tags.normalized_name <> 'places'
              AND photo_tags.normalized_name NOT LIKE 'places/%'
        ),
        latest_place_action AS (
            SELECT photo_place_actions.asset_revision_id, photo_place_actions.tag_id, photo_place_actions.action_kind,
                   ROW_NUMBER() OVER (PARTITION BY photo_place_actions.asset_revision_id ORDER BY photo_place_actions.id DESC) AS row_number
            FROM photo_place_actions
        ),
        effective_revision_places AS (
            SELECT latest_place_action.asset_revision_id AS revision_id, photo_tags.normalized_name AS normalized_value
            FROM latest_place_action
            INNER JOIN photo_tags ON photo_tags.id = latest_place_action.tag_id
            WHERE latest_place_action.row_number = 1 AND latest_place_action.action_kind = 'set'
        )
        """;

    private readonly SqliteCatalogueDatabase _database;

    public SqliteSmartCollectionQueryRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<SmartCollectionPhotoPage> QueryAsync(SmartCollectionFilter filter, int offset = 0, int limit = 40, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "Smart-collection page size must be between 1 and 200.");

        string where = BuildWhere(filter);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoPersonSchema.EnsureAsync(connection, transaction: null, cancellationToken);
        await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(connection, cancellationToken);
        await EnsurePhotoMetadataSchemaAsync(connection, cancellationToken);

        int total;
        using (SqliteCommand count = connection.CreateCommand())
        {
            count.CommandText = $"""
                {CommonCtes}
                SELECT COUNT(*)
                FROM asset_revisions
                INNER JOIN assets ON assets.id = asset_revisions.asset_id
                LEFT JOIN photo_capture_metadata ON photo_capture_metadata.asset_revision_id = asset_revisions.id
                WHERE assets.deleted_at_utc IS NULL
                  {where};
                """;
            AddFilterParameters(count, filter);
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {CommonCtes}
            SELECT asset_revisions.id, asset_revisions.asset_id, asset_revisions.observed_at_utc,
                   asset_revisions.media_type, asset_revisions.width, asset_revisions.height,
                   photo_capture_metadata.taken_at_local, photo_capture_metadata.latitude, photo_capture_metadata.longitude
            FROM asset_revisions
            INNER JOIN assets ON assets.id = asset_revisions.asset_id
            LEFT JOIN photo_capture_metadata ON photo_capture_metadata.asset_revision_id = asset_revisions.id
            WHERE assets.deleted_at_utc IS NULL
              {where}
            ORDER BY photo_capture_metadata.taken_at_local DESC, asset_revisions.observed_at_utc DESC, asset_revisions.id
            LIMIT $limit OFFSET $offset;
            """;
        AddFilterParameters(command, filter);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        List<SmartCollectionPhoto> items = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SmartCollectionPhoto(
                AssetRevisionId.From(Guid.Parse(reader.GetString(0))), AssetId.From(Guid.Parse(reader.GetString(1))),
                ParseObserved(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : ParseLocal(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetDouble(7), reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }
        return new SmartCollectionPhotoPage(items, offset, limit, total, filter);
    }

    private static string BuildWhere(SmartCollectionFilter filter)
    {
        List<string> predicates = [];
        if (filter.People.Count > 0)
        {
            string people = string.Join(", ", Enumerable.Range(0, filter.People.Count).Select(index => $"$person_{index}"));
            string having = filter.PeopleMatch == SmartCollectionMatchModes.All ? "COUNT(DISTINCT person_id) = $person_count" : "COUNT(DISTINCT person_id) >= 1";
            predicates.Add($"AND asset_revisions.id IN (SELECT revision_id FROM revision_people WHERE person_id IN ({people}) GROUP BY revision_id HAVING {having})");
        }
        if (filter.Tags.Count > 0)
        {
            string tags = string.Join(", ", Enumerable.Range(0, filter.Tags.Count).Select(index => $"$tag_{index}"));
            string having = filter.TagMatch == SmartCollectionMatchModes.All ? "COUNT(DISTINCT normalized_value) = $tag_count" : "COUNT(DISTINCT normalized_value) >= 1";
            predicates.Add($"AND asset_revisions.id IN (SELECT revision_id FROM active_revision_tags WHERE normalized_value IN ({tags}) GROUP BY revision_id HAVING {having})");
        }
        if (!string.IsNullOrWhiteSpace(filter.Location?.Place))
        {
            predicates.Add("AND asset_revisions.id IN (SELECT revision_id FROM effective_revision_places WHERE normalized_value = $place OR normalized_value LIKE $place_descendant ESCAPE '\\')");
        }
        if (filter.Location?.Bounds is not null)
        {
            predicates.Add("AND photo_capture_metadata.latitude BETWEEN $south AND $north");
            predicates.Add("AND photo_capture_metadata.longitude BETWEEN $west AND $east");
        }
        if (filter.Taken is not null)
        {
            predicates.Add("AND photo_capture_metadata.taken_at_local >= $taken_from");
            predicates.Add("AND photo_capture_metadata.taken_at_local <= $taken_to");
        }
        return predicates.Count == 0 ? string.Empty : string.Join(Environment.NewLine, predicates);
    }

    private static void AddFilterParameters(SqliteCommand command, SmartCollectionFilter filter)
    {
        for (int index = 0; index < filter.People.Count; index++) command.Parameters.AddWithValue($"$person_{index}", filter.People[index].ToString());
        command.Parameters.AddWithValue("$person_count", filter.People.Count);
        for (int index = 0; index < filter.Tags.Count; index++) command.Parameters.AddWithValue($"$tag_{index}", filter.Tags[index]);
        command.Parameters.AddWithValue("$tag_count", filter.Tags.Count);
        if (!string.IsNullOrWhiteSpace(filter.Location?.Place))
        {
            command.Parameters.AddWithValue("$place", filter.Location.Place);
            command.Parameters.AddWithValue("$place_descendant", EscapeLike(filter.Location.Place) + "/%");
        }
        if (filter.Location?.Bounds is not null)
        {
            command.Parameters.AddWithValue("$south", filter.Location.Bounds.South);
            command.Parameters.AddWithValue("$west", filter.Location.Bounds.West);
            command.Parameters.AddWithValue("$north", filter.Location.Bounds.North);
            command.Parameters.AddWithValue("$east", filter.Location.Bounds.East);
        }
        if (filter.Taken is not null)
        {
            command.Parameters.AddWithValue("$taken_from", FormatDateStart(filter.Taken.From));
            command.Parameters.AddWithValue("$taken_to", FormatDateEnd(filter.Taken.To));
        }
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    private static string FormatDateStart(DateOnly value) => $"{value:yyyy-MM-dd}T00:00:00.0000000";
    private static string FormatDateEnd(DateOnly value) => $"{value:yyyy-MM-dd}T23:59:59.9999999";
    private static DateTimeOffset ParseObserved(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTime ParseLocal(string value) => DateTime.SpecifyKind(DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture, DateTimeStyles.None), DateTimeKind.Unspecified);

    private static async Task EnsurePhotoMetadataSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_capture_metadata (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                taken_at_local TEXT NULL,
                utc_offset_minutes INTEGER NULL CHECK (utc_offset_minutes IS NULL OR utc_offset_minutes BETWEEN -840 AND 840),
                latitude REAL NULL CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
                longitude REAL NULL CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
                extracted_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CHECK ((latitude IS NULL) = (longitude IS NULL)),
                CHECK (utc_offset_minutes IS NULL OR taken_at_local IS NOT NULL));
            CREATE INDEX IF NOT EXISTS ix_photo_capture_metadata_taken ON photo_capture_metadata (taken_at_local, asset_revision_id);
            CREATE INDEX IF NOT EXISTS ix_photo_capture_metadata_location ON photo_capture_metadata (latitude, longitude, asset_revision_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
