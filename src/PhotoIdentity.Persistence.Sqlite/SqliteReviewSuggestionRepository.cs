using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Reads model-generated identity suggestions for the review application.
/// Suggestions remain advisory and never create or change human labels.
/// </summary>
public sealed class SqliteReviewSuggestionRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteReviewSuggestionRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<CatalogueReviewIdentitySuggestion>> GetSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                suggestion.id,
                suggestion.suggested_person_id,
                COALESCE(NULLIF(TRIM(person.display_name), ''), 'Unnamed person'),
                suggestion.model_id,
                suggestion.model_hash,
                ranking.rank,
                suggestion.score,
                ranking.score_margin,
                suggestion.status,
                ranking.generated_at_utc
            FROM identity_suggestion_rankings AS ranking
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
            INNER JOIN people AS person
                ON person.id = suggestion.suggested_person_id
            WHERE ranking.face_occurrence_id = $face_occurrence_id
            ORDER BY
                ranking.generated_at_utc DESC,
                suggestion.model_id,
                suggestion.model_hash,
                ranking.rank;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());

        List<CatalogueReviewIdentitySuggestion> suggestions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            suggestions.Add(new CatalogueReviewIdentitySuggestion(
                reader.GetInt64(0),
                new CatalogueReviewPerson(
                    PersonId.From(Guid.Parse(reader.GetString(1))),
                    reader.GetString(2)),
                new ModelId(reader.GetString(3)),
                new Sha256Digest(reader.GetString(4)),
                reader.GetInt32(5),
                reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetString(8),
                Parse(reader.GetString(9))));
        }

        return suggestions;
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
}
