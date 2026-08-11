using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Durable control state for browser-triggered identity suggestion regeneration.
/// The repository snapshots the eligible target face IDs and an evidence version at start,
/// allowing a background worker to expose progress and safely resume after process restart.
/// </summary>
public sealed class SqliteIdentityMatchRegenerationRepository
{
    private const string PendingTargetStatus = "pending";
    private const string RunningTargetStatus = "running";
    private const string CompletedTargetStatus = "completed";
    private const string ErrorTargetStatus = "error";

    private readonly SqliteCatalogueDatabase _database;

    public SqliteIdentityMatchRegenerationRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueIdentityMatchRegenerationRun> StartAsync(
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

        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        CatalogueIdentityMatchRegenerationRun? active = await ReadActiveAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        if (active is not null)
        {
            throw new InvalidOperationException(
                $"Identity regeneration is already {active.Status} for model '{modelId}' / '{modelHash}'.");
        }

        IdentityMatchEvidenceVersion evidence = await ReadEvidenceVersionAsync(
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
        using (SqliteCommand insertRun = connection.CreateCommand())
        {
            insertRun.Transaction = transaction;
            insertRun.CommandText = """
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
                    $id,
                    $model_id,
                    $model_hash,
                    $policy_version,
                    $status,
                    $review_action_id,
                    $suggestion_review_action_id,
                    $person_merge_action_id,
                    $embedding_id,
                    $target_count,
                    0,
                    0,
                    0,
                    0,
                    0,
                    $requested_by,
                    $requested_at_utc,
                    NULL,
                    NULL,
                    $updated_at_utc,
                    NULL);
                """;
            AddRunIdentityParameters(insertRun, runId, modelId, modelHash);
            insertRun.Parameters.AddWithValue("$policy_version", policyVersion);
            insertRun.Parameters.AddWithValue("$status", IdentityMatchRegenerationStatuses.Pending);
            insertRun.Parameters.AddWithValue("$review_action_id", evidence.ReviewActionId);
            insertRun.Parameters.AddWithValue("$suggestion_review_action_id", evidence.SuggestionReviewActionId);
            insertRun.Parameters.AddWithValue("$person_merge_action_id", evidence.PersonMergeActionId);
            insertRun.Parameters.AddWithValue("$embedding_id", evidence.EmbeddingId);
            insertRun.Parameters.AddWithValue("$target_count", targets.Count);
            insertRun.Parameters.AddWithValue("$requested_by", actor);
            insertRun.Parameters.AddWithValue("$requested_at_utc", FormatTimestamp(now));
            insertRun.Parameters.AddWithValue("$updated_at_utc", FormatTimestamp(now));
            await insertRun.ExecuteNonQueryAsync(cancellationToken);
        }

        for (int index = 0; index < targets.Count; index++)
        {
            using SqliteCommand insertTarget = connection.CreateCommand();
            insertTarget.Transaction = transaction;
            insertTarget.CommandText = """
                INSERT INTO identity_match_regeneration_targets (
                    run_id,
                    face_occurrence_id,
                    ordinal,
                    status,
                    suggestion_count,
                    error)
                VALUES ($run_id, $face_occurrence_id, $ordinal, $status, 0, NULL);
                """;
            insertTarget.Parameters.AddWithValue("$run_id", runId.ToString("D"));
            insertTarget.Parameters.AddWithValue("$face_occurrence_id", targets[index].ToString());
            insertTarget.Parameters.AddWithValue("$ordinal", index);
            insertTarget.Parameters.AddWithValue("$status", PendingTargetStatus);
            await insertTarget.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return new CatalogueIdentityMatchRegenerationRun(
            runId,
            modelId,
            modelHash,
            policyVersion,
            IdentityMatchRegenerationStatuses.Pending,
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

    public async Task<CatalogueIdentityMatchRegenerationRun?> GetLatestAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        CatalogueIdentityMatchRegenerationRun? result = await ReadLatestAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        transaction.Commit();
        return result;
    }

    public async Task<CatalogueIdentityMatchRegenerationRun?> GetNextActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            {RunSelect}
            WHERE status IN ($pending_status, $running_status)
            ORDER BY requested_at_utc, id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pending_status", IdentityMatchRegenerationStatuses.Pending);
        command.Parameters.AddWithValue("$running_status", IdentityMatchRegenerationStatuses.Running);

        CatalogueIdentityMatchRegenerationRun? result = null;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            result = ReadRun(reader);
        }

        transaction.Commit();
        return result;
    }

    public async Task<CatalogueIdentityMatchRegenerationTarget?> ClaimNextTargetAsync(
        Guid runId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        CatalogueIdentityMatchRegenerationRun run = await RequireRunAsync(
            connection,
            transaction,
            runId,
            cancellationToken);
        if (!run.IsActive)
        {
            transaction.Commit();
            return null;
        }

        IdentityMatchEvidenceVersion currentEvidence = await ReadEvidenceVersionAsync(
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
                IdentityMatchRegenerationStatuses.Stale,
                "Identity evidence changed while regeneration was running. Start a new regeneration from the current catalogue state.",
                now,
                completed: true,
                cancellationToken);
            transaction.Commit();
            return null;
        }

        CatalogueIdentityMatchRegenerationTarget? target = await ReadNextTargetAsync(
            connection,
            transaction,
            runId,
            cancellationToken);
        if (target is null)
        {
            transaction.Commit();
            return null;
        }

        using (SqliteCommand updateTarget = connection.CreateCommand())
        {
            updateTarget.Transaction = transaction;
            updateTarget.CommandText = """
                UPDATE identity_match_regeneration_targets
                SET status = $status,
                    error = NULL
                WHERE run_id = $run_id
                  AND face_occurrence_id = $face_occurrence_id;
                """;
            updateTarget.Parameters.AddWithValue("$status", RunningTargetStatus);
            updateTarget.Parameters.AddWithValue("$run_id", runId.ToString("D"));
            updateTarget.Parameters.AddWithValue("$face_occurrence_id", target.FaceOccurrenceId.ToString());
            await updateTarget.ExecuteNonQueryAsync(cancellationToken);
        }

        using (SqliteCommand updateRun = connection.CreateCommand())
        {
            updateRun.Transaction = transaction;
            updateRun.CommandText = """
                UPDATE identity_match_regeneration_runs
                SET status = $status,
                    started_at_utc = COALESCE(started_at_utc, $now),
                    updated_at_utc = $now
                WHERE id = $run_id;
                """;
            updateRun.Parameters.AddWithValue("$status", IdentityMatchRegenerationStatuses.Running);
            updateRun.Parameters.AddWithValue("$now", FormatTimestamp(now));
            updateRun.Parameters.AddWithValue("$run_id", runId.ToString("D"));
            await updateRun.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return target with { Status = RunningTargetStatus, Error = null };
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
            CompletedTargetStatus,
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
            ErrorTargetStatus,
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
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        using (SqliteCommand remaining = connection.CreateCommand())
        {
            remaining.Transaction = transaction;
            remaining.CommandText = """
                SELECT COUNT(*)
                FROM identity_match_regeneration_targets
                WHERE run_id = $run_id
                  AND status IN ($pending_status, $running_status);
                """;
            remaining.Parameters.AddWithValue("$run_id", runId.ToString("D"));
            remaining.Parameters.AddWithValue("$pending_status", PendingTargetStatus);
            remaining.Parameters.AddWithValue("$running_status", RunningTargetStatus);
            long count = Convert.ToInt64(
                await remaining.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (count != 0)
            {
                throw new InvalidOperationException("Cannot complete identity regeneration while targets remain unfinished.");
            }
        }

        using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE identity_match_regeneration_runs
            SET status = $status,
                automatically_assigned_count = $automatically_assigned_count,
                completed_at_utc = $now,
                updated_at_utc = $now,
                error = NULL
            WHERE id = $run_id
              AND status IN ($pending_status, $running_status);
            """;
        update.Parameters.AddWithValue("$status", IdentityMatchRegenerationStatuses.Completed);
        update.Parameters.AddWithValue("$automatically_assigned_count", automaticallyAssignedCount);
        update.Parameters.AddWithValue("$now", FormatTimestamp(now));
        update.Parameters.AddWithValue("$run_id", runId.ToString("D"));
        update.Parameters.AddWithValue("$pending_status", IdentityMatchRegenerationStatuses.Pending);
        update.Parameters.AddWithValue("$running_status", IdentityMatchRegenerationStatuses.Running);
        await update.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    public async Task MarkFailedAsync(
        Guid runId,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        await UpdateRunStatusAsync(
            connection,
            transaction,
            runId,
            IdentityMatchRegenerationStatuses.Failed,
            Required(error, nameof(error)),
            nowUtc.ToUniversalTime(),
            completed: true,
            cancellationToken);
        transaction.Commit();
    }

    public async Task<bool> EvidenceStillMatchesAsync(
        CatalogueIdentityMatchRegenerationRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        IdentityMatchEvidenceVersion current = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            run.ModelId,
            run.ModelHash,
            cancellationToken);
        transaction.Commit();
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
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        using SqliteCommand updateTarget = connection.CreateCommand();
        updateTarget.Transaction = transaction;
        updateTarget.CommandText = """
            UPDATE identity_match_regeneration_targets
            SET status = $status,
                suggestion_count = $suggestion_count,
                error = $error
            WHERE run_id = $run_id
              AND face_occurrence_id = $face_occurrence_id
              AND status <> $completed_status
              AND status <> $error_status;
            """;
        updateTarget.Parameters.AddWithValue("$status", status);
        updateTarget.Parameters.AddWithValue("$suggestion_count", suggestionCount);
        updateTarget.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        updateTarget.Parameters.AddWithValue("$run_id", runId.ToString("D"));
        updateTarget.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        updateTarget.Parameters.AddWithValue("$completed_status", CompletedTargetStatus);
        updateTarget.Parameters.AddWithValue("$error_status", ErrorTargetStatus);
        int changed = await updateTarget.ExecuteNonQueryAsync(cancellationToken);

        if (changed == 1)
        {
            using SqliteCommand updateRun = connection.CreateCommand();
            updateRun.Transaction = transaction;
            updateRun.CommandText = """
                UPDATE identity_match_regeneration_runs
                SET processed_target_count = processed_target_count + 1,
                    suggested_target_count = suggested_target_count + $suggested_increment,
                    suggestion_count = suggestion_count + $suggestion_count,
                    error_count = error_count + $error_increment,
                    updated_at_utc = $now
                WHERE id = $run_id;
                """;
            updateRun.Parameters.AddWithValue("$suggested_increment", suggestionCount > 0 ? 1 : 0);
            updateRun.Parameters.AddWithValue("$suggestion_count", suggestionCount);
            updateRun.Parameters.AddWithValue("$error_increment", status == ErrorTargetStatus ? 1 : 0);
            updateRun.Parameters.AddWithValue("$now", FormatTimestamp(now));
            updateRun.Parameters.AddWithValue("$run_id", runId.ToString("D"));
            await updateRun.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS identity_match_regeneration_runs (
                id TEXT NOT NULL PRIMARY KEY,
                model_id TEXT NOT NULL,
                model_hash TEXT NOT NULL,
                policy_version INTEGER NOT NULL CHECK (policy_version >= 1),
                status TEXT NOT NULL CHECK (status IN ('pending', 'running', 'completed', 'stale', 'failed')),
                evidence_review_action_id INTEGER NOT NULL,
                evidence_suggestion_review_action_id INTEGER NOT NULL,
                evidence_person_merge_action_id INTEGER NOT NULL,
                evidence_embedding_id INTEGER NOT NULL,
                target_count INTEGER NOT NULL CHECK (target_count >= 0),
                processed_target_count INTEGER NOT NULL CHECK (processed_target_count >= 0),
                suggested_target_count INTEGER NOT NULL CHECK (suggested_target_count >= 0),
                suggestion_count INTEGER NOT NULL CHECK (suggestion_count >= 0),
                automatically_assigned_count INTEGER NOT NULL CHECK (automatically_assigned_count >= 0),
                error_count INTEGER NOT NULL CHECK (error_count >= 0),
                requested_by TEXT NOT NULL,
                requested_at_utc TEXT NOT NULL,
                started_at_utc TEXT NULL,
                completed_at_utc TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                error TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS identity_match_regeneration_targets (
                run_id TEXT NOT NULL,
                face_occurrence_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
                status TEXT NOT NULL CHECK (status IN ('pending', 'running', 'completed', 'error')),
                suggestion_count INTEGER NOT NULL CHECK (suggestion_count >= 0),
                error TEXT NULL,
                PRIMARY KEY (run_id, face_occurrence_id),
                UNIQUE (run_id, ordinal),
                FOREIGN KEY (run_id) REFERENCES identity_match_regeneration_runs (id) ON DELETE CASCADE,
                FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_match_regeneration_active_model
                ON identity_match_regeneration_runs (model_id, model_hash)
                WHERE status IN ('pending', 'running');

            CREATE INDEX IF NOT EXISTS ix_identity_match_regeneration_status
                ON identity_match_regeneration_runs (status, requested_at_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<FaceOccurrenceId>> ReadEligibleTargetIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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
                WHERE embedding.model_id = $model_id
                  AND embedding.model_hash = $model_hash
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
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        List<FaceOccurrenceId> targets = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))));
        }

        return targets;
    }

    private static async Task<IdentityMatchEvidenceVersion> ReadEvidenceVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                COALESCE((SELECT MAX(id) FROM review_actions), 0),
                COALESCE((SELECT MAX(id) FROM identity_suggestion_review_actions), 0),
                COALESCE((SELECT MAX(id) FROM person_maintenance_actions WHERE action_kind = 'merge'), 0),
                COALESCE((
                    SELECT MAX(embedding.id)
                    FROM embeddings AS embedding
                    WHERE embedding.model_id = $model_id
                      AND embedding.model_hash = $model_hash), 0);
            """;
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not read identity evidence version.");
        }

        return new IdentityMatchEvidenceVersion(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task<CatalogueIdentityMatchRegenerationRun?> ReadActiveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            {RunSelect}
            WHERE model_id = $model_id
              AND model_hash = $model_hash
              AND status IN ($pending_status, $running_status)
            ORDER BY requested_at_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("$pending_status", IdentityMatchRegenerationStatuses.Pending);
        command.Parameters.AddWithValue("$running_status", IdentityMatchRegenerationStatuses.Running);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static async Task<CatalogueIdentityMatchRegenerationRun?> ReadLatestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            {RunSelect}
            WHERE model_id = $model_id
              AND model_hash = $model_hash
            ORDER BY requested_at_utc DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static async Task<CatalogueIdentityMatchRegenerationRun> RequireRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            {RunSelect}
            WHERE id = $run_id;
            """;
        command.Parameters.AddWithValue("$run_id", runId.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Identity regeneration run '{runId:D}' does not exist.");
        }

        return ReadRun(reader);
    }

    private static async Task<CatalogueIdentityMatchRegenerationTarget?> ReadNextTargetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT run_id, face_occurrence_id, ordinal, status, suggestion_count, error
            FROM identity_match_regeneration_targets
            WHERE run_id = $run_id
              AND status IN ($running_status, $pending_status)
            ORDER BY
                CASE status WHEN $running_status THEN 0 ELSE 1 END,
                ordinal
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$run_id", runId.ToString("D"));
        command.Parameters.AddWithValue("$running_status", RunningTargetStatus);
        command.Parameters.AddWithValue("$pending_status", PendingTargetStatus);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueIdentityMatchRegenerationTarget(
            Guid.Parse(reader.GetString(0)),
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static async Task UpdateRunStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid runId,
        string status,
        string? error,
        DateTimeOffset now,
        bool completed,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE identity_match_regeneration_runs
            SET status = $status,
                completed_at_utc = CASE WHEN $completed = 1 THEN $now ELSE completed_at_utc END,
                updated_at_utc = $now,
                error = $error
            WHERE id = $run_id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
        command.Parameters.AddWithValue("$now", FormatTimestamp(now));
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$run_id", runId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CatalogueIdentityMatchRegenerationRun ReadRun(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            new ModelId(reader.GetString(1)),
            new Sha256Digest(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetString(4),
            new IdentityMatchEvidenceVersion(
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
            ParseTimestamp(reader.GetString(16)),
            reader.IsDBNull(17) ? null : ParseTimestamp(reader.GetString(17)),
            reader.IsDBNull(18) ? null : ParseTimestamp(reader.GetString(18)),
            ParseTimestamp(reader.GetString(19)),
            reader.IsDBNull(20) ? null : reader.GetString(20));

    private const string RunSelect = """
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
        SqliteCommand command,
        Guid runId,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("$id", runId.ToString("D"));
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
    }

    private static string Required(string value, string parameterName)
    {
        string normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return normalized;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
}
