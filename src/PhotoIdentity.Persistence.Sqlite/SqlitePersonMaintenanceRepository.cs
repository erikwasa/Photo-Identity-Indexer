using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Maintains canonical people with append-only audit history.
/// Renames are reversible by applying another audited rename. Merges are explicitly irreversible.
/// </summary>
public sealed class SqlitePersonMaintenanceRepository : IPersonMaintenanceRepository
{
    private const string RenameAction = "rename";
    private const string MergeAction = "merge";

    private readonly SqliteCatalogueDatabase _database;

    public SqlitePersonMaintenanceRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    async Task<IReadOnlyList<PersonMaintenancePerson>>
        IPersonMaintenanceRepository.GetPeopleAsync(
            CancellationToken cancellationToken) =>
        (await GetPeopleAsync(cancellationToken))
            .Select(ToCorePerson)
            .ToArray();

    async Task<IReadOnlyList<PersonMaintenanceAction>>
        IPersonMaintenanceRepository.GetHistoryAsync(
            int limit,
            CancellationToken cancellationToken) =>
        (await GetHistoryAsync(limit, cancellationToken))
            .Select(ToCoreAction)
            .ToArray();

    async Task<PersonMaintenanceAction>
        IPersonMaintenanceRepository.RenameAsync(
            PersonId personId,
            string displayName,
            string actor,
            DateTimeOffset createdAtUtc,
            string? note,
            CancellationToken cancellationToken) =>
        ToCoreAction(await RenameAsync(
            personId,
            displayName,
            actor,
            createdAtUtc,
            note,
            cancellationToken));

    async Task<PersonMaintenanceAction>
        IPersonMaintenanceRepository.MergeAsync(
            PersonId sourcePersonId,
            PersonId targetPersonId,
            bool confirmIrreversible,
            string actor,
            DateTimeOffset createdAtUtc,
            string? note,
            CancellationToken cancellationToken) =>
        ToCoreAction(await MergeAsync(
            sourcePersonId,
            targetPersonId,
            confirmIrreversible,
            actor,
            createdAtUtc,
            note,
            cancellationToken));

