using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public static class CatalogueCollectionMatchModes
{
    public const string Any = "any";
    public const string All = "all";
}

public sealed record CatalogueCollectionPersonMatch(
    PersonId PersonId,
    string DisplayName,
    int ConfirmedFaceCount);

public sealed record CatalogueCollectionPhoto(
    AssetRevisionId RevisionId,
    AssetId AssetId,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    IReadOnlyList<CatalogueCollectionPersonMatch> People);

public sealed record CatalogueCollectionPhotoPage(
    IReadOnlyList<CatalogueCollectionPhoto> Items,
    int Offset,
    int Limit,
    int Total,
    string MatchMode);

/// <summary>
/// Queries path-free photo manifests from active, confirmed human assignments.
/// </summary>
public sealed class SqliteCollectionQueryRepository
{
    private const string AssignedFaceCtes = """
        WITH latest_action AS (
            SELECT
                review_actions.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY id DESC) AS row_number
            FROM review_actions
            WHERE action_kind IN ('assign', 'reject')
              AND reversed_at_utc IS NULL
        ),
        latest_observation AS (
            SELECT
                face_observations.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY observed_at_utc DESC, detector_model_id, detector_model_hash) AS row_number
            FROM face_observations
        ),
        matched_faces AS (
            SELECT
                asset_revisions.id AS revision_id,
                asset_revisions.asset_id,
                asset_revisions.observed_at_utc,
                asset_revisions.media_type,
                asset_revisions.width,
                asset_revisions.height,
                face_occurrences.id AS face_id,
                latest_action.person_id,
                people.display_name
            FROM asset_revisions
            INNER JOIN face_occurrences
                ON face_occurrences.asset_revision_id = asset_revisions.id
            INNER JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
               AND latest_action.action_kind = 'assign'
            INNER JOIN people
                ON people.id = latest_action.person_id
            LEFT JOIN latest_observation
                ON latest_observation.face_occurrence_id = face_occurrences.id
               AND latest_observation.row_number = 1
            WHERE latest_action.person_id IN ({0})
              AND ($from_utc IS NULL OR asset_revisions.observed_at_utc >= $from_utc)
              AND ($to_utc IS NULL OR asset_revisions.observed_at_utc <= $to_utc)
              AND ($min_confidence IS NULL OR latest_observation.confidence >= $min_confidence)
        ),
        matched_revisions AS (
            SELECT
                revision_id,
                asset_id,
                observed_at_utc,
                media_type,
                width,
                height
            FROM matched_faces
            GROUP BY
                revision_id,
                asset_id,
                observed_at_utc,
                media_type,
                width,
                height
            HAVING {1}
        )
        """;

    private readonly SqliteCatalogueDatabase _database;

    public SqliteCollectionQueryRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueCollectionPhotoPage> QueryConfirmedPhotosAsync(
        IReadOnlyCollection<PersonId> personIds,
        string matchMode = CatalogueCollectionMatchModes.All,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        double? minimumConfidence = null,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Collection page size must be between 1 and 200.");
        }

        PersonId[] distinctPeople = personIds.Distinct().ToArray();
        if (distinctPeople.Length is < 1 or > 100)
        {
            throw new ArgumentException("Between 1 and 100 distinct people must be supplied.", nameof(personIds));
        }

        string normalizedMatchMode = NormalizeMatchMode(matchMode);
        if (fromUtc is not null && toUtc is not null && fromUtc > toUtc)
        {
            throw new ArgumentException("The collection start date cannot be later than the end date.");
        }

