using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Stores human identities and labels separately from versioned model suggestions.
/// </summary>
public sealed class SqliteIdentityCatalogueRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteIdentityCatalogueRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CataloguePerson> SavePersonAsync(
        CataloguePerson person,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await UpsertPersonAsync(connection, transaction, person, cancellationToken);

        CataloguePerson persisted = await GetPersonAsync(
            connection,
            transaction,
            person.Id,
            cancellationToken)
            ?? throw new InvalidOperationException("The person was not available after it was persisted.");

        transaction.Commit();
        return persisted;
    }

    /// <summary>
    /// Upserts the person and human-authored label in one transaction.
    /// A repeated person/occurrence/label-kind assignment keeps its stable row identity
    /// and refreshes assignment metadata.
    /// </summary>
    public async Task<CatalogueHumanLabel> SaveHumanLabelAsync(
        CataloguePerson person,
        HumanLabelAssignment assignment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(assignment);

        if (assignment.PersonId != person.Id)
        {
            throw new ArgumentException("The human label must refer to the supplied person.", nameof(assignment));
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await UpsertPersonAsync(connection, transaction, person, cancellationToken);
        await UpsertHumanLabelAsync(connection, transaction, assignment, cancellationToken);

        CatalogueHumanLabel persisted = await FindHumanLabelAsync(
            connection,
            transaction,
            assignment.PersonId,
            assignment.FaceOccurrenceId,
            assignment.LabelKind,
            cancellationToken)
            ?? throw new InvalidOperationException("The human label was not available after it was persisted.");

        transaction.Commit();
        return persisted;
    }

    /// <summary>
    /// Inserts a versioned model suggestion or refreshes its score on a rerun.
    /// Review status and original creation time are preserved for an existing model result.
    /// </summary>
    public async Task<CatalogueIdentitySuggestion> SaveSuggestionAsync(
        IdentitySuggestionDraft suggestion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await InsertOrRefreshSuggestionAsync(connection, transaction, suggestion, cancellationToken);
        CatalogueIdentitySuggestion persisted = await FindSuggestionAsync(
            connection,
            transaction,
            suggestion.FaceOccurrenceId,
            suggestion.SuggestedPersonId,
            suggestion.ModelId,
            suggestion.ModelHash,
            cancellationToken)
            ?? throw new InvalidOperationException("The identity suggestion was not available after it was persisted.");

        transaction.Commit();
        return persisted;
    }

    public async Task<CatalogueIdentitySuggestion?> UpdateSuggestionStatusAsync(
        long id,
        string status,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE identity_suggestions SET status = $status WHERE id = $id;";
            command.Parameters.AddWithValue("$status", status.Trim());
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueIdentitySuggestion? persisted = await GetSuggestionAsync(
            connection,
            transaction,
            id,
            cancellationToken);
        transaction.Commit();
        return persisted;
    }

    public async Task<CataloguePerson?> GetPersonAsync(
        PersonId id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await GetPersonAsync(connection, transaction: null, id, cancellationToken);
    }

    public async Task<CatalogueHumanLabel?> GetHumanLabelAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc, note
            FROM person_labels
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHumanLabel(reader) : null;
    }

    public async Task<IReadOnlyList<CatalogueHumanLabel>> GetHumanLabelsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc, note
            FROM person_labels
            WHERE face_occurrence_id = $face_occurrence_id
            ORDER BY assigned_at_utc DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());

        List<CatalogueHumanLabel> labels = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            labels.Add(ReadHumanLabel(reader));
        }

        return labels;
    }

    public async Task<CatalogueIdentitySuggestion?> GetSuggestionAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await GetSuggestionAsync(connection, transaction: null, id, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueIdentitySuggestion>> GetSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, face_occurrence_id, suggested_person_id, model_id, model_hash, score, status, created_at_utc
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_occurrence_id
            ORDER BY created_at_utc DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());

        List<CatalogueIdentitySuggestion> suggestions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            suggestions.Add(ReadSuggestion(reader));
        }

        return suggestions;
    }

    private static async Task UpsertPersonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CataloguePerson person,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES ($id, $display_name, $created_at_utc, $merged_into_person_id)
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                merged_into_person_id = excluded.merged_into_person_id;
            """;
        command.Parameters.AddWithValue("$id", person.Id.ToString());
        command.Parameters.AddWithValue("$display_name", (object?)person.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", Format(person.CreatedAtUtc));
        command.Parameters.AddWithValue(
            "$merged_into_person_id",
            person.MergedIntoPersonId is PersonId mergeTarget ? mergeTarget.ToString() : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertHumanLabelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HumanLabelAssignment assignment,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO person_labels (
                person_id,
                face_occurrence_id,
                label_kind,
                assigned_by,
                assigned_at_utc,
                note)
                VALUES (
                    $person_id,
                    $face_occurrence_id,
                    $label_kind,
                    $assigned_by,
                    $assigned_at_utc,
                    $note)
            ON CONFLICT(person_id, face_occurrence_id, label_kind) DO UPDATE SET
                assigned_by = excluded.assigned_by,
                assigned_at_utc = excluded.assigned_at_utc,
                note = excluded.note;
            """;
        command.Parameters.AddWithValue("$person_id", assignment.PersonId.ToString());
        command.Parameters.AddWithValue("$face_occurrence_id", assignment.FaceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$label_kind", assignment.LabelKind);
        command.Parameters.AddWithValue("$assigned_by", assignment.AssignedBy);
        command.Parameters.AddWithValue("$assigned_at_utc", Format(assignment.AssignedAtUtc));
        command.Parameters.AddWithValue("$note", (object?)assignment.Note ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOrRefreshSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IdentitySuggestionDraft suggestion,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO identity_suggestions (
                face_occurrence_id,
                suggested_person_id,
                model_id,
                model_hash,
                score,
                status,
                created_at_utc)
                VALUES (
                    $face_occurrence_id,
                    $suggested_person_id,
                    $model_id,
                    $model_hash,
                    $score,
                    $status,
                    $created_at_utc)
            ON CONFLICT(face_occurrence_id, suggested_person_id, model_id, model_hash) DO UPDATE SET
                score = excluded.score;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", suggestion.FaceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$suggested_person_id", suggestion.SuggestedPersonId.ToString());
        command.Parameters.AddWithValue("$model_id", suggestion.ModelId.ToString());
        command.Parameters.AddWithValue("$model_hash", suggestion.ModelHash.ToString());
        command.Parameters.AddWithValue("$score", suggestion.Score);
        command.Parameters.AddWithValue("$status", suggestion.InitialStatus);
        command.Parameters.AddWithValue("$created_at_utc", Format(suggestion.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CataloguePerson?> GetPersonAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        PersonId id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, display_name, created_at_utc, merged_into_person_id
            FROM people
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPerson(reader) : null;
    }

    private static async Task<CatalogueHumanLabel?> FindHumanLabelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId personId,
        FaceOccurrenceId faceOccurrenceId,
        string labelKind,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc, note
            FROM person_labels
            WHERE person_id = $person_id
              AND face_occurrence_id = $face_occurrence_id
              AND label_kind = $label_kind;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$label_kind", labelKind);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHumanLabel(reader) : null;
    }

    private static async Task<CatalogueIdentitySuggestion?> FindSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, face_occurrence_id, suggested_person_id, model_id, model_hash, score, status, created_at_utc
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_occurrence_id
              AND suggested_person_id = $suggested_person_id
              AND model_id = $model_id
              AND model_hash = $model_hash;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$suggested_person_id", personId.ToString());
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSuggestion(reader) : null;
    }

    private static async Task<CatalogueIdentitySuggestion?> GetSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, face_occurrence_id, suggested_person_id, model_id, model_hash, score, status, created_at_utc
            FROM identity_suggestions
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSuggestion(reader) : null;
    }

    private static CataloguePerson ReadPerson(SqliteDataReader reader) =>
        new(
            PersonId.From(Guid.Parse(reader.GetString(0))),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            ParseTimestamp(reader.GetString(2)),
            reader.IsDBNull(3) ? null : PersonId.From(Guid.Parse(reader.GetString(3))));

    private static CatalogueHumanLabel ReadHumanLabel(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            PersonId.From(Guid.Parse(reader.GetString(1))),
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(2))),
            reader.GetString(3),
            reader.GetString(4),
            ParseTimestamp(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6));

    private static CatalogueIdentitySuggestion ReadSuggestion(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
            PersonId.From(Guid.Parse(reader.GetString(2))),
            new ModelId(reader.GetString(3)),
            new Sha256Digest(reader.GetString(4)),
            reader.GetDouble(5),
            reader.GetString(6),
            ParseTimestamp(reader.GetString(7)));

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
