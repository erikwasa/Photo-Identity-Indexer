using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL durability for identity-match regeneration control state. The target snapshot is
/// immutable for a run; interrupted running targets remain reclaimable before pending targets.
/// </summary>
public sealed class PostgresIdentityMatchRegenerationRepository :
    IIdentityMatchRegenerationRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresIdentityMatchRegenerationRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ReviewIdentityMatchRegenerationRun> StartAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int policyVersion,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (policyVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion));
        }

        string actor = Required(requestedBy, nameof(requestedBy));
        DateTimeOffset now = requestedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        ReviewIdentityMatchRegenerationRun? active = await ReadActiveAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        if (active is not null)
        {
            throw AlreadyActive(modelId, modelHash, active.Status);
        }

        ReviewIdentityMatchEvidenceVersion evidence = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        IReadOnlyList<FaceOccurrenceId> targets = await ReadEligibleTargetIdsAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);

        Guid runId = Guid.NewGuid();
        try
        {
            await using (NpgsqlCommand insertRun = connection.CreateCommand())
            {
                insertRun.Transaction = transaction;
                insertRun.CommandText =
                    """
                    INSERT INTO identity_match_regeneration_runs (
                        id,
                        model_id,
                        model_hash,
                        policy_version,
                        status,
                        evidence_review_action_id,
                        evidence_suggestion_review_action_id,
                        evidence_person_merge_action_id,
                        evidence_embedding_id,
                        target_count,
                        processed_target_count,
                        suggested_target_count,
                        suggestion_count,
                        automatically_assigned_count,
                        error_count,
                        requested_by,
                        requested_at_utc,
                        started_at_utc,
                        completed_at_utc,
                        updated_at_utc,
                        error)
                    VALUES (
                        @id,
                        @model_id,
                        @model_hash,
                        @policy_version,
                        @status,
                        @review_action_id,
                        @suggestion_review_action_id,
                        @person_merge_action_id,
                        @embedding_id,
                        @target_count,
                        0,
                        0,
                        0,
                        0,
                        0,
                        @requested_by,
                        @requested_at_utc,
                        NULL,
                        NULL,
                        @updated_at_utc,
                        NULL);
                    """;
                AddRunIdentityParameters(insertRun, runId, modelId, modelHash);
                insertRun.Parameters.AddWithValue("policy_version", policyVersion);
                insertRun.Parameters.AddWithValue(
                    "status",
                    ReviewIdentityMatchRegenerationStatuses.Pending);
                insertRun.Parameters.AddWithValue("review_action_id", evidence.ReviewActionId);
                insertRun.Parameters.AddWithValue(
                    "suggestion_review_action_id",
                    evidence.SuggestionReviewActionId);
                insertRun.Parameters.AddWithValue(
                    "person_merge_action_id",
                    evidence.PersonMergeActionId);
                insertRun.Parameters.AddWithValue("embedding_id", evidence.EmbeddingId);
                insertRun.Parameters.AddWithValue("target_count", targets.Count);
                insertRun.Parameters.AddWithValue("requested_by", actor);
                insertRun.Parameters.AddWithValue("requested_at_utc", now);
                insertRun.Parameters.AddWithValue("updated_at_utc", now);
                await insertRun.ExecuteNonQueryAsync(cancellationToken);
            }

            for (int index = 0; index < targets.Count; index++)
            {
                await using NpgsqlCommand insertTarget = connection.CreateCommand();
                insertTarget.Transaction = transaction;
                insertTarget.CommandText =
                    """
                    INSERT INTO identity_match_regeneration_targets (
                        run_id,
                        face_occurrence_id,
                        ordinal,
                        status,
                        suggestion_count,
                        error)
                    VALUES (
                        @run_id,
                        @face_occurrence_id,
                        @ordinal,
                        @status,
                        0,
                        NULL);
                    """;
                insertTarget.Parameters.AddWithValue("run_id", runId);
                insertTarget.Parameters.AddWithValue(
                    "face_occurrence_id",
                    Guid.Parse(targets[index].ToString()));
                insertTarget.Parameters.AddWithValue("ordinal", index);
                insertTarget.Parameters.AddWithValue(
                    "status",
                    ReviewIdentityMatchRegenerationTargetStatuses.Pending);
                await insertTarget.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw AlreadyActive(modelId, modelHash, "active", exception);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ReviewIdentityMatchRegenerationRun(
            runId,
            modelId,
            modelHash,
            policyVersion,
            ReviewIdentityMatchRegenerationStatuses.Pending,
            evidence,
            targets.Count,
            0,
            0,
            0,
            0,
            0,
            actor,
            now,
            null,
            null,
            now,
            null);
    }

    public async Task<ReviewIdentityMatchRegenerationRun?> GetLatestAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        ReviewIdentityMatchRegenerationRun? result = await ReadLatestAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ReviewIdentityMatchRegenerationRun?> GetNextActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            RunSelect +
            """
            WHERE status IN (@pending_status, @running_status)
            ORDER BY requested_at_utc, id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "pending_status",
            ReviewIdentityMatchRegenerationStatuses.Pending);
        command.Parameters.AddWithValue(
            "running_status",
            ReviewIdentityMatchRegenerationStatuses.Running);

        ReviewIdentityMatchRegenerationRun? result = null;
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            result = ReadRun(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ReviewIdentityMatchRegenerationTarget?> ClaimNextTargetAsync(
        Guid runId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        ReviewIdentityMatchRegenerationRun run = await RequireRunAsync(
            connection,
            transaction,
            runId,
            forUpdate: true,
            cancellationToken);
        if (!run.IsActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        ReviewIdentityMatchEvidenceVersion currentEvidence = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            run.ModelId,
            run.ModelHash,
            cancellationToken);
        if (currentEvidence != run.EvidenceVersion)
        {
            await UpdateRunStatusAsync(
                connection,
                transaction,
                runId,
                ReviewIdentityMatchRegenerationStatuses.Stale,
                "Identity evidence changed while regeneration was running. Start a new regeneration from the current catalogue state.",
                now,
                completed: true,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        ReviewIdentityMatchRegenerationTarget? target = await ReadNextTargetAsync(
            connection,
            transaction,
            runId,
            cancellationToken);
        if (target is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await using (NpgsqlCommand updateTarget = connection.CreateCommand())
        {
            updateTarget.Transaction = transaction;
            updateTarget.CommandText =
                """
                UPDATE identity_match_regeneration_targets
                SET status = @status,
                    error = NULL
                WHERE run_id = @run_id
                  AND face_occurrence_id = @face_occurrence_id;
                """;
            updateTarget.Parameters.AddWithValue(
                "status",
                ReviewIdentityMatchRegenerationTargetStatuses.Running);
            updateTarget.Parameters.AddWithValue("run_id", runId);
            updateTarget.Parameters.AddWithValue(
                "face_occurrence_id",
                Guid.Parse(target.FaceOccurrenceId.ToString()));
            await updateTarget.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand updateRun = connection.CreateCommand())
        {
            updateRun.Transaction = transaction;
            updateRun.CommandText =
                """
                UPDATE identity_match_regeneration_runs
                SET status = @status,
                    started_at_utc = COALESCE(started_at_utc, @now),
                    updated_at_utc = @now
                WHERE id = @run_id;
                """;
            updateRun.Parameters.AddWithValue(
                "status",
                ReviewIdentityMatchRegenerationStatuses.Running);
            updateRun.Parameters.AddWithValue("now", now);
            updateRun.Parameters.AddWithValue("run_id", runId);
            await updateRun.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return target with
        {
            Status = ReviewIdentityMatchRegenerationTargetStatuses.Running,
            Error = null,
        };
    }

    public async Task CompleteTargetAsync(
        Guid runId,
        FaceOccurrenceId faceOccurrenceId,
        int suggestionCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (suggestionCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suggestionCount));
        }

        await CompleteTargetCoreAsync(
            runId,
            faceOccurrenceId,
            ReviewIdentityMatchRegenerationTargetStatuses.Completed,
            suggestionCount,
            error: null,
            nowUtc,
            cancellationToken);
    }

    public Task FailTargetAsync(
        Guid runId,
        FaceOccurrenceId faceOccurrenceId,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default) =>
        CompleteTargetCoreAsync(
            runId,
            faceOccurrenceId,
            ReviewIdentityMatchRegenerationTargetStatuses.Error,
            suggestionCount: 0,
            Required(error, nameof(error)),
            nowUtc,
            cancellationToken);

    public async Task CompleteRunAsync(
        Guid runId,
        int automaticallyAssignedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (automaticallyAssignedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(automaticallyAssignedCount));
        }

        DateTimeOffset now = nowUtc.ToUniversalTime();
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        _ = await RequireRunAsync(
            connection,
            transaction,
            runId,
            forUpdate: true,
            cancellationToken);

        await using (NpgsqlCommand remaining = connection.CreateCommand())
        {
            remaining.Transaction = transaction;
            remaining.CommandText =
                """
                SELECT COUNT(*)
                FROM identity_match_regeneration_targets
                WHERE run_id = @run_id
                  AND status IN (@pending_status, @running_status);
                """;
            remaining.Parameters.AddWithValue("run_id", runId);
            remaining.Parameters.AddWithValue(
                "pending_status",
                ReviewIdentityMatchRegenerationTargetStatuses.Pending);
            remaining.Parameters.AddWithValue(
                "running_status",
                ReviewIdentityMatchRegenerationTargetStatuses.Running);
            long count = Convert.ToInt64(
                await remaining.ExecuteScalarAsync(cancellationToken));
            if (count != 0)
            {
                throw new InvalidOperationException(
                    "Cannot complete identity regeneration while targets remain unfinished.");
            }
        }

        await using NpgsqlCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE identity_match_regeneration_runs
            SET status = @status,
                automatically_assigned_count = @automatically_assigned_count,
                completed_at_utc = @now,
                updated_at_utc = @now,
                error = NULL
            WHERE id = @run_id
              AND status IN (@pending_status, @running_status);
            """;
        update.Parameters.AddWithValue(
            "status",
            ReviewIdentityMatchRegenerationStatuses.Completed);
        update.Parameters.AddWithValue(
            "automatically_assigned_count",
            automaticallyAssignedCount);
        update.Parameters.AddWithValue("now", now);
        update.Parameters.AddWithValue("run_id", runId);
        update.Parameters.AddWithValue(
            "pending_status",
            ReviewIdentityMatchRegenerationStatuses.Pending);
        update.Parameters.AddWithValue(
            "running_status",
            ReviewIdentityMatchRegenerationStatuses.Running);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid runId,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        _ = await RequireRunAsync(
            connection,
            transaction,
            runId,
            forUpdate: true,
            cancellationToken);
        await UpdateRunStatusAsync(
            connection,
            transaction,
            runId,
            ReviewIdentityMatchRegenerationStatuses.Failed,
            Required(error, nameof(error)),
            nowUtc.ToUniversalTime(),
            completed: true,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> EvidenceStillMatchesAsync(
        ReviewIdentityMatchRegenerationRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        ReviewIdentityMatchEvidenceVersion current = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            run.ModelId,
            run.ModelHash,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return current == run.EvidenceVersion;
    }

    private async Task CompleteTargetCoreAsync(
        Guid runId,
        FaceOccurrenceId faceOccurrenceId,
        string status,
        int suggestionCount,
        string? error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        _ = await RequireRunAsync(
            connection,
            transaction,
            runId,
            forUpdate: true,
            cancellationToken);

        await using NpgsqlCommand updateTarget = connection.CreateCommand();
        updateTarget.Transaction = transaction;
        updateTarget.CommandText =
            """
            UPDATE identity_match_regeneration_targets
            SET status = @status,
                suggestion_count = @suggestion_count,
                error = @error
            WHERE run_id = @run_id
              AND face_occurrence_id = @face_occurrence_id
              AND status <> @completed_status
              AND status <> @error_status;
            """;
        updateTarget.Parameters.AddWithValue("status", status);
        updateTarget.Parameters.AddWithValue("suggestion_count", suggestionCount);
        updateTarget.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        updateTarget.Parameters.AddWithValue("run_id", runId);
        updateTarget.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        updateTarget.Parameters.AddWithValue(
            "completed_status",
            ReviewIdentityMatchRegenerationTargetStatuses.Completed);
        updateTarget.Parameters.AddWithValue(
            "error_status",
            ReviewIdentityMatchRegenerationTargetStatuses.Error);
        int changed = await updateTarget.ExecuteNonQueryAsync(cancellationToken);

        if (changed == 1)
        {
            await using NpgsqlCommand updateRun = connection.CreateCommand();
            updateRun.Transaction = transaction;
            updateRun.CommandText =
                """
                UPDATE identity_match_regeneration_runs
                SET processed_target_count = processed_target_count + 1,
                    suggested_target_count = suggested_target_count + @suggested_increment,
                    suggestion_count = suggestion_count + @suggestion_count,
                    error_count = error_count + @error_increment,
                    updated_at_utc = @now
                WHERE id = @run_id;
                """;
            updateRun.Parameters.AddWithValue(
                "suggested_increment",
                suggestionCount > 0 ? 1 : 0);
            updateRun.Parameters.AddWithValue("suggestion_count", suggestionCount);
            updateRun.Parameters.AddWithValue(
                "error_increment",
                status == ReviewIdentityMatchRegenerationTargetStatuses.Error ? 1 : 0);
            updateRun.Parameters.AddWithValue("now", now);
            updateRun.Parameters.AddWithValue("run_id", runId);
            await updateRun.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task EnsureSchemaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS identity_match_regeneration_runs (
                id uuid NOT NULL PRIMARY KEY,
                model_id text NOT NULL CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                policy_version integer NOT NULL CHECK (policy_version >= 1),
                status text NOT NULL
                    CHECK (status IN ('pending', 'running', 'completed', 'stale', 'failed')),
                evidence_review_action_id bigint NOT NULL CHECK (evidence_review_action_id >= 0),
                evidence_suggestion_review_action_id bigint NOT NULL CHECK (evidence_suggestion_review_action_id >= 0),
                evidence_person_merge_action_id bigint NOT NULL CHECK (evidence_person_merge_action_id >= 0),
                evidence_embedding_id bigint NOT NULL CHECK (evidence_embedding_id >= 0),
                target_count integer NOT NULL CHECK (target_count >= 0),
                processed_target_count integer NOT NULL CHECK (processed_target_count >= 0),
                suggested_target_count integer NOT NULL CHECK (suggested_target_count >= 0),
                suggestion_count integer NOT NULL CHECK (suggestion_count >= 0),
                automatically_assigned_count integer NOT NULL CHECK (automatically_assigned_count >= 0),
                error_count integer NOT NULL CHECK (error_count >= 0),
                requested_by text NOT NULL CHECK (btrim(requested_by) <> ''),
                requested_at_utc timestamp with time zone NOT NULL,
                started_at_utc timestamp with time zone NULL,
                completed_at_utc timestamp with time zone NULL,
                updated_at_utc timestamp with time zone NOT NULL,
                error text NULL,
                CHECK (processed_target_count <= target_count),
                CHECK (suggested_target_count <= processed_target_count)
            );

            CREATE TABLE IF NOT EXISTS identity_match_regeneration_targets (
                run_id uuid NOT NULL,
                face_occurrence_id uuid NOT NULL,
                ordinal integer NOT NULL CHECK (ordinal >= 0),
                status text NOT NULL
                    CHECK (status IN ('pending', 'running', 'completed', 'error')),
                suggestion_count integer NOT NULL CHECK (suggestion_count >= 0),
                error text NULL,
                PRIMARY KEY (run_id, face_occurrence_id),
                UNIQUE (run_id, ordinal),
                CONSTRAINT fk_identity_match_regeneration_targets_run
                    FOREIGN KEY (run_id)
                    REFERENCES identity_match_regeneration_runs (id) ON DELETE CASCADE,
                CONSTRAINT fk_identity_match_regeneration_targets_face
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_match_regeneration_active_model
                ON identity_match_regeneration_runs (model_id, model_hash)
                WHERE status IN ('pending', 'running');

            CREATE INDEX IF NOT EXISTS ix_identity_match_regeneration_status
                ON identity_match_regeneration_runs (status, requested_at_utc, id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<FaceOccurrenceId>> ReadEligibleTargetIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH latest_review AS (
                SELECT
                    face_occurrence_id,
                    action_kind,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'unknown', 'reject')
                  AND reversed_at_utc IS NULL
            ),
            legacy_confirmed AS (
                SELECT DISTINCT label.face_occurrence_id
                FROM person_labels AS label
                WHERE label.label_kind = 'confirmed'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM review_actions AS action
                      WHERE action.face_occurrence_id = label.face_occurrence_id)
            ),
            matching_embeddings AS (
                SELECT
                    crop.face_occurrence_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY crop.face_occurrence_id
                        ORDER BY embedding.created_at_utc DESC, embedding.id DESC) AS row_number
                FROM face_crops AS crop
                INNER JOIN embeddings AS embedding
                    ON embedding.face_crop_id = crop.id
                WHERE embedding.model_id = @model_id
                  AND embedding.model_hash = @model_hash
            )
            SELECT matching.face_occurrence_id
            FROM matching_embeddings AS matching
            WHERE matching.row_number = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM latest_review AS review
                  WHERE review.face_occurrence_id = matching.face_occurrence_id
                    AND review.row_number = 1)
              AND NOT EXISTS (
                  SELECT 1
                  FROM legacy_confirmed AS confirmed
                  WHERE confirmed.face_occurrence_id = matching.face_occurrence_id)
            ORDER BY matching.face_occurrence_id;
            """;
        AddModelParameters(command, modelId, modelHash);

        List<FaceOccurrenceId> targets = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(FaceOccurrenceId.From(reader.GetGuid(0)));
        }

        return targets;
    }

    private static async Task<ReviewIdentityMatchEvidenceVersion> ReadEvidenceVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                COALESCE((SELECT MAX(id) FROM review_actions), 0),
                COALESCE((SELECT MAX(id) FROM identity_suggestion_review_actions), 0),
                COALESCE((SELECT MAX(id) FROM person_maintenance_actions WHERE action_kind = 'merge'), 0),
                COALESCE((
                    SELECT MAX(embedding.id)
                    FROM embeddings AS embedding
                    WHERE embedding.model_id = @model_id
                      AND embedding.model_hash = @model_hash), 0);
            """;
        AddModelParameters(command, modelId, modelHash);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not read identity evidence version.");
        }

        return new ReviewIdentityMatchEvidenceVersion(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task<ReviewIdentityMatchRegenerationRun?> ReadActiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            RunSelect +
            """
            WHERE model_id = @model_id
              AND model_hash = @model_hash
              AND status IN (@pending_status, @running_status)
            ORDER BY requested_at_utc DESC, id DESC
            LIMIT 1;
            """;
        AddModelParameters(command, modelId, modelHash);
        command.Parameters.AddWithValue(
            "pending_status",
            ReviewIdentityMatchRegenerationStatuses.Pending);
        command.Parameters.AddWithValue(
            "running_status",
            ReviewIdentityMatchRegenerationStatuses.Running);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static async Task<ReviewIdentityMatchRegenerationRun?> ReadLatestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            RunSelect +
            """
            WHERE model_id = @model_id
              AND model_hash = @model_hash
            ORDER BY requested_at_utc DESC, id DESC
            LIMIT 1;
            """;
        AddModelParameters(command, modelId, modelHash);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static async Task<ReviewIdentityMatchRegenerationRun> RequireRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RunSelect +
            "WHERE id = @run_id" +
            (forUpdate ? " FOR UPDATE;" : ";");
        command.Parameters.AddWithValue("run_id", runId);
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Identity regeneration run '{runId:D}' does not exist.");
        }

        return ReadRun(reader);
    }

    private static async Task<ReviewIdentityMatchRegenerationTarget?> ReadNextTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                run_id,
                face_occurrence_id,
                ordinal,
                status,
                suggestion_count,
                error
            FROM identity_match_regeneration_targets
            WHERE run_id = @run_id
              AND status IN (@running_status, @pending_status)
            ORDER BY
                CASE status WHEN @running_status THEN 0 ELSE 1 END,
                ordinal
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue(
            "running_status",
            ReviewIdentityMatchRegenerationTargetStatuses.Running);
        command.Parameters.AddWithValue(
            "pending_status",
            ReviewIdentityMatchRegenerationTargetStatuses.Pending);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReviewIdentityMatchRegenerationTarget(
            reader.GetGuid(0),
            FaceOccurrenceId.From(reader.GetGuid(1)),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static async Task UpdateRunStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        string status,
        string? error,
        DateTimeOffset now,
        bool completed,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE identity_match_regeneration_runs
            SET status = @status,
                completed_at_utc = CASE WHEN @completed THEN @now ELSE completed_at_utc END,
                updated_at_utc = @now,
                error = @error
            WHERE id = @run_id;
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("completed", completed);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ReviewIdentityMatchRegenerationRun ReadRun(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            new ModelId(reader.GetString(1)),
            new Sha256Digest(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetString(4),
            new ReviewIdentityMatchEvidenceVersion(
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8)),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt32(14),
            reader.GetString(15),
            reader.GetFieldValue<DateTimeOffset>(16),
            reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
            reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
            reader.GetFieldValue<DateTimeOffset>(19),
            reader.IsDBNull(20) ? null : reader.GetString(20));

    private const string RunSelect =
        """
        SELECT
            id,
            model_id,
            model_hash,
            policy_version,
            status,
            evidence_review_action_id,
            evidence_suggestion_review_action_id,
            evidence_person_merge_action_id,
            evidence_embedding_id,
            target_count,
            processed_target_count,
            suggested_target_count,
            suggestion_count,
            automatically_assigned_count,
            error_count,
            requested_by,
            requested_at_utc,
            started_at_utc,
            completed_at_utc,
            updated_at_utc,
            error
        FROM identity_match_regeneration_runs
        """;

    private static void AddRunIdentityParameters(
        NpgsqlCommand command,
        Guid runId,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("id", runId);
        AddModelParameters(command, modelId, modelHash);
    }

    private static void AddModelParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
    }

    private static InvalidOperationException AlreadyActive(
        ModelId modelId,
        Sha256Digest modelHash,
        string status,
        Exception? inner = null) =>
        new(
            $"Identity regeneration is already {status} for model '{modelId}' / '{modelHash}'.",
            inner);

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