        if (minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumConfidence),
                "Minimum confidence must be between 0 and 1.");
        }

        string[] personParameters = distinctPeople
            .Select((_, index) => $"$person_{index}")
            .ToArray();
        string having = normalizedMatchMode == CatalogueCollectionMatchModes.All
            ? "COUNT(DISTINCT person_id) = $person_count"
            : "COUNT(DISTINCT person_id) >= 1";
        string ctes = string.Format(
            CultureInfo.InvariantCulture,
            AssignedFaceCtes,
            string.Join(", ", personParameters),
            having);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);

        int total;
        using (SqliteCommand countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"""
                {ctes}
                SELECT COUNT(*)
                FROM matched_revisions;
                """;
            AddParameters(
                countCommand,
                distinctPeople,
                fromUtc,
                toUtc,
                minimumConfidence);
            total = Convert.ToInt32(
                await countCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ctes},
            paged_revisions AS (
                SELECT *
                FROM matched_revisions
                ORDER BY observed_at_utc DESC, revision_id
                LIMIT $limit OFFSET $offset
            )
            SELECT
                paged_revisions.revision_id,
                paged_revisions.asset_id,
                paged_revisions.observed_at_utc,
                paged_revisions.media_type,
                paged_revisions.width,
                paged_revisions.height,
                matched_faces.person_id,
                matched_faces.display_name,
                COUNT(DISTINCT matched_faces.face_id) AS confirmed_face_count
            FROM paged_revisions
            INNER JOIN matched_faces
                ON matched_faces.revision_id = paged_revisions.revision_id
            GROUP BY
                paged_revisions.revision_id,
                paged_revisions.asset_id,
                paged_revisions.observed_at_utc,
                paged_revisions.media_type,
                paged_revisions.width,
                paged_revisions.height,
                matched_faces.person_id,
                matched_faces.display_name
            ORDER BY
                paged_revisions.observed_at_utc DESC,
                paged_revisions.revision_id,
                matched_faces.display_name,
                matched_faces.person_id;
            """;
        AddParameters(command, distinctPeople, fromUtc, toUtc, minimumConfidence);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        List<CatalogueCollectionPhoto> items = [];
        CatalogueCollectionPhotoBuilder? current = null;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AssetRevisionId revisionId = AssetRevisionId.From(Guid.Parse(reader.GetString(0)));
            if (current is null || current.RevisionId != revisionId)
            {
                if (current is not null)
                {
                    items.Add(current.Build());
                }

                current = new CatalogueCollectionPhotoBuilder(
                    revisionId,
                    AssetId.From(Guid.Parse(reader.GetString(1))),
                    Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5));
            }

            current.People.Add(new CatalogueCollectionPersonMatch(
                PersonId.From(Guid.Parse(reader.GetString(6))),
                reader.GetString(7),
                reader.GetInt32(8)));
        }

        if (current is not null)
        {
            items.Add(current.Build());
        }

        return new CatalogueCollectionPhotoPage(items, offset, limit, total, normalizedMatchMode);
    }

    private static void AddParameters(
        SqliteCommand command,
        IReadOnlyList<PersonId> personIds,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        double? minimumConfidence)
    {
        for (int index = 0; index < personIds.Count; index++)
        {
            command.Parameters.AddWithValue($"$person_{index}", personIds[index].ToString());
        }

        command.Parameters.AddWithValue("$person_count", personIds.Count);
        command.Parameters.AddWithValue(
            "$from_utc",
            fromUtc is null ? DBNull.Value : fromUtc.Value.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$to_utc",
            toUtc is null ? DBNull.Value : toUtc.Value.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$min_confidence",
            minimumConfidence is null ? DBNull.Value : minimumConfidence.Value);
    }

    private static string NormalizeMatchMode(string matchMode)
    {
        string normalized = string.IsNullOrWhiteSpace(matchMode)
            ? CatalogueCollectionMatchModes.All
            : matchMode.Trim().ToLowerInvariant();
        return normalized switch
        {
            CatalogueCollectionMatchModes.Any => normalized,
            CatalogueCollectionMatchModes.All => normalized,
            _ => throw new ArgumentException(
                $"Unsupported collection match mode '{matchMode}'. Use 'any' or 'all'.",
                nameof(matchMode)),
        };
    }

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);

    private sealed class CatalogueCollectionPhotoBuilder
    {
        public CatalogueCollectionPhotoBuilder(
            AssetRevisionId revisionId,
            AssetId assetId,
            DateTimeOffset observedAtUtc,
            string? mediaType,
            int? width,
            int? height)
        {
            RevisionId = revisionId;
            AssetId = assetId;
            ObservedAtUtc = observedAtUtc;
            MediaType = mediaType;
            Width = width;
            Height = height;
        }

        public AssetRevisionId RevisionId { get; }
        public AssetId AssetId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string? MediaType { get; }
        public int? Width { get; }
        public int? Height { get; }
        public List<CatalogueCollectionPersonMatch> People { get; } = [];

        public CatalogueCollectionPhoto Build() => new(
            RevisionId,
            AssetId,
            ObservedAtUtc,
            MediaType,
            Width,
            Height,
            People.ToArray());
    }
}