    public async Task<IReadOnlyList<CataloguePersonMaintenancePerson>> GetPeopleAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                person.id,
                person.display_name,
                (SELECT COUNT(*) FROM person_labels AS label WHERE label.person_id = person.id),
                (SELECT COUNT(*) FROM identity_suggestions AS suggestion
                    WHERE suggestion.suggested_person_id = person.id)
            FROM people AS person
            WHERE person.merged_into_person_id IS NULL
              AND person.display_name IS NOT NULL
              AND TRIM(person.display_name) <> ''
            ORDER BY person.display_name COLLATE NOCASE, person.id;
            """;

        List<CataloguePersonMaintenancePerson> people = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            people.Add(new CataloguePersonMaintenancePerson(
                PersonId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                Convert.ToInt32(reader.GetInt64(2), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetInt64(3), CultureInfo.InvariantCulture)));
        }

        return people;
    }

    public async Task<IReadOnlyList<CataloguePersonMaintenanceAction>> GetHistoryAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Person history limit must be between 1 and 500.");
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                action_kind,
                person_id,
                previous_display_name,
                target_person_id,
                new_display_name,
                actor,
                note,
                created_at_utc,
                reversible
            FROM person_maintenance_actions
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        List<CataloguePersonMaintenanceAction> actions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(ReadAction(reader));
        }

        return actions;
    }

    public async Task<CataloguePersonMaintenanceAction> RenameAsync(
        PersonId personId,
        string displayName,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeDisplayName(displayName);
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        string previousName = await RequireActivePersonAsync(
            connection,
            transaction,
            personId,
            cancellationToken);
        if (string.Equals(previousName, normalizedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The person already uses that display name.");
        }

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE people
                SET display_name = $display_name
                WHERE id = $person_id
                  AND merged_into_person_id IS NULL;
                """;
            update.Parameters.AddWithValue("$display_name", normalizedName);
            update.Parameters.AddWithValue("$person_id", personId.ToString());
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The person changed before the rename could be saved.");
            }
        }

        long actionId = await InsertActionAsync(
            connection,
            transaction,
            RenameAction,
            personId,
            previousName,
            targetPersonId: null,
            normalizedName,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            reversible: true,
            cancellationToken);
        transaction.Commit();

        return new CataloguePersonMaintenanceAction(
            actionId,
            RenameAction,
            personId,
            previousName,
            TargetPersonId: null,
            normalizedName,
            normalizedActor,
            normalizedNote,
            createdAtUtc.ToUniversalTime(),
            Reversible: true);
    }

    public async Task<CataloguePersonMaintenanceAction> MergeAsync(
        PersonId sourcePersonId,
        PersonId targetPersonId,
        bool confirmIrreversible,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (!confirmIrreversible)
        {
            throw new ArgumentException(
                "Person merge is irreversible and requires explicit confirmation.",
                nameof(confirmIrreversible));
        }

        if (sourcePersonId == targetPersonId)
        {
            throw new ArgumentException("A person cannot be merged into itself.", nameof(targetPersonId));
        }

        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        string sourceName = await RequireActivePersonAsync(
            connection,
            transaction,
            sourcePersonId,
            cancellationToken);
        string targetName = await RequireActivePersonAsync(
            connection,
            transaction,
            targetPersonId,
            cancellationToken);

        await ConsolidateLabelsAsync(
            connection,
            transaction,
            sourcePersonId,
            targetPersonId,
            cancellationToken);
        await ConsolidateSuggestionsAsync(
            connection,
            transaction,
            sourcePersonId,
            targetPersonId,
            cancellationToken);
        await ConsolidateFavoritesAsync(
            connection,
            transaction,
            sourcePersonId,
            targetPersonId,
            createdAtUtc,
            cancellationToken);

        using (SqliteCommand merge = connection.CreateCommand())
        {
            merge.Transaction = transaction;
            merge.CommandText = """
                UPDATE people
                SET merged_into_person_id = $target_person_id
                WHERE id = $source_person_id
                  AND merged_into_person_id IS NULL;
                """;
            merge.Parameters.AddWithValue("$source_person_id", sourcePersonId.ToString());
            merge.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
            if (await merge.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("The source person changed before the merge could be saved.");
            }
        }

        long actionId = await InsertActionAsync(
            connection,
            transaction,
            MergeAction,
            sourcePersonId,
            sourceName,
            targetPersonId,
            targetName,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            reversible: false,
            cancellationToken);
        transaction.Commit();

        return new CataloguePersonMaintenanceAction(
            actionId,
            MergeAction,
            sourcePersonId,
            sourceName,
            targetPersonId,
            targetName,
            normalizedActor,
            normalizedNote,
            createdAtUtc.ToUniversalTime(),
            Reversible: false);
    }

    private static async Task ConsolidateLabelsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId sourcePersonId,
        PersonId targetPersonId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE review_actions
            SET person_id = $target_person_id,
                person_label_id = (
                    SELECT target_label.id
                    FROM person_labels AS source_label
                    INNER JOIN person_labels AS target_label
                        ON target_label.person_id = $target_person_id
                       AND target_label.face_occurrence_id = source_label.face_occurrence_id
                       AND target_label.label_kind = source_label.label_kind
                    WHERE source_label.id = review_actions.person_label_id
                      AND source_label.person_id = $source_person_id)
            WHERE review_actions.person_id = $source_person_id
              AND review_actions.person_label_id IN (
                    SELECT source_label.id
                    FROM person_labels AS source_label
                    INNER JOIN person_labels AS target_label
                        ON target_label.person_id = $target_person_id
                       AND target_label.face_occurrence_id = source_label.face_occurrence_id
                       AND target_label.label_kind = source_label.label_kind
                    WHERE source_label.person_id = $source_person_id);

            DELETE FROM person_labels
            WHERE person_id = $source_person_id
              AND EXISTS (
                    SELECT 1
                    FROM person_labels AS target_label
                    WHERE target_label.person_id = $target_person_id
                      AND target_label.face_occurrence_id = person_labels.face_occurrence_id
                      AND target_label.label_kind = person_labels.label_kind);

            UPDATE person_labels
            SET person_id = $target_person_id
            WHERE person_id = $source_person_id;

            UPDATE review_actions
            SET person_id = $target_person_id
            WHERE person_id = $source_person_id;
            """;
        command.Parameters.AddWithValue("$source_person_id", sourcePersonId.ToString());
        command.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ConsolidateSuggestionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId sourcePersonId,
        PersonId targetPersonId,
        CancellationToken cancellationToken)
    {
        List<SuggestionRow> sourceSuggestions = [];
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT id, face_occurrence_id, model_id, model_hash, status
                FROM identity_suggestions
                WHERE suggested_person_id = $source_person_id
                ORDER BY id;
                """;
            read.Parameters.AddWithValue("$source_person_id", sourcePersonId.ToString());
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sourceSuggestions.Add(new SuggestionRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        foreach (SuggestionRow source in sourceSuggestions)
        {
            long? targetSuggestionId = null;
            string? targetStatus = null;
            using (SqliteCommand findTarget = connection.CreateCommand())
            {
                findTarget.Transaction = transaction;
                findTarget.CommandText = """
                    SELECT id, status
                    FROM identity_suggestions
                    WHERE face_occurrence_id = $face_occurrence_id
                      AND suggested_person_id = $target_person_id
                      AND model_id = $model_id
                      AND model_hash = $model_hash;
                    """;
                findTarget.Parameters.AddWithValue("$face_occurrence_id", source.FaceOccurrenceId);
                findTarget.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
                findTarget.Parameters.AddWithValue("$model_id", source.ModelId);
                findTarget.Parameters.AddWithValue("$model_hash", source.ModelHash);
                await using SqliteDataReader reader = await findTarget.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    targetSuggestionId = reader.GetInt64(0);
                    targetStatus = reader.GetString(1);
                }
            }

            if (targetSuggestionId is not long targetId)
            {
                using SqliteCommand move = connection.CreateCommand();
                move.Transaction = transaction;
                move.CommandText = """
                    UPDATE identity_suggestions
                    SET suggested_person_id = $target_person_id
                    WHERE id = $source_suggestion_id;
                    """;
                move.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
                move.Parameters.AddWithValue("$source_suggestion_id", source.Id);
                await move.ExecuteNonQueryAsync(cancellationToken);
                continue;
            }

            using (SqliteCommand combine = connection.CreateCommand())
            {
                combine.Transaction = transaction;
                combine.CommandText = """
                    UPDATE identity_suggestions
                    SET status = $status
                    WHERE id = $target_suggestion_id;

                    UPDATE identity_suggestion_review_actions
                    SET suggestion_id = $target_suggestion_id
                    WHERE suggestion_id = $source_suggestion_id;

                    UPDATE identity_suggestion_rankings
                    SET suggestion_id = $target_suggestion_id
                    WHERE suggestion_id = $source_suggestion_id
                      AND NOT EXISTS (
                          SELECT 1
                          FROM identity_suggestion_rankings
                          WHERE suggestion_id = $target_suggestion_id);

                    DELETE FROM identity_suggestion_rankings
                    WHERE suggestion_id = $source_suggestion_id;

                    DELETE FROM identity_suggestions
                    WHERE id = $source_suggestion_id;
                    """;
                combine.Parameters.AddWithValue("$status", MergeStatus(targetStatus!, source.Status));
                combine.Parameters.AddWithValue("$target_suggestion_id", targetId);
                combine.Parameters.AddWithValue("$source_suggestion_id", source.Id);
                await combine.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task ConsolidateFavoritesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId sourcePersonId,
        PersonId targetPersonId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO person_favorites (person_id, favorited_at_utc)
            SELECT $target_person_id, $favorited_at_utc
            WHERE EXISTS (
                SELECT 1
                FROM person_favorites
                WHERE person_id IN ($source_person_id, $target_person_id))
            ON CONFLICT(person_id) DO UPDATE SET
                favorited_at_utc = excluded.favorited_at_utc;

            DELETE FROM person_favorites
            WHERE person_id = $source_person_id;
            """;
        command.Parameters.AddWithValue("$source_person_id", sourcePersonId.ToString());
        command.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
        command.Parameters.AddWithValue("$favorited_at_utc", Format(changedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> RequireActivePersonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT display_name
            FROM people
            WHERE id = $person_id
              AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : throw new KeyNotFoundException($"Active person {personId} was not found.");
    }

    private static async Task<long> InsertActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string kind,
        PersonId personId,
        string previousDisplayName,
        PersonId? targetPersonId,
        string newDisplayName,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        bool reversible,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO person_maintenance_actions (
                action_kind,
                person_id,
                previous_display_name,
                target_person_id,
                new_display_name,
                actor,
                note,
                created_at_utc,
                reversible)
            VALUES (
                $action_kind,
                $person_id,
                $previous_display_name,
                $target_person_id,
                $new_display_name,
                $actor,
                $note,
                $created_at_utc,
                $reversible);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$action_kind", kind);
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$previous_display_name", previousDisplayName);
        command.Parameters.AddWithValue(
            "$target_person_id",
            targetPersonId is PersonId target ? target.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("$new_display_name", newDisplayName);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
        command.Parameters.AddWithValue("$reversible", reversible ? 1 : 0);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static CataloguePersonMaintenanceAction ReadAction(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        PersonId.From(Guid.Parse(reader.GetString(2))),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : PersonId.From(Guid.Parse(reader.GetString(4))),
        reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        Parse(reader.GetString(8)),
        reader.GetInt64(9) == 1);

    private static PersonMaintenancePerson ToCorePerson(
        CataloguePersonMaintenancePerson person) => new(
        person.Id,
        person.DisplayName,
        person.LabelCount,
        person.SuggestionCount);

    private static PersonMaintenanceAction ToCoreAction(
        CataloguePersonMaintenanceAction action) => new(
        action.Id,
        action.Kind,
        action.PersonId,
        action.PreviousDisplayName,
        action.TargetPersonId,
        action.NewDisplayName,
        action.Actor,
        action.Note,
        action.CreatedAtUtc,
        action.Reversible);

    private static string MergeStatus(string targetStatus, string sourceStatus)
    {
        static int Rank(string status) => status switch
        {
            "accepted" => 3,
            "rejected" => 2,
            "pending" => 1,
            _ => 0,
        };

        return Rank(sourceStatus) > Rank(targetStatus) ? sourceStatus : targetStatus;
    }

    private static string NormalizeDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException("A person display name cannot exceed 200 characters.", nameof(value));
        }

        return normalized;
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException("The value cannot exceed 200 characters.", parameterName);
        }

        return normalized;
    }

    private static string? Optional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > 1000)
        {
            throw new ArgumentException("A maintenance note cannot exceed 1000 characters.", nameof(value));
        }

        return normalized;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();

    private sealed record SuggestionRow(
        long Id,
        string FaceOccurrenceId,
        string ModelId,
        string ModelHash,
        string Status);
}
