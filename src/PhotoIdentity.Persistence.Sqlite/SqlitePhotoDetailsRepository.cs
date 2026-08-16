using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CataloguePhotoDetailsPerson(
    PersonId PersonId,
    string DisplayName,
    int ConfirmedFaceCount,
    bool ManualPresence);

public sealed record CataloguePhotoDetails(
    AssetRevisionId RevisionId,
    string SourceKey,
    IReadOnlyList<CataloguePhotoDetailsPerson> People);

/// <summary>
/// Reads revision-level catalogue details without opening the authoritative original.
/// SourceKey remains server-side input; browser-facing code must reduce it to a file name.
/// </summary>
public sealed class SqlitePhotoDetailsRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqlitePhotoDetailsRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CataloguePhotoDetails?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_action AS (
                SELECT
                    review_actions.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'unknown', 'reject')
                  AND reversed_at_utc IS NULL
            )
            SELECT
                asset_revisions.id,
                assets.source_key,
                confirmed_people.id,
                confirmed_people.display_name,
                COUNT(confirmed_people.id) AS confirmed_face_count
            FROM asset_revisions
            INNER JOIN assets
                ON assets.id = asset_revisions.asset_id
            LEFT JOIN face_occurrences
                ON face_occurrences.asset_revision_id = asset_revisions.id
            LEFT JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
               AND latest_action.action_kind = 'assign'
            LEFT JOIN people AS confirmed_people
                ON confirmed_people.id = latest_action.person_id
               AND confirmed_people.merged_into_person_id IS NULL
            WHERE asset_revisions.id = $revision_id
            GROUP BY
                asset_revisions.id,
                assets.source_key,
                confirmed_people.id,
                confirmed_people.display_name
            ORDER BY
                confirmed_people.display_name,
                confirmed_people.id;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());

        string? sourceKey = null;
        List<CataloguePhotoDetailsPerson> people = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sourceKey ??= reader.GetString(1);
            if (reader.IsDBNull(2))
            {
                continue;
            }

            people.Add(new CataloguePhotoDetailsPerson(
                PersonId.From(Guid.Parse(reader.GetString(2))),
                reader.GetString(3),
                checked((int)reader.GetInt64(4)),
                ManualPresence: false));
        }

        return sourceKey is null
            ? null
            : new CataloguePhotoDetails(revisionId, sourceKey, people);
    }
}
