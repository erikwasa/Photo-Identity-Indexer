using System.Buffers.Binary;
using System.Data;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL authority for provisional face-cluster runs and memberships. A run captures one
/// exact embedding/review evidence version. Replacement evidence becomes current only after all
/// memberships are durably written and the captured evidence still matches the catalogue.
/// </summary>
public sealed class PostgresProvisionalFaceClusterRepository : IProvisionalFaceClusterRepository
{
    private const int MaximumGroups = 5000;
    private const int MembershipInsertBatchSize = 250;
    private readonly PostgresCatalogueDatabase _database;

    public PostgresProvisionalFaceClusterRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ProvisionalFaceClusterRun> StartAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ProvisionalFaceClusterPolicy policy,
        bool includeUnknown,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        string actor = Required(requestedBy, nameof(requestedBy));
        DateTimeOffset now = requestedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        ProvisionalFaceClusterRun? active = await ReadLatestAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            policy.Version,
            includeUnknown,
            activeOnly: true,
            cancellationToken);
        if (active is not null)
        {
            throw new InvalidOperationException(
                $"A provisional clustering run is already active for model '{modelId}', policy '{policy.Version}', includeUnknown={includeUnknown}.");
        }

        ProvisionalFaceClusterEvidenceVersion evidence = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        int targetCount = await CountEligibleFacesAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            evidence,
            includeUnknown,
            cancellationToken);
        if (targetCount > ProvisionalFaceClusterPolicies.MaximumFacesPerRun)
        {
            throw new InvalidOperationException(
                $"Eligible provisional-cluster input contains {targetCount:N0} faces, exceeding the bounded limit of {ProvisionalFaceClusterPolicies.MaximumFacesPerRun:N0}. " +
                "Measure and deliberately revise the production retrieval strategy before raising this safety bound.");
        }

        Guid runId = Guid.NewGuid();
        try
        {
            await using NpgsqlCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO provisional_face_cluster_runs (
                    id,
                    model_id,
                    model_hash,
                    policy_version,
                    algorithm,
                    minimum_cluster_size,
                    minimum_samples,
                    distance_threshold,
                    mutual_neighbor_count,
                    minimum_shared_neighbors,
                    include_unknown,
                    status,
                    evidence_review_action_id,
                    evidence_review_mutation_version,
                    evidence_embedding_id,
                    target_count,
                    processed_target_count,
                    cluster_count,
                    noise_count,
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
                    @algorithm,
                    @minimum_cluster_size,
                    @minimum_samples,
                    @distance_threshold,
                    @mutual_neighbor_count,
                    @minimum_shared_neighbors,
                    @include_unknown,
                    @status,
                    @evidence_review_action_id,
                    @evidence_review_mutation_version,
                    @evidence_embedding_id,
                    @target_count,
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
            AddRunIdentityParameters(insert, runId, modelId, modelHash, policy.Version, includeUnknown);
            AddPolicyParameters(insert, policy);
            insert.Parameters.AddWithValue("status", ProvisionalFaceClusterRunStatuses.Pending);
            AddEvidenceParameters(insert, evidence);
            insert.Parameters.AddWithValue("target_count", targetCount);
            insert.Parameters.AddWithValue("requested_by", actor);
            insert.Parameters.AddWithValue("requested_at_utc", now);
            insert.Parameters.AddWithValue("updated_at_utc", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException(
                "An equivalent provisional clustering run became active concurrently.",
                exception);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ProvisionalFaceClusterRun(
            runId,
            modelId,
            modelHash,
            policy,
            includeUnknown,
            ProvisionalFaceClusterRunStatuses.Pending,
            evidence,
            targetCount,
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

    public async Task<ProvisionalFaceClusterRun?> GetLatestAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        CancellationToken cancellationToken = default)
    {
        string version = Required(policyVersion, nameof(policyVersion));
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        ProvisionalFaceClusterRun? result = await ReadLatestAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            version,
            includeUnknown,
            activeOnly: false,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ProvisionalFaceClusterRun?> GetNextActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        ProvisionalFaceClusterRun? result = null;
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = RunSelect +
                """
                WHERE run.status IN (@pending, @running)
                ORDER BY run.requested_at_utc, run.id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("pending", ProvisionalFaceClusterRunStatuses.Pending);
            command.Parameters.AddWithValue("running", ProvisionalFaceClusterRunStatuses.Running);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                result = ReadRun(reader);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ProvisionalFaceClusterRun?> TryStartNextRefreshAsync(
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        TimeSpan? minimumReviewQuietPeriod = null,
        CancellationToken cancellationToken = default)
    {
        string actor = Required(requestedBy, nameof(requestedBy));
        TimeSpan reviewQuietPeriod = minimumReviewQuietPeriod ?? TimeSpan.Zero;
        if (reviewQuietPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumReviewQuietPeriod),
                "The automatic refresh review quiet period cannot be negative.");
        }

        List<ProvisionalFaceClusterRun> currentRuns = [];
        await using (NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken))
        await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await EnsureSchemaAsync(connection, transaction, cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = RunSelect +
                """
                INNER JOIN provisional_face_cluster_current AS current_scope
                    ON current_scope.run_id = run.id
                ORDER BY current_scope.updated_at_utc, run.id;
                """;
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    currentRuns.Add(ReadRun(reader));
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        foreach (ProvisionalFaceClusterRun current in currentRuns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProvisionalFaceClusterEvidenceVersion currentEvidence =
                await ReadCurrentEvidenceVersionAsync(
                    current.ModelId,
                    current.ModelHash,
                    cancellationToken);
            if (currentEvidence == current.EvidenceVersion)
            {
                continue;
            }

            if (ReviewEvidenceChanged(current.EvidenceVersion, currentEvidence) &&
                IsWithinReviewQuietPeriod(
                    currentEvidence.ReviewMutationVersion,
                    requestedAtUtc,
                    reviewQuietPeriod))
            {
                continue;
            }

            try
            {
                return await StartAsync(
                    current.ModelId,
                    current.ModelHash,
                    current.Policy,
                    current.IncludeUnknown,
                    actor,
                    requestedAtUtc,
                    cancellationToken);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("already active", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("became active", StringComparison.OrdinalIgnoreCase))
            {
                // Another host/process won the unique active-scope race. The durable active run
                // will be discovered normally on the next worker cycle.
                return null;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<ProvisionalFaceClusterInputFace>> ReadInputSnapshotAsync(
        ProvisionalFaceClusterRun run,
        int maximumFaces = ProvisionalFaceClusterPolicies.MaximumFacesPerRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (maximumFaces is < 1 or > ProvisionalFaceClusterPolicies.MaximumFacesPerRun)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFaces));
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        ProvisionalFaceClusterRun persisted = await RequireRunAsync(
            connection,
            transaction,
            run.Id,
            forUpdate: true,
            cancellationToken);
        if (!persisted.IsActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        ProvisionalFaceClusterEvidenceVersion currentEvidence = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            persisted.ModelId,
            persisted.ModelHash,
            cancellationToken);
        if (currentEvidence != persisted.EvidenceVersion)
        {
            await UpdateRunTerminalStatusAsync(
                connection,
                transaction,
                persisted.Id,
                ProvisionalFaceClusterRunStatuses.Stale,
                "Canonical review or exact-model embedding evidence changed before clustering began; a replacement run is required.",
                DateTimeOffset.UtcNow,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        List<ProvisionalFaceClusterInputFace> result = await ReadEligibleFacesAsync(
            connection,
            transaction,
            persisted,
            maximumFaces,
            cancellationToken);
        if (result.Count != persisted.TargetCount)
        {
            await UpdateRunTerminalStatusAsync(
                connection,
                transaction,
                persisted.Id,
                ProvisionalFaceClusterRunStatuses.Stale,
                "The captured provisional-cluster input no longer matches its durable target count.",
                DateTimeOffset.UtcNow,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        await using NpgsqlCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE provisional_face_cluster_runs
            SET status = @running,
                started_at_utc = COALESCE(started_at_utc, @now),
                updated_at_utc = @now
            WHERE id = @id
              AND status IN (@pending, @running);
            """;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        update.Parameters.AddWithValue("running", ProvisionalFaceClusterRunStatuses.Running);
        update.Parameters.AddWithValue("pending", ProvisionalFaceClusterRunStatuses.Pending);
        update.Parameters.AddWithValue("now", now);
        update.Parameters.AddWithValue("id", persisted.Id);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task ReportProgressAsync(
        Guid runId,
        int processedTargetCount,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (processedTargetCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processedTargetCount));
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE provisional_face_cluster_runs
            SET processed_target_count = GREATEST(
                    processed_target_count,
                    LEAST(target_count, @processed)),
                updated_at_utc = @updated
            WHERE id = @id
              AND status IN (@pending, @running);
            """;
        command.Parameters.AddWithValue("processed", processedTargetCount);
        command.Parameters.AddWithValue("updated", updatedAtUtc.ToUniversalTime());
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("pending", ProvisionalFaceClusterRunStatuses.Pending);
        command.Parameters.AddWithValue("running", ProvisionalFaceClusterRunStatuses.Running);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> CompleteAsync(
        ProvisionalFaceClusterRun run,
        ProvisionalFaceClusterComputation computation,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(computation);
        if (computation.EvaluatedFaceCount != computation.Memberships.Count)
        {
            throw new ArgumentException(
                "Provisional cluster computation face count does not match its memberships.",
                nameof(computation));
        }

        if (computation.Memberships.Select(member => member.FaceOccurrenceId).Distinct().Count() !=
            computation.Memberships.Count)
        {
            throw new ArgumentException(
                "Provisional cluster computation contains duplicate face memberships.",
                nameof(computation));
        }

        DateTimeOffset now = completedAtUtc.ToUniversalTime();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        ProvisionalFaceClusterRun persisted = await RequireRunAsync(
            connection,
            transaction,
            run.Id,
            forUpdate: true,
            cancellationToken);
        if (!persisted.IsActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        ProvisionalFaceClusterEvidenceVersion currentEvidence = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            persisted.ModelId,
            persisted.ModelHash,
            cancellationToken);
        if (currentEvidence != persisted.EvidenceVersion)
        {
            await UpdateRunTerminalStatusAsync(
                connection,
                transaction,
                persisted.Id,
                ProvisionalFaceClusterRunStatuses.Stale,
                "Canonical review or exact-model embedding evidence changed while provisional clustering was running; derived memberships were not published.",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        if (computation.EvaluatedFaceCount != persisted.TargetCount)
        {
            throw new InvalidOperationException(
                $"Provisional clustering evaluated {computation.EvaluatedFaceCount} faces but the durable snapshot contains {persisted.TargetCount} targets.");
        }

        await using (NpgsqlCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM provisional_face_cluster_members WHERE run_id = @run_id;";
            delete.Parameters.AddWithValue("run_id", persisted.Id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        for (int offset = 0; offset < computation.Memberships.Count; offset += MembershipInsertBatchSize)
        {
            int count = Math.Min(MembershipInsertBatchSize, computation.Memberships.Count - offset);
            await InsertMembershipBatchAsync(
                connection,
                transaction,
                persisted.Id,
                computation.Memberships,
                offset,
                count,
                cancellationToken);
        }

        Guid? previousRunId = null;
        await using (NpgsqlCommand current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText =
                """
                SELECT run_id
                FROM provisional_face_cluster_current
                WHERE model_id = @model_id
                  AND model_hash = @model_hash
                  AND policy_version = @policy_version
                  AND include_unknown = @include_unknown
                FOR UPDATE;
                """;
            AddScopeParameters(
                current,
                persisted.ModelId,
                persisted.ModelHash,
                persisted.Policy.Version,
                persisted.IncludeUnknown);
            object? value = await current.ExecuteScalarAsync(cancellationToken);
            if (value is Guid id)
            {
                previousRunId = id;
            }
        }

        if (previousRunId is Guid previous && previous != persisted.Id)
        {
            await using NpgsqlCommand supersede = connection.CreateCommand();
            supersede.Transaction = transaction;
            supersede.CommandText =
                """
                UPDATE provisional_face_cluster_runs
                SET status = @status,
                    updated_at_utc = @now
                WHERE id = @id
                  AND status = @completed;
                """;
            supersede.Parameters.AddWithValue("status", ProvisionalFaceClusterRunStatuses.Superseded);
            supersede.Parameters.AddWithValue("completed", ProvisionalFaceClusterRunStatuses.Completed);
            supersede.Parameters.AddWithValue("now", now);
            supersede.Parameters.AddWithValue("id", previous);
            await supersede.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand publish = connection.CreateCommand())
        {
            publish.Transaction = transaction;
            publish.CommandText =
                """
                INSERT INTO provisional_face_cluster_current (
                    model_id,
                    model_hash,
                    policy_version,
                    include_unknown,
                    run_id,
                    updated_at_utc)
                VALUES (
                    @model_id,
                    @model_hash,
                    @policy_version,
                    @include_unknown,
                    @run_id,
                    @now)
                ON CONFLICT (model_id, model_hash, policy_version, include_unknown)
                DO UPDATE SET
                    run_id = EXCLUDED.run_id,
                    updated_at_utc = EXCLUDED.updated_at_utc;
                """;
            AddScopeParameters(
                publish,
                persisted.ModelId,
                persisted.ModelHash,
                persisted.Policy.Version,
                persisted.IncludeUnknown);
            publish.Parameters.AddWithValue("run_id", persisted.Id);
            publish.Parameters.AddWithValue("now", now);
            await publish.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand complete = connection.CreateCommand())
        {
            complete.Transaction = transaction;
            complete.CommandText =
                """
                UPDATE provisional_face_cluster_runs
                SET status = @status,
                    processed_target_count = target_count,
                    cluster_count = @cluster_count,
                    noise_count = @noise_count,
                    completed_at_utc = @now,
                    updated_at_utc = @now,
                    error = NULL
                WHERE id = @id;
                """;
            complete.Parameters.AddWithValue("status", ProvisionalFaceClusterRunStatuses.Completed);
            complete.Parameters.AddWithValue("cluster_count", computation.ClusterCount);
            complete.Parameters.AddWithValue("noise_count", computation.NoiseCount);
            complete.Parameters.AddWithValue("now", now);
            complete.Parameters.AddWithValue("id", persisted.Id);
            await complete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task MarkFailedAsync(
        Guid runId,
        string error,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default)
    {
        string message = Required(error, nameof(error));
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        _ = await RequireRunAsync(connection, transaction, runId, forUpdate: true, cancellationToken);
        await UpdateRunTerminalStatusAsync(
            connection,
            transaction,
            runId,
            ProvisionalFaceClusterRunStatuses.Failed,
            message,
            failedAtUtc.ToUniversalTime(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProvisionalFaceClusterGroupSummary>> ListCurrentGroupsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        int maximumGroups = 500,
        CancellationToken cancellationToken = default)
    {
        if (maximumGroups is < 1 or > MaximumGroups)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumGroups));
        }

        string version = Required(policyVersion, nameof(policyVersion));
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                current_scope.run_id,
                member.derived_cluster_key,
                COUNT(*)::integer AS member_count,
                COUNT(*) FILTER (WHERE member.role = 'core')::integer AS core_count,
                COUNT(*) FILTER (WHERE member.role = 'border')::integer AS border_count
            FROM provisional_face_cluster_current AS current_scope
            INNER JOIN provisional_face_cluster_members AS member
                ON member.run_id = current_scope.run_id
            WHERE current_scope.model_id = @model_id
              AND current_scope.model_hash = @model_hash
              AND current_scope.policy_version = @policy_version
              AND current_scope.include_unknown = @include_unknown
              AND member.derived_cluster_key IS NOT NULL
            GROUP BY current_scope.run_id, member.derived_cluster_key
            ORDER BY member_count DESC, member.derived_cluster_key
            LIMIT @maximum_groups;
            """;
        AddScopeParameters(command, modelId, modelHash, version, includeUnknown);
        command.Parameters.AddWithValue("maximum_groups", maximumGroups);

        List<ProvisionalFaceClusterGroupSummary> result = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> EvidenceStillMatchesAsync(
        ProvisionalFaceClusterRun run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ProvisionalFaceClusterEvidenceVersion current = await ReadCurrentEvidenceVersionAsync(
            run.ModelId,
            run.ModelHash,
            cancellationToken);
        return current == run.EvidenceVersion;
    }

    private async Task<ProvisionalFaceClusterEvidenceVersion> ReadCurrentEvidenceVersionAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        ProvisionalFaceClusterEvidenceVersion current = await ReadEvidenceVersionAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return current;
    }

    private static bool ReviewEvidenceChanged(
        ProvisionalFaceClusterEvidenceVersion captured,
        ProvisionalFaceClusterEvidenceVersion current) =>
        captured.ReviewActionId != current.ReviewActionId ||
        captured.ReviewMutationVersion != current.ReviewMutationVersion;

    private static bool IsWithinReviewQuietPeriod(
        long reviewMutationVersion,
        DateTimeOffset requestedAtUtc,
        TimeSpan reviewQuietPeriod)
    {
        if (reviewQuietPeriod <= TimeSpan.Zero || reviewMutationVersion <= 0)
        {
            return false;
        }

        try
        {
            DateTimeOffset latestReviewMutationUtc = DateTimeOffset.UnixEpoch.AddTicks(
                checked(reviewMutationVersion * 10L));
            return requestedAtUtc.ToUniversalTime() < latestReviewMutationUtc + reviewQuietPeriod;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static async Task EnsureSchemaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ProvisionalFaceClusterSchema.Sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ProvisionalFaceClusterEvidenceVersion> ReadEvidenceVersionAsync(
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
                COALESCE((
                    SELECT MAX((
                        EXTRACT(EPOCH FROM GREATEST(
                            created_at_utc,
                            COALESCE(reversed_at_utc, created_at_utc))) * 1000000)::bigint)
                    FROM review_actions), 0),
                COALESCE((
                    SELECT MAX(embedding.id)
                    FROM embeddings AS embedding
                    WHERE embedding.model_id = @model_id
                      AND embedding.model_hash = @model_hash), 0);
            """;
        AddModelParameters(command, modelId, modelHash);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not read provisional-cluster evidence version.");
        }

        return new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2));
    }

    private static async Task<int> CountEligibleFacesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        ProvisionalFaceClusterEvidenceVersion evidence,
        bool includeUnknown,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = EligibleFaceCtes +
            """
            SELECT COUNT(*)::integer
            FROM eligible_faces;
            """;
        AddEligibilityParameters(command, modelId, modelHash, evidence, includeUnknown);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<List<ProvisionalFaceClusterInputFace>> ReadEligibleFacesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProvisionalFaceClusterRun run,
        int maximumFaces,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = EligibleFaceCtes +
            """
            SELECT
                eligible.face_occurrence_id,
                eligible.asset_revision_id,
                eligible.review_state,
                eligible.dimensions,
                eligible.l2_norm,
                eligible.vector_blob
            FROM eligible_faces AS eligible
            ORDER BY eligible.face_occurrence_id
            LIMIT @maximum_plus_one;
            """;
        AddEligibilityParameters(
            command,
            run.ModelId,
            run.ModelHash,
            run.EvidenceVersion,
            run.IncludeUnknown);
        command.Parameters.AddWithValue("maximum_plus_one", maximumFaces + 1);

        List<ProvisionalFaceClusterInputFace> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (result.Count == maximumFaces)
            {
                throw new InvalidOperationException(
                    $"Provisional clustering input exceeds the bounded limit of {maximumFaces:N0} faces.");
            }

            result.Add(new(
                FaceOccurrenceId.From(reader.GetGuid(0)),
                AssetRevisionId.From(reader.GetGuid(1)),
                reader.GetString(2),
                ReadVector(reader, 3, 4, 5)));
        }

        return result;
    }

    private static async Task InsertMembershipBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        IReadOnlyList<ProvisionalFaceClusterComputedMembership> memberships,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        StringBuilder sql = new(
            "INSERT INTO provisional_face_cluster_members (run_id, face_occurrence_id, derived_cluster_key, role) VALUES ");
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("run_id", runId);

        for (int relative = 0; relative < count; relative++)
        {
            int index = offset + relative;
            ProvisionalFaceClusterComputedMembership membership = memberships[index];
            if (relative > 0)
            {
                sql.Append(',');
            }

            sql.Append($"(@run_id, @face_{relative}, @key_{relative}, @role_{relative})");
            command.Parameters.AddWithValue($"face_{relative}", membership.FaceOccurrenceId.Value);
            command.Parameters.AddWithValue(
                $"key_{relative}",
                NpgsqlDbType.Text,
                (object?)membership.DerivedClusterKey ?? DBNull.Value);
            command.Parameters.AddWithValue(
                $"role_{relative}",
                membership.Role.ToString().ToLowerInvariant());
        }

        sql.Append(';');
        command.CommandText = sql.ToString();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ProvisionalFaceClusterRun?> ReadLatestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RunSelect +
            """
            WHERE run.model_id = @model_id
              AND run.model_hash = @model_hash
              AND run.policy_version = @policy_version
              AND run.include_unknown = @include_unknown
            """ + "\n" +
            (activeOnly
                ? "AND run.status IN (@pending, @running)\n"
                : string.Empty) +
            "ORDER BY run.requested_at_utc DESC, run.id DESC LIMIT 1;";
        AddScopeParameters(command, modelId, modelHash, policyVersion, includeUnknown);
        if (activeOnly)
        {
            command.Parameters.AddWithValue("pending", ProvisionalFaceClusterRunStatuses.Pending);
            command.Parameters.AddWithValue("running", ProvisionalFaceClusterRunStatuses.Running);
        }

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static async Task<ProvisionalFaceClusterRun> RequireRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RunSelect +
            "WHERE run.id = @id" +
            (forUpdate ? " FOR UPDATE;" : ";");
        command.Parameters.AddWithValue("id", runId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Provisional clustering run '{runId:D}' does not exist.");
        }

        return ReadRun(reader);
    }

    private static async Task UpdateRunTerminalStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        string status,
        string error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE provisional_face_cluster_runs
            SET status = @status,
                completed_at_utc = @now,
                updated_at_utc = @now,
                error = @error
            WHERE id = @id
              AND status IN (@pending, @running);
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("error", error);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("pending", ProvisionalFaceClusterRunStatuses.Pending);
        command.Parameters.AddWithValue("running", ProvisionalFaceClusterRunStatuses.Running);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ProvisionalFaceClusterRun ReadRun(NpgsqlDataReader reader)
    {
        ProvisionalFaceClusterPolicy policy = new(
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9));
        return new(
            reader.GetGuid(0),
            new ModelId(reader.GetString(1)),
            new Sha256Digest(reader.GetString(2)),
            policy,
            reader.GetBoolean(10),
            reader.GetString(11),
            new ProvisionalFaceClusterEvidenceVersion(
                reader.GetInt64(12),
                reader.GetInt64(13),
                reader.GetInt64(14)),
            reader.GetInt32(15),
            reader.GetInt32(16),
            reader.GetInt32(17),
            reader.GetInt32(18),
            reader.GetString(19),
            reader.GetFieldValue<DateTimeOffset>(20),
            reader.IsDBNull(21) ? null : reader.GetFieldValue<DateTimeOffset>(21),
            reader.IsDBNull(22) ? null : reader.GetFieldValue<DateTimeOffset>(22),
            reader.GetFieldValue<DateTimeOffset>(23),
            reader.IsDBNull(24) ? null : reader.GetString(24));
    }

    private static EmbeddingVector ReadVector(
        NpgsqlDataReader reader,
        int dimensionsOrdinal,
        int normOrdinal,
        int blobOrdinal)
    {
        int dimensions = reader.GetInt32(dimensionsOrdinal);
        double storedNorm = reader.GetDouble(normOrdinal);
        byte[] bytes = reader.GetFieldValue<byte[]>(blobOrdinal);
        if (dimensions <= 0 || bytes.Length != checked(dimensions * sizeof(float)))
        {
            throw new DataException("Stored provisional-cluster embedding dimensions do not match vector data.");
        }

        float[] values = new float[dimensions];
        for (int index = 0; index < dimensions; index++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)));
            values[index] = BitConverter.Int32BitsToSingle(bits);
        }

        EmbeddingVector vector = new(values);
        double tolerance = 1e-9 * Math.Max(1, storedNorm);
        if (Math.Abs(vector.L2Norm - storedNorm) > tolerance)
        {
            throw new DataException("Stored provisional-cluster embedding norm does not match vector data.");
        }

        return vector;
    }

    private static void AddRunIdentityParameters(
        NpgsqlCommand command,
        Guid runId,
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown)
    {
        command.Parameters.AddWithValue("id", runId);
        AddScopeParameters(command, modelId, modelHash, policyVersion, includeUnknown);
    }

    private static void AddScopeParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown)
    {
        AddModelParameters(command, modelId, modelHash);
        command.Parameters.AddWithValue("policy_version", policyVersion);
        command.Parameters.AddWithValue("include_unknown", includeUnknown);
    }

    private static void AddModelParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
    }

    private static void AddPolicyParameters(NpgsqlCommand command, ProvisionalFaceClusterPolicy policy)
    {
        command.Parameters.AddWithValue("algorithm", policy.Algorithm);
        command.Parameters.AddWithValue("minimum_cluster_size", policy.MinimumClusterSize);
        command.Parameters.AddWithValue("minimum_samples", policy.MinimumSamples);
        command.Parameters.AddWithValue(
            "distance_threshold",
            NpgsqlDbType.Double,
            (object?)policy.DistanceThreshold ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "mutual_neighbor_count",
            NpgsqlDbType.Integer,
            (object?)policy.MutualNeighborCount ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "minimum_shared_neighbors",
            NpgsqlDbType.Integer,
            (object?)policy.MinimumSharedNeighbors ?? DBNull.Value);
    }

    private static void AddEvidenceParameters(
        NpgsqlCommand command,
        ProvisionalFaceClusterEvidenceVersion evidence)
    {
        command.Parameters.AddWithValue("evidence_review_action_id", evidence.ReviewActionId);
        command.Parameters.AddWithValue(
            "evidence_review_mutation_version",
            evidence.ReviewMutationVersion);
        command.Parameters.AddWithValue("evidence_embedding_id", evidence.EmbeddingId);
    }

    private static void AddEligibilityParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash,
        ProvisionalFaceClusterEvidenceVersion evidence,
        bool includeUnknown)
    {
        AddModelParameters(command, modelId, modelHash);
        AddEvidenceParameters(command, evidence);
        command.Parameters.AddWithValue("include_unknown", includeUnknown);
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private const string RunSelect =
        """
        SELECT
            run.id,
            run.model_id,
            run.model_hash,
            run.policy_version,
            run.algorithm,
            run.minimum_cluster_size,
            run.minimum_samples,
            run.distance_threshold,
            run.mutual_neighbor_count,
            run.minimum_shared_neighbors,
            run.include_unknown,
            run.status,
            run.evidence_review_action_id,
            run.evidence_review_mutation_version,
            run.evidence_embedding_id,
            run.target_count,
            run.processed_target_count,
            run.cluster_count,
            run.noise_count,
            run.requested_by,
            run.requested_at_utc,
            run.started_at_utc,
            run.completed_at_utc,
            run.updated_at_utc,
            run.error
        FROM provisional_face_cluster_runs AS run
        """ + "\n";

    private const string EligibleFaceCtes =
        """
        WITH latest_review AS (
            SELECT
                action.face_occurrence_id,
                action.action_kind,
                ROW_NUMBER() OVER (
                    PARTITION BY action.face_occurrence_id
                    ORDER BY action.id DESC) AS row_number
            FROM review_actions AS action
            WHERE action.id <= @evidence_review_action_id
              AND action.action_kind IN ('assign', 'unknown', 'reject')
              AND action.reversed_at_utc IS NULL
        ),
        legacy_confirmed AS (
            SELECT DISTINCT label.face_occurrence_id
            FROM person_labels AS label
            WHERE label.label_kind = 'confirmed'
              AND NOT EXISTS (
                  SELECT 1
                  FROM review_actions AS action
                  WHERE action.face_occurrence_id = label.face_occurrence_id
                    AND action.id <= @evidence_review_action_id)
        ),
        matching_embeddings AS (
            SELECT
                crop.face_occurrence_id,
                embedding.dimensions,
                embedding.l2_norm,
                embedding.vector_blob,
                ROW_NUMBER() OVER (
                    PARTITION BY crop.face_occurrence_id
                    ORDER BY embedding.created_at_utc DESC, embedding.id DESC) AS row_number
            FROM face_crops AS crop
            INNER JOIN embeddings AS embedding
                ON embedding.face_crop_id = crop.id
            WHERE embedding.model_id = @model_id
              AND embedding.model_hash = @model_hash
              AND embedding.id <= @evidence_embedding_id
        ),
        eligible_faces AS (
            SELECT
                occurrence.id AS face_occurrence_id,
                occurrence.asset_revision_id,
                CASE
                    WHEN review.action_kind = 'unknown' THEN 'unknown'
                    ELSE 'unreviewed'
                END AS review_state,
                matching.dimensions,
                matching.l2_norm,
                matching.vector_blob
            FROM matching_embeddings AS matching
            INNER JOIN face_occurrences AS occurrence
                ON occurrence.id = matching.face_occurrence_id
            LEFT JOIN latest_review AS review
                ON review.face_occurrence_id = occurrence.id
               AND review.row_number = 1
            WHERE matching.row_number = 1
              AND (review.action_kind IS NULL OR (@include_unknown AND review.action_kind = 'unknown'))
              AND NOT EXISTS (
                  SELECT 1
                  FROM legacy_confirmed AS confirmed
                  WHERE confirmed.face_occurrence_id = occurrence.id)
        )
        """ + "\n";

    internal static class ProvisionalFaceClusterSchema
    {
        public const string Sql =
            """
            CREATE TABLE IF NOT EXISTS provisional_face_cluster_runs (
                id uuid NOT NULL PRIMARY KEY,
                model_id text NOT NULL CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                policy_version text NOT NULL CHECK (btrim(policy_version) <> ''),
                algorithm text NOT NULL CHECK (btrim(algorithm) <> ''),
                minimum_cluster_size integer NOT NULL CHECK (minimum_cluster_size >= 2),
                minimum_samples integer NOT NULL CHECK (minimum_samples >= 1),
                distance_threshold double precision NULL
                    CHECK (distance_threshold IS NULL OR distance_threshold BETWEEN 0 AND 2),
                mutual_neighbor_count integer NULL
                    CHECK (mutual_neighbor_count IS NULL OR mutual_neighbor_count >= 1),
                minimum_shared_neighbors integer NULL
                    CHECK (minimum_shared_neighbors IS NULL OR minimum_shared_neighbors >= 0),
                include_unknown boolean NOT NULL,
                status text NOT NULL
                    CHECK (status IN ('pending', 'running', 'completed', 'superseded', 'stale', 'failed')),
                evidence_review_action_id bigint NOT NULL CHECK (evidence_review_action_id >= 0),
                evidence_review_mutation_version bigint NOT NULL CHECK (evidence_review_mutation_version >= 0),
                evidence_embedding_id bigint NOT NULL CHECK (evidence_embedding_id >= 0),
                target_count integer NOT NULL CHECK (target_count >= 0),
                processed_target_count integer NOT NULL CHECK (processed_target_count >= 0),
                cluster_count integer NOT NULL CHECK (cluster_count >= 0),
                noise_count integer NOT NULL CHECK (noise_count >= 0),
                requested_by text NOT NULL CHECK (btrim(requested_by) <> ''),
                requested_at_utc timestamp with time zone NOT NULL,
                started_at_utc timestamp with time zone NULL,
                completed_at_utc timestamp with time zone NULL,
                updated_at_utc timestamp with time zone NOT NULL,
                error text NULL,
                CHECK (processed_target_count <= target_count),
                CHECK (noise_count <= target_count)
            );

            CREATE TABLE IF NOT EXISTS provisional_face_cluster_members (
                run_id uuid NOT NULL,
                face_occurrence_id uuid NOT NULL,
                derived_cluster_key text NULL,
                role text NOT NULL CHECK (role IN ('core', 'border', 'noise')),
                PRIMARY KEY (run_id, face_occurrence_id),
                CONSTRAINT fk_provisional_face_cluster_members_run
                    FOREIGN KEY (run_id)
                    REFERENCES provisional_face_cluster_runs (id) ON DELETE CASCADE,
                CONSTRAINT fk_provisional_face_cluster_members_face
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                CHECK (
                    (role = 'noise' AND derived_cluster_key IS NULL)
                    OR (role IN ('core', 'border') AND btrim(derived_cluster_key) <> ''))
            );

            CREATE TABLE IF NOT EXISTS provisional_face_cluster_current (
                model_id text NOT NULL,
                model_hash text NOT NULL,
                policy_version text NOT NULL,
                include_unknown boolean NOT NULL,
                run_id uuid NOT NULL UNIQUE,
                updated_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (model_id, model_hash, policy_version, include_unknown),
                CONSTRAINT fk_provisional_face_cluster_current_run
                    FOREIGN KEY (run_id)
                    REFERENCES provisional_face_cluster_runs (id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_provisional_face_cluster_active_scope
                ON provisional_face_cluster_runs (
                    model_id,
                    model_hash,
                    policy_version,
                    include_unknown)
                WHERE status IN ('pending', 'running');

            CREATE INDEX IF NOT EXISTS ix_provisional_face_cluster_runs_status
                ON provisional_face_cluster_runs (status, requested_at_utc, id);

            CREATE INDEX IF NOT EXISTS ix_provisional_face_cluster_members_group
                ON provisional_face_cluster_members (run_id, derived_cluster_key, face_occurrence_id)
                WHERE derived_cluster_key IS NOT NULL;
            """;
    }
}
