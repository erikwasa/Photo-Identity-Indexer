using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresCollectionQueryRepository : ICollectionQueryRepository
{
    private const string MatchingFaceCtes = """
        WITH latest_action AS (
            SELECT
                review_actions.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY id DESC) AS row_number
            FROM review_actions
            WHERE action_kind IN ('assign', 'unknown', 'reject')
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
        top_suggestion AS (
            SELECT
                rankings.face_occurrence_id,
                suggestions.suggested_person_id,
                suggested_people.display_name,
                suggestions.score
            FROM identity_suggestion_rankings AS rankings
            INNER JOIN identity_suggestions AS suggestions
                ON suggestions.id = rankings.suggestion_id
            INNER JOIN people AS suggested_people
                ON suggested_people.id = suggestions.suggested_person_id
               AND suggested_people.merged_into_person_id IS NULL
            WHERE rankings.rank = 1
              AND suggestions.status = 'pending'
              AND rankings.model_id = @suggestion_model_id
              AND rankings.model_hash = @suggestion_model_hash
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
                confirmed_people.display_name,
                1 AS confirmed_evidence,
                0 AS suggested_evidence,
                NULL::double precision AS suggestion_score
            FROM asset_revisions
            INNER JOIN face_occurrences
                ON face_occurrences.asset_revision_id = asset_revisions.id
            INNER JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
               AND latest_action.action_kind = 'assign'
            INNER JOIN people AS confirmed_people
                ON confirmed_people.id = latest_action.person_id
               AND confirmed_people.merged_into_person_id IS NULL
            LEFT JOIN latest_observation
                ON latest_observation.face_occurrence_id = face_occurrences.id
               AND latest_observation.row_number = 1
            WHERE @include_assigned
              AND latest_action.person_id IN ({0})
              AND (@from_utc IS NULL OR asset_revisions.observed_at_utc >= @from_utc)
              AND (@to_utc IS NULL OR asset_revisions.observed_at_utc <= @to_utc)
              AND (@min_confidence IS NULL OR latest_observation.confidence >= @min_confidence)

            UNION ALL

            SELECT
                asset_revisions.id AS revision_id,
                asset_revisions.asset_id,
                asset_revisions.observed_at_utc,
                asset_revisions.media_type,
                asset_revisions.width,
                asset_revisions.height,
                face_occurrences.id AS face_id,
                top_suggestion.suggested_person_id AS person_id,
                top_suggestion.display_name,
                0 AS confirmed_evidence,
                1 AS suggested_evidence,
                top_suggestion.score AS suggestion_score
            FROM asset_revisions
            INNER JOIN face_occurrences
                ON face_occurrences.asset_revision_id = asset_revisions.id
            LEFT JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
            INNER JOIN top_suggestion
                ON top_suggestion.face_occurrence_id = face_occurrences.id
            LEFT JOIN latest_observation
                ON latest_observation.face_occurrence_id = face_occurrences.id
               AND latest_observation.row_number = 1
            WHERE @include_unreviewed
              AND latest_action.id IS NULL
              AND top_suggestion.suggested_person_id IN ({0})
              AND top_suggestion.score >= @min_suggestion_score
              AND (@from_utc IS NULL OR asset_revisions.observed_at_utc >= @from_utc)
              AND (@to_utc IS NULL OR asset_revisions.observed_at_utc <= @to_utc)
              AND (@min_confidence IS NULL OR latest_observation.confidence >= @min_confidence)
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

    private readonly PostgresCatalogueDatabase _database;

    public PostgresCollectionQueryRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CollectionPhotoPage> QueryPhotosAsync(
        IReadOnlyCollection<PersonId> personIds,
        string matchMode = CollectionMatchModes.All,
        CollectionSuggestionPolicy? suggestionPolicy = null,
        string? reviewState = null,
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

        if (suggestionPolicy is not null &&
            (double.IsNaN(suggestionPolicy.MinimumScore) ||
             double.IsInfinity(suggestionPolicy.MinimumScore) ||
             suggestionPolicy.MinimumScore is < -1 or > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(suggestionPolicy),
                "Minimum suggestion score must be a finite cosine similarity between -1 and 1.");
        }

        string normalizedReviewState = NormalizeReviewState(reviewState, suggestionPolicy);
        string[] personParameters = distinctPeople
            .Select((_, index) => $"@person_{index}")
            .ToArray();
        string having = normalizedMatchMode == CollectionMatchModes.All
            ? "COUNT(DISTINCT person_id) = @person_count"
            : "COUNT(DISTINCT person_id) >= 1";
        string ctes = string.Format(
            CultureInfo.InvariantCulture,
            MatchingFaceCtes,
            string.Join(", ", personParameters),
            having);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);

        int total;
        await using (NpgsqlCommand countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"""
                {ctes}
                SELECT COUNT(*)
                FROM matched_revisions;
                """;
            AddParameters(
                countCommand,
                distinctPeople,
                suggestionPolicy,
                normalizedReviewState,
                fromUtc,
                toUtc,
                minimumConfidence);
            total = Convert.ToInt32(
                await countCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ctes},
            paged_revisions AS (
                SELECT *
                FROM matched_revisions
                ORDER BY observed_at_utc DESC, revision_id
                LIMIT @limit OFFSET @offset
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
                SUM(matched_faces.confirmed_evidence) AS confirmed_face_count,
                SUM(matched_faces.suggested_evidence) AS suggested_face_count,
                MAX(matched_faces.suggestion_score) AS maximum_suggestion_score
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
                lower(matched_faces.display_name),
                matched_faces.person_id;
            """;
        AddParameters(
            command,
            distinctPeople,
            suggestionPolicy,
            normalizedReviewState,
            fromUtc,
            toUtc,
            minimumConfidence);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        List<CollectionPhoto> items = [];
        CollectionPhotoBuilder? current = null;
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AssetRevisionId revisionId = AssetRevisionId.From(reader.GetGuid(0));
            if (current is null || current.RevisionId != revisionId)
            {
                if (current is not null)
                {
                    items.Add(current.Build());
                }

                current = new CollectionPhotoBuilder(
                    revisionId,
                    AssetId.From(reader.GetGuid(1)),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5));
            }

            current.People.Add(new CollectionPersonMatch(
                PersonId.From(reader.GetGuid(6)),
                reader.GetString(7),
                checked((int)reader.GetInt64(8)),
                checked((int)reader.GetInt64(9)),
                reader.IsDBNull(10) ? null : reader.GetDouble(10)));
        }

        if (current is not null)
        {
            items.Add(current.Build());
        }

        return new CollectionPhotoPage(
            items,
            offset,
            limit,
            total,
            normalizedMatchMode,
            normalizedReviewState,
            suggestionPolicy);
    }

    private static void AddParameters(
        NpgsqlCommand command,
        IReadOnlyList<PersonId> personIds,
        CollectionSuggestionPolicy? suggestionPolicy,
        string reviewState,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        double? minimumConfidence)
    {
        for (int index = 0; index < personIds.Count; index++)
        {
            command.Parameters.AddWithValue($"person_{index}", NpgsqlDbType.Uuid, Guid.Parse(personIds[index].ToString()));
        }

        command.Parameters.AddWithValue("person_count", NpgsqlDbType.Integer, personIds.Count);
        command.Parameters.AddWithValue(
            "include_assigned",
            NpgsqlDbType.Boolean,
            reviewState is CollectionReviewStates.Assigned or CollectionReviewStates.All);
        command.Parameters.AddWithValue(
            "include_unreviewed",
            NpgsqlDbType.Boolean,
            reviewState is CollectionReviewStates.Unreviewed or CollectionReviewStates.All);

        NpgsqlParameter modelId = command.Parameters.Add("suggestion_model_id", NpgsqlDbType.Text);
        modelId.Value = suggestionPolicy is null ? DBNull.Value : suggestionPolicy.ModelId.ToString();
        NpgsqlParameter modelHash = command.Parameters.Add("suggestion_model_hash", NpgsqlDbType.Text);
        modelHash.Value = suggestionPolicy is null ? DBNull.Value : suggestionPolicy.ModelHash.ToString();
        NpgsqlParameter suggestionScore = command.Parameters.Add("min_suggestion_score", NpgsqlDbType.Double);
        suggestionScore.Value = suggestionPolicy is null ? DBNull.Value : suggestionPolicy.MinimumScore;
        NpgsqlParameter from = command.Parameters.Add("from_utc", NpgsqlDbType.TimestampTz);
        from.Value = fromUtc is null ? DBNull.Value : fromUtc.Value.ToUniversalTime();
        NpgsqlParameter to = command.Parameters.Add("to_utc", NpgsqlDbType.TimestampTz);
        to.Value = toUtc is null ? DBNull.Value : toUtc.Value.ToUniversalTime();
        NpgsqlParameter confidence = command.Parameters.Add("min_confidence", NpgsqlDbType.Double);
        confidence.Value = minimumConfidence is null ? DBNull.Value : minimumConfidence.Value;
    }

    private static string NormalizeMatchMode(string matchMode)
    {
        string normalized = string.IsNullOrWhiteSpace(matchMode)
            ? CollectionMatchModes.All
            : matchMode.Trim().ToLowerInvariant();
        return normalized switch
        {
            CollectionMatchModes.Any => normalized,
            CollectionMatchModes.All => normalized,
            _ => throw new ArgumentException(
                $"Unsupported collection match mode '{matchMode}'. Use 'any' or 'all'.",
                nameof(matchMode)),
        };
    }

    private static string NormalizeReviewState(
        string? reviewState,
        CollectionSuggestionPolicy? suggestionPolicy)
    {
        string normalized = string.IsNullOrWhiteSpace(reviewState)
            ? suggestionPolicy is null
                ? CollectionReviewStates.Assigned
                : CollectionReviewStates.All
            : reviewState.Trim().ToLowerInvariant();

        if (normalized is not (
            CollectionReviewStates.Assigned or
            CollectionReviewStates.Unreviewed or
            CollectionReviewStates.All))
        {
            throw new ArgumentException(
                $"Unsupported collection review state '{reviewState}'. Use 'assigned', 'unreviewed' or 'all'.",
                nameof(reviewState));
        }

        if (normalized == CollectionReviewStates.Assigned && suggestionPolicy is not null)
        {
            throw new ArgumentException(
                "The 'assigned' review state cannot be combined with suggestion parameters. " +
                "Omit 'includeSuggestions' or use reviewState=unreviewed/all.",
                nameof(reviewState));
        }

        if (normalized != CollectionReviewStates.Assigned && suggestionPolicy is null)
        {
            throw new ArgumentException(
                $"The '{normalized}' review state requires includeSuggestions=true with exact model and threshold parameters.",
                nameof(reviewState));
        }

        return normalized;
    }

    private sealed class CollectionPhotoBuilder
    {
        public CollectionPhotoBuilder(
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
        public List<CollectionPersonMatch> People { get; } = [];

        public CollectionPhoto Build() => new(
            RevisionId,
            AssetId,
            ObservedAtUtc,
            MediaType,
            Width,
            Height,
            People.ToArray());
    }
}
