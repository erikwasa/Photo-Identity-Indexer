using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL canonical person maintenance with append-only rename/merge audit history.
/// Renames are reversible through another audited rename; merges are irreversible.
/// </summary>
public sealed class PostgresPersonMaintenanceRepository :
    IPersonMaintenanceRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPersonMaintenanceRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<PersonMaintenancePerson>> GetPeopleAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                person.id,
                person.display_name,
                (
                    SELECT COUNT(*)
                    FROM person_labels AS label
                    WHERE label.person_id = person.id
                ),
                (
                    SELECT COUNT(*)
                    FROM identity_suggestions AS suggestion
                    WHERE suggestion.suggested_person_id = person.id
                )
            FROM people AS person
            WHERE person.merged_into_person_id IS NULL
              AND person.display_name IS NOT NULL
              AND BTRIM(person.display_name) <> ''
            ORDER BY
                LOWER(person.display_name),
                person.display_name,
                person.id;
            """;

        List<PersonMaintenancePerson> people = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            people.Add(new PersonMaintenancePerson(
                PersonId.From(reader.GetGuid(0)),
                reader.GetString(1),
                checked((int)reader.GetInt64(2)),
                checked((int)reader.GetInt64(3))));
        }

        return people;
    }

    public async Task<IReadOnlyList<PersonMaintenanceAction>> GetHistoryAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Person history limit must be between 1 and 500.");
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.CommandText =
            """
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
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("limit", limit);

        List<PersonMaintenanceAction> actions = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(ReadAction(reader));
        }

        return actions;
    }

    public async Task<PersonMaintenanceAction> RenameAsync(
        PersonId personId,
        string displayName,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedName =
            NormalizeDisplayName(displayName);
        string normalizedActor =
            Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);
        DateTimeOffset createdAt =
            createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        string previousName =
            await RequireActivePersonAsync(
                connection,
                transaction,
                personId,
                forUpdate: true,
                cancellationToken);

        if (string.Equals(
                previousName,
                normalizedName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The person already uses that display name.");
        }

        await using (NpgsqlCommand update =
                     connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE people
                SET display_name = @display_name
                WHERE id = @person_id
                  AND merged_into_person_id IS NULL;
                """;
            update.Parameters.AddWithValue(
                "display_name",
                normalizedName);
            update.Parameters.AddWithValue(
                "person_id",
                Guid.Parse(personId.ToString()));

            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The person changed before the rename could be saved.");
            }
        }

        long actionId =
            await InsertActionAsync(
                connection,
                transaction,
                PersonMaintenanceActionKinds.Rename,
                personId,
                previousName,
                targetPersonId: null,
                normalizedName,
                normalizedActor,
                normalizedNote,
                createdAt,
                reversible: true,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PersonMaintenanceAction(
            actionId,
            PersonMaintenanceActionKinds.Rename,
            personId,
            previousName,
            TargetPersonId: null,
            normalizedName,
            normalizedActor,
            normalizedNote,
            createdAt,
            Reversible: true);
    }

    public async Task<PersonMaintenanceAction> MergeAsync(
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
            throw new ArgumentException(
                "A person cannot be merged into itself.",
                nameof(targetPersonId));
        }

        string normalizedActor =
            Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);
        DateTimeOffset createdAt =
            createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        Dictionary<PersonId, string> lockedPeople =
            await LockActivePeopleAsync(
                connection,
                transaction,
                sourcePersonId,
                targetPersonId,
                cancellationToken);

        string sourceName = lockedPeople[sourcePersonId];
        string targetName = lockedPeople[targetPersonId];

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
            createdAt,
            cancellationToken);

        await using (NpgsqlCommand merge =
                     connection.CreateCommand())
        {
            merge.Transaction = transaction;
            merge.CommandText =
                """
                UPDATE people
                SET merged_into_person_id = @target_person_id
                WHERE id = @source_person_id
                  AND merged_into_person_id IS NULL;
                """;
            merge.Parameters.AddWithValue(
                "source_person_id",
                Guid.Parse(sourcePersonId.ToString()));
            merge.Parameters.AddWithValue(
                "target_person_id",
                Guid.Parse(targetPersonId.ToString()));

            if (await merge.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The source person changed before the merge could be saved.");
            }
        }

        long actionId =
            await InsertActionAsync(
                connection,
                transaction,
                PersonMaintenanceActionKinds.Merge,
                sourcePersonId,
                sourceName,
                targetPersonId,
                targetName,
                normalizedActor,
                normalizedNote,
                createdAt,
                reversible: false,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PersonMaintenanceAction(
            actionId,
            PersonMaintenanceActionKinds.Merge,
            sourcePersonId,
            sourceName,
            targetPersonId,
            targetName,
            normalizedActor,
            normalizedNote,
            createdAt,
            Reversible: false);
    }

    private static async Task<Dictionary<PersonId, string>>
        LockActivePeopleAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            PersonId sourcePersonId,
            PersonId targetPersonId,
            CancellationToken cancellationToken)
    {
        Guid sourceId = Guid.Parse(sourcePersonId.ToString());
        Guid targetId = Guid.Parse(targetPersonId.ToString());

        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                id,
                display_name,
                merged_into_person_id
            FROM people
            WHERE id IN (@source_person_id, @target_person_id)
            ORDER BY id
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue(
            "source_person_id",
            sourceId);
        command.Parameters.AddWithValue(
            "target_person_id",
            targetId);

        Dictionary<PersonId, string> people = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            PersonId id =
                PersonId.From(reader.GetGuid(0));
            if (!reader.IsDBNull(2) ||
                reader.IsDBNull(1) ||
                string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                continue;
            }

            people[id] = reader.GetString(1);
        }

        if (!people.ContainsKey(sourcePersonId))
        {
            throw new KeyNotFoundException(
                $"Active person {sourcePersonId} was not found.");
        }

        if (!people.ContainsKey(targetPersonId))
        {
            throw new KeyNotFoundException(
                $"Active person {targetPersonId} was not found.");
        }

        return people;
    }

    private static async Task<string> RequireActivePersonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId personId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = forUpdate
            ? """
              SELECT display_name
              FROM people
              WHERE id = @person_id
                AND merged_into_person_id IS NULL
              FOR UPDATE;
              """
            : """
              SELECT display_name
              FROM people
              WHERE id = @person_id
                AND merged_into_person_id IS NULL;
              """;
        command.Parameters.AddWithValue(
            "person_id",
            Guid.Parse(personId.ToString()));

        object? value =
            await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName &&
            !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : throw new KeyNotFoundException(
                $"Active person {personId} was not found.");
    }

    private static async Task ConsolidateLabelsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId sourcePersonId,
        PersonId targetPersonId,
        CancellationToken cancellationToken)
    {
        Guid sourceId = Guid.Parse(sourcePersonId.ToString());
        Guid targetId = Guid.Parse(targetPersonId.ToString());

        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE review_actions AS action
            SET
                person_id = @target_person_id,
                person_label_id = target_label.id
            FROM person_labels AS source_label
            INNER JOIN person_labels AS target_label
                ON target_label.person_id = @target_person_id
               AND target_label.face_occurrence_id =
                    source_label.face_occurrence_id
               AND target_label.label_kind =
                    source_label.label_kind
            WHERE action.person_id = @source_person_id
              AND action.person_label_id = source_label.id
              AND source_label.person_id = @source_person_id;

            DELETE FROM person_labels AS source_label
            WHERE source_label.person_id = @source_person_id
              AND EXISTS (
                    SELECT 1
                    FROM person_labels AS target_label
                    WHERE target_label.person_id = @target_person_id
                      AND target_label.face_occurrence_id =
                            source_label.face_occurrence_id
                      AND target_label.label_kind =
                            source_label.label_kind);

            UPDATE person_labels
            SET person_id = @target_person_id
            WHERE person_id = @source_person_id;

            UPDATE review_actions
            SET person_id = @target_person_id
            WHERE person_id = @source_person_id;
            """;
        command.Parameters.AddWithValue(
            "source_person_id",
            sourceId);
        command.Parameters.AddWithValue(
            "target_person_id",
            targetId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ConsolidateSuggestionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId sourcePersonId,
        PersonId targetPersonId,
        CancellationToken cancellationToken)
    {
        Guid sourceId = Guid.Parse(sourcePersonId.ToString());
        Guid targetId = Guid.Parse(targetPersonId.ToString());

        List<SuggestionRow> sourceSuggestions = [];
        await using (NpgsqlCommand read =
                     connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT
                    id,
                    face_occurrence_id,
                    model_id,
                    model_hash,
                    status
                FROM identity_suggestions
                WHERE suggested_person_id = @source_person_id
                ORDER BY id
                FOR UPDATE;
                """;
            read.Parameters.AddWithValue(
                "source_person_id",
                sourceId);

            await using NpgsqlDataReader reader =
                await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sourceSuggestions.Add(new SuggestionRow(
                    reader.GetInt64(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        foreach (SuggestionRow source in sourceSuggestions)
        {
            long? targetSuggestionId = null;
            string? targetStatus = null;

            await using (NpgsqlCommand findTarget =
                         connection.CreateCommand())
            {
                findTarget.Transaction = transaction;
                findTarget.CommandText =
                    """
                    SELECT id, status
                    FROM identity_suggestions
                    WHERE face_occurrence_id = @face_occurrence_id
                      AND suggested_person_id = @target_person_id
                      AND model_id = @model_id
                      AND model_hash = @model_hash
                    FOR UPDATE;
                    """;
                findTarget.Parameters.AddWithValue(
                    "face_occurrence_id",
                    source.FaceOccurrenceId);
                findTarget.Parameters.AddWithValue(
                    "target_person_id",
                    targetId);
                findTarget.Parameters.AddWithValue(
                    "model_id",
                    source.ModelId);
                findTarget.Parameters.AddWithValue(
                    "model_hash",
                    source.ModelHash);

                await using NpgsqlDataReader reader =
                    await findTarget.ExecuteReaderAsync(
                        cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    targetSuggestionId = reader.GetInt64(0);
                    targetStatus = reader.GetString(1);
                }
            }

            if (targetSuggestionId is not long targetSuggestion)
            {
                await using NpgsqlCommand move =
                    connection.CreateCommand();
                move.Transaction = transaction;
                move.CommandText =
                    """
                    UPDATE identity_suggestions
                    SET suggested_person_id = @target_person_id
                    WHERE id = @source_suggestion_id;
                    """;
                move.Parameters.AddWithValue(
                    "target_person_id",
                    targetId);
                move.Parameters.AddWithValue(
                    "source_suggestion_id",
                    source.Id);
                await move.ExecuteNonQueryAsync(cancellationToken);
                continue;
            }

            string mergedStatus =
                MergeStatus(targetStatus!, source.Status);

            await using (NpgsqlCommand updateTarget =
                         connection.CreateCommand())
            {
                updateTarget.Transaction = transaction;
                updateTarget.CommandText =
                    """
                    UPDATE identity_suggestions
                    SET status = @status
                    WHERE id = @target_suggestion_id;
                    """;
                updateTarget.Parameters.AddWithValue(
                    "status",
                    mergedStatus);
                updateTarget.Parameters.AddWithValue(
                    "target_suggestion_id",
                    targetSuggestion);
                await updateTarget.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using (NpgsqlCommand moveHistory =
                         connection.CreateCommand())
            {
                moveHistory.Transaction = transaction;
                moveHistory.CommandText =
                    """
                    UPDATE identity_suggestion_review_actions
                    SET suggestion_id = @target_suggestion_id
                    WHERE suggestion_id = @source_suggestion_id;
                    """;
                moveHistory.Parameters.AddWithValue(
                    "target_suggestion_id",
                    targetSuggestion);
                moveHistory.Parameters.AddWithValue(
                    "source_suggestion_id",
                    source.Id);
                await moveHistory.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            bool targetHasRanking;
            await using (NpgsqlCommand rankingCheck =
                         connection.CreateCommand())
            {
                rankingCheck.Transaction = transaction;
                rankingCheck.CommandText =
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM identity_suggestion_rankings
                        WHERE suggestion_id = @target_suggestion_id);
                    """;
                rankingCheck.Parameters.AddWithValue(
                    "target_suggestion_id",
                    targetSuggestion);
                targetHasRanking =
                    (bool)(await rankingCheck.ExecuteScalarAsync(
                        cancellationToken) ?? false);
            }

            if (!targetHasRanking)
            {
                await using NpgsqlCommand moveRanking =
                    connection.CreateCommand();
                moveRanking.Transaction = transaction;
                moveRanking.CommandText =
                    """
                    UPDATE identity_suggestion_rankings
                    SET suggestion_id = @target_suggestion_id
                    WHERE suggestion_id = @source_suggestion_id;
                    """;
                moveRanking.Parameters.AddWithValue(
                    "target_suggestion_id",
                    targetSuggestion);
                moveRanking.Parameters.AddWithValue(
                    "source_suggestion_id",
                    source.Id);
                await moveRanking.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using (NpgsqlCommand deleteSourceRanking =
                         connection.CreateCommand())
            {
                deleteSourceRanking.Transaction = transaction;
                deleteSourceRanking.CommandText =
                    """
                    DELETE FROM identity_suggestion_rankings
                    WHERE suggestion_id = @source_suggestion_id;
                    """;
                deleteSourceRanking.Parameters.AddWithValue(
                    "source_suggestion_id",
                    source.Id);
                await deleteSourceRanking.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using NpgsqlCommand deleteSourceSuggestion =
                connection.CreateCommand();
            deleteSourceSuggestion.Transaction = transaction;
            deleteSourceSuggestion.CommandText =
                """
                DELETE FROM identity_suggestions
                WHERE id = @source_suggestion_id;
                """;
            deleteSourceSuggestion.Parameters.AddWithValue(
                "source_suggestion_id",
                source.Id);
            await deleteSourceSuggestion.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static async Task ConsolidateFavoritesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId sourcePersonId,
        PersonId targetPersonId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO person_favorites (
                person_id,
                favorited_at_utc)
            SELECT
                @target_person_id,
                @favorited_at_utc
            WHERE EXISTS (
                SELECT 1
                FROM person_favorites
                WHERE person_id IN (
                    @source_person_id,
                    @target_person_id))
            ON CONFLICT(person_id) DO UPDATE SET
                favorited_at_utc = excluded.favorited_at_utc;

            DELETE FROM person_favorites
            WHERE person_id = @source_person_id;
            """;
        command.Parameters.AddWithValue(
            "source_person_id",
            Guid.Parse(sourcePersonId.ToString()));
        command.Parameters.AddWithValue(
            "target_person_id",
            Guid.Parse(targetPersonId.ToString()));
        command.Parameters.AddWithValue(
            "favorited_at_utc",
            changedAtUtc.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> InsertActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
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
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
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
                @action_kind,
                @person_id,
                @previous_display_name,
                @target_person_id,
                @new_display_name,
                @actor,
                @note,
                @created_at_utc,
                @reversible)
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "action_kind",
            kind);
        command.Parameters.AddWithValue(
            "person_id",
            Guid.Parse(personId.ToString()));
        command.Parameters.AddWithValue(
            "previous_display_name",
            previousDisplayName);

        NpgsqlParameter target =
            command.Parameters.Add(
                "target_person_id",
                NpgsqlDbType.Uuid);
        target.Value = targetPersonId is PersonId targetId
            ? Guid.Parse(targetId.ToString())
            : DBNull.Value;

        command.Parameters.AddWithValue(
            "new_display_name",
            newDisplayName);
        command.Parameters.AddWithValue(
            "actor",
            actor);
        AddNullableText(command, "note", note);
        command.Parameters.AddWithValue(
            "created_at_utc",
            createdAtUtc.ToUniversalTime());
        command.Parameters.AddWithValue(
            "reversible",
            reversible);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static PersonMaintenanceAction ReadAction(
        NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        PersonId.From(reader.GetGuid(2)),
        reader.GetString(3),
        reader.IsDBNull(4)
            ? null
            : PersonId.From(reader.GetGuid(4)),
        reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7)
            ? null
            : reader.GetString(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.GetBoolean(9));

    private static string MergeStatus(
        string targetStatus,
        string sourceStatus)
    {
        static int Rank(string status) => status switch
        {
            ReviewSuggestionStatuses.Accepted => 3,
            ReviewSuggestionStatuses.Rejected => 2,
            ReviewSuggestionStatuses.Pending => 1,
            _ => 0,
        };

        return Rank(sourceStatus) > Rank(targetStatus)
            ? sourceStatus
            : targetStatus;
    }

    private static string NormalizeDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException(
                "A person display name cannot exceed 200 characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string Required(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException(
                "The value cannot exceed 200 characters.",
                parameterName);
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
            throw new ArgumentException(
                "A maintenance note cannot exceed 1000 characters.",
                nameof(value));
        }

        return normalized;
    }

    private static void AddNullableText(
        NpgsqlCommand command,
        string name,
        string? value)
    {
        NpgsqlParameter parameter =
            command.Parameters.Add(
                name,
                NpgsqlDbType.Text);
        parameter.Value = value is null
            ? DBNull.Value
            : value;
    }

    private sealed record SuggestionRow(
        long Id,
        Guid FaceOccurrenceId,
        string ModelId,
        string ModelHash,
        string Status);
}
