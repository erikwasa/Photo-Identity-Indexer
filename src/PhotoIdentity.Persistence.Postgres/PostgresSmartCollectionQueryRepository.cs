using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresSmartCollectionQueryRepository : ISmartCollectionQueryRepository
{
    private const string CommonCtes = """
        WITH latest_review_action AS (
            SELECT
                review_actions.face_occurrence_id,
                review_actions.action_kind,
                review_actions.person_id,
                ROW_NUMBER() OVER (
                    PARTITION BY review_actions.face_occurrence_id
                    ORDER BY review_actions.id DESC) AS row_number
            FROM review_actions
            WHERE review_actions.action_kind IN ('assign', 'unknown', 'reject')
              AND review_actions.reversed_at_utc IS NULL
        ),
        confirmed_revision_people AS (
            SELECT DISTINCT
                face_occurrences.asset_revision_id AS revision_id,
                latest_review_action.person_id
            FROM face_occurrences
            INNER JOIN latest_review_action
                ON latest_review_action.face_occurrence_id = face_occurrences.id
               AND latest_review_action.row_number = 1
               AND latest_review_action.action_kind = 'assign'
            INNER JOIN people
                ON people.id = latest_review_action.person_id
               AND people.merged_into_person_id IS NULL
        ),
        latest_photo_person_action AS (
            SELECT
                photo_person_actions.asset_revision_id,
                photo_person_actions.person_id,
                photo_person_actions.action_kind,
                ROW_NUMBER() OVER (
                    PARTITION BY photo_person_actions.asset_revision_id, photo_person_actions.person_id
                    ORDER BY photo_person_actions.id DESC) AS row_number
            FROM photo_person_actions
        ),
        active_manual_revision_people AS (
            SELECT
                latest_photo_person_action.asset_revision_id AS revision_id,
                latest_photo_person_action.person_id
            FROM latest_photo_person_action
            INNER JOIN people
                ON people.id = latest_photo_person_action.person_id
               AND people.merged_into_person_id IS NULL
            WHERE latest_photo_person_action.row_number = 1
              AND latest_photo_person_action.action_kind = 'add'
        ),
        revision_people AS (
            SELECT revision_id, person_id FROM confirmed_revision_people
            UNION
            SELECT revision_id, person_id FROM active_manual_revision_people
        ),
        latest_tag_action AS (
            SELECT
                photo_tag_actions.asset_revision_id,
                photo_tag_actions.tag_id,
                photo_tag_actions.action_kind,
                ROW_NUMBER() OVER (
                    PARTITION BY photo_tag_actions.asset_revision_id, photo_tag_actions.tag_id
                    ORDER BY photo_tag_actions.id DESC) AS row_number
            FROM photo_tag_actions
        ),
        active_revision_tags AS (
            SELECT
                latest_tag_action.asset_revision_id AS revision_id,
                photo_tags.normalized_name AS normalized_value
            FROM latest_tag_action
            INNER JOIN photo_tags ON photo_tags.id = latest_tag_action.tag_id
            WHERE latest_tag_action.row_number = 1
              AND latest_tag_action.action_kind = 'add'
              AND photo_tags.normalized_name <> 'places'
              AND photo_tags.normalized_name NOT LIKE 'places/%'
        ),
        latest_place_action AS (
            SELECT
                photo_place_actions.asset_revision_id,
                photo_place_actions.tag_id,
                photo_place_actions.action_kind,
                ROW_NUMBER() OVER (
                    PARTITION BY photo_place_actions.asset_revision_id
                    ORDER BY photo_place_actions.id DESC) AS row_number
            FROM photo_place_actions
        ),
        effective_revision_places AS (
            SELECT
                latest_place_action.asset_revision_id AS revision_id,
                photo_tags.normalized_name AS normalized_value
            FROM latest_place_action
            INNER JOIN photo_tags ON photo_tags.id = latest_place_action.tag_id
            WHERE latest_place_action.row_number = 1
              AND latest_place_action.action_kind = 'set'
        )
        """;

    private readonly PostgresCatalogueDatabase _database;
    private readonly ISmartCollectionRepository _definitions;
    private readonly TimeProvider _timeProvider;

    public PostgresSmartCollectionQueryRepository(
        PostgresCatalogueDatabase database,
        ISmartCollectionRepository definitions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _definitions = definitions;
        _timeProvider = timeProvider;
    }

    public async Task<SmartCollectionPhotoPage> QueryAsync(
        SmartCollectionFilter filter,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Smart-collection page size must be between 1 and 200.");
        }

        string where = BuildWhere(filter);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);

        int total;
        await using (NpgsqlCommand count = connection.CreateCommand())
        {
            count.CommandText = $"""
                {CommonCtes}
                SELECT COUNT(*)
                FROM asset_revisions
                INNER JOIN assets ON assets.id = asset_revisions.asset_id
                LEFT JOIN photo_capture_metadata
                    ON photo_capture_metadata.asset_revision_id = asset_revisions.id
                WHERE assets.deleted_at_utc IS NULL
                  {where};
                """;
            AddFilterParameters(count, filter);
            total = Convert.ToInt32(
                await count.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {CommonCtes}
            SELECT
                asset_revisions.id,
                asset_revisions.asset_id,
                asset_revisions.observed_at_utc,
                asset_revisions.media_type,
                asset_revisions.width,
                asset_revisions.height,
                photo_capture_metadata.taken_at_local,
                photo_capture_metadata.latitude,
                photo_capture_metadata.longitude
            FROM asset_revisions
            INNER JOIN assets ON assets.id = asset_revisions.asset_id
            LEFT JOIN photo_capture_metadata
                ON photo_capture_metadata.asset_revision_id = asset_revisions.id
            WHERE assets.deleted_at_utc IS NULL
              {where}
            ORDER BY
                photo_capture_metadata.taken_at_local DESC,
                asset_revisions.observed_at_utc DESC,
                asset_revisions.id
            LIMIT @limit OFFSET @offset;
            """;
        AddFilterParameters(command, filter);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        command.Parameters.AddWithValue("offset", NpgsqlDbType.Integer, offset);

        List<SmartCollectionPhoto> items = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadPhoto(reader));
        }

        return new SmartCollectionPhotoPage(items, offset, limit, total, filter);
    }

    public async Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
        SmartCollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        SmartCollectionDefinition? definition =
            await _definitions.GetAsync(collectionId, cancellationToken);
        if (definition is null)
        {
            return null;
        }

        string where = BuildWhere(definition.Filter);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {CommonCtes}
            SELECT
                asset_revisions.id,
                asset_revisions.observed_at_utc,
                photo_capture_metadata.taken_at_local
            FROM asset_revisions
            INNER JOIN assets ON assets.id = asset_revisions.asset_id
            LEFT JOIN photo_capture_metadata
                ON photo_capture_metadata.asset_revision_id = asset_revisions.id
            WHERE assets.deleted_at_utc IS NULL
              {where};
            """;
        AddFilterParameters(command, definition.Filter);

        List<SlideshowSnapshotCandidate> candidates = [];
        await using (NpgsqlDataReader reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new SlideshowSnapshotCandidate(
                    AssetRevisionId.From(reader.GetGuid(0)),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2)));
            }
        }

        AssetRevisionId[] revisionIds = candidates
            .OrderBy(candidate => EffectiveSlideshowTime(candidate))
            .ThenBy(candidate => candidate.RevisionId.ToString(), StringComparer.Ordinal)
            .Select(candidate => candidate.RevisionId)
            .ToArray();

        return new SmartCollectionSlideshowSnapshot(
            definition.Id,
            definition.Name,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            revisionIds);
    }

    private static SmartCollectionPhoto ReadPhoto(NpgsqlDataReader reader) => new(
        AssetRevisionId.From(reader.GetGuid(0)),
        AssetId.From(reader.GetGuid(1)),
        reader.GetFieldValue<DateTimeOffset>(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        reader.IsDBNull(7) ? null : reader.GetDouble(7),
        reader.IsDBNull(8) ? null : reader.GetDouble(8));

    private static DateTime EffectiveSlideshowTime(SlideshowSnapshotCandidate candidate) =>
        candidate.TakenAtLocal
        ?? DateTime.SpecifyKind(candidate.ObservedAtUtc.UtcDateTime, DateTimeKind.Unspecified);

    private static string BuildWhere(SmartCollectionFilter filter)
    {
        List<string> predicates = [];

        if (filter.People.Count > 0)
        {
            string people = string.Join(", ", Enumerable.Range(0, filter.People.Count).Select(index => $"@person_{index}"));
            string having = filter.PeopleMatch == SmartCollectionMatchModes.All
                ? "COUNT(DISTINCT person_id) = @person_count"
                : "COUNT(DISTINCT person_id) >= 1";
            predicates.Add($"""
                AND asset_revisions.id IN (
                    SELECT revision_id
                    FROM revision_people
                    WHERE person_id IN ({people})
                    GROUP BY revision_id
                    HAVING {having})
                """);
        }

        if (filter.Tags.Count > 0)
        {
            string tags = string.Join(", ", Enumerable.Range(0, filter.Tags.Count).Select(index => $"@tag_{index}"));
            string having = filter.TagMatch == SmartCollectionMatchModes.All
                ? "COUNT(DISTINCT normalized_value) = @tag_count"
                : "COUNT(DISTINCT normalized_value) >= 1";
            predicates.Add($"""
                AND asset_revisions.id IN (
                    SELECT revision_id
                    FROM active_revision_tags
                    WHERE normalized_value IN ({tags})
                    GROUP BY revision_id
                    HAVING {having})
                """);
        }

        if (filter.LocationPlace is not null)
        {
            predicates.Add("""
                AND EXISTS (
                    SELECT 1
                    FROM effective_revision_places
                    WHERE effective_revision_places.revision_id = asset_revisions.id
                      AND (
                          effective_revision_places.normalized_value = @location_place
                          OR substr(
                              effective_revision_places.normalized_value,
                              1,
                              length(@location_place) + 1) = @location_place || '/'))
                """);
        }

        if (filter.Location is not null)
        {
            predicates.Add("AND photo_capture_metadata.latitude BETWEEN @south AND @north");
            predicates.Add("AND photo_capture_metadata.longitude BETWEEN @west AND @east");
        }

        if (filter.Taken is not null)
        {
            predicates.Add("AND photo_capture_metadata.taken_at_local >= @taken_from");
            predicates.Add("AND photo_capture_metadata.taken_at_local <= @taken_to");
        }

        return predicates.Count == 0 ? string.Empty : string.Join(Environment.NewLine, predicates);
    }

    private static void AddFilterParameters(NpgsqlCommand command, SmartCollectionFilter filter)
    {
        for (int index = 0; index < filter.People.Count; index++)
        {
            command.Parameters.AddWithValue($"person_{index}", NpgsqlDbType.Uuid, filter.People[index].Value);
        }
        command.Parameters.AddWithValue("person_count", NpgsqlDbType.Integer, filter.People.Count);

        for (int index = 0; index < filter.Tags.Count; index++)
        {
            command.Parameters.AddWithValue($"tag_{index}", NpgsqlDbType.Text, filter.Tags[index]);
        }
        command.Parameters.AddWithValue("tag_count", NpgsqlDbType.Integer, filter.Tags.Count);

        if (filter.LocationPlace is not null)
        {
            command.Parameters.AddWithValue("location_place", NpgsqlDbType.Text, filter.LocationPlace);
        }

        if (filter.Location is not null)
        {
            command.Parameters.AddWithValue("south", NpgsqlDbType.Double, filter.Location.South);
            command.Parameters.AddWithValue("west", NpgsqlDbType.Double, filter.Location.West);
            command.Parameters.AddWithValue("north", NpgsqlDbType.Double, filter.Location.North);
            command.Parameters.AddWithValue("east", NpgsqlDbType.Double, filter.Location.East);
        }

        if (filter.Taken is not null)
        {
            command.Parameters.AddWithValue("taken_from", NpgsqlDbType.Timestamp, FormatDateStart(filter.Taken.From));
            command.Parameters.AddWithValue("taken_to", NpgsqlDbType.Timestamp, FormatDateEnd(filter.Taken.To));
        }
    }

    private static DateTime FormatDateStart(DateOnly value) =>
        value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

    private static DateTime FormatDateEnd(DateOnly value) =>
        value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);

    private sealed record SlideshowSnapshotCandidate(
        AssetRevisionId RevisionId,
        DateTimeOffset ObservedAtUtc,
        DateTime? TakenAtLocal);
}
