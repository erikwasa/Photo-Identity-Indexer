using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL review projection over current provisional clusters. This repository never writes
/// canonical identity state; its only mutation is durable face-to-face not-same discovery evidence.
/// </summary>
public sealed class PostgresProvisionalFaceClusterReviewRepository : IProvisionalFaceClusterReviewRepository
{
    private const int MaximumPageSize = 200;
    private const int RepresentativeCount = 3;
    private const int MaximumFeedbackFaces = 500;
    private readonly PostgresCatalogueDatabase _database;

    public PostgresProvisionalFaceClusterReviewRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ProvisionalFaceClusterReviewGroupPage> ListCurrentGroupsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (limit is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        string version = Required(policyVersion, nameof(policyVersion));
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH grouped AS (
                SELECT
                    current_scope.run_id,
                    member.derived_cluster_key,
                    COUNT(*)::integer AS member_count,
                    COUNT(*) FILTER (WHERE member.role = 'core')::integer AS core_count,
                    COUNT(*) FILTER (WHERE member.role = 'border')::integer AS border_count,
                    ARRAY_AGG(
                        member.face_occurrence_id
                        ORDER BY CASE WHEN member.role = 'core' THEN 0 ELSE 1 END,
                                 member.face_occurrence_id) AS representative_candidates
                FROM provisional_face_cluster_current AS current_scope
                INNER JOIN provisional_face_cluster_members AS member
                    ON member.run_id = current_scope.run_id
                WHERE current_scope.model_id = @model_id
                  AND current_scope.model_hash = @model_hash
                  AND current_scope.policy_version = @policy_version
                  AND current_scope.include_unknown = @include_unknown
                  AND member.derived_cluster_key IS NOT NULL
                GROUP BY current_scope.run_id, member.derived_cluster_key
            )
            SELECT
                grouped.run_id,
                grouped.derived_cluster_key,
                grouped.member_count,
                grouped.core_count,
                grouped.border_count,
                grouped.representative_candidates[1:@representative_count],
                COUNT(*) OVER()::integer AS total
            FROM grouped
            ORDER BY
                grouped.member_count DESC,
                (grouped.core_count::double precision / grouped.member_count) DESC,
                grouped.derived_cluster_key
            OFFSET @offset
            LIMIT @limit;
            """;
        AddScopeParameters(command, modelId, modelHash, version, includeUnknown);
        command.Parameters.AddWithValue("representative_count", RepresentativeCount);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", limit);

        List<ProvisionalFaceClusterReviewGroup> items = [];
        int total = 0;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                Guid[] representatives = reader.GetFieldValue<Guid[]>(5);
                total = reader.GetInt32(6);
                items.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    representatives.Select(FaceOccurrenceId.From).ToArray()));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new(items, offset, limit, total);
    }

    public async Task<IReadOnlyList<ProvisionalFaceClusterReviewMember>> ListCurrentGroupMembersAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (limit is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        string version = Required(policyVersion, nameof(policyVersion));
        string clusterKey = Required(derivedClusterKey, nameof(derivedClusterKey));
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
                member.face_occurrence_id,
                member.role
            FROM provisional_face_cluster_current AS current_scope
            INNER JOIN provisional_face_cluster_members AS member
                ON member.run_id = current_scope.run_id
            WHERE current_scope.model_id = @model_id
              AND current_scope.model_hash = @model_hash
              AND current_scope.policy_version = @policy_version
              AND current_scope.include_unknown = @include_unknown
              AND member.derived_cluster_key = @cluster_key
            ORDER BY CASE WHEN member.role = 'core' THEN 0 ELSE 1 END,
                     member.face_occurrence_id
            OFFSET @offset
            LIMIT @limit;
            """;
        AddScopeParameters(command, modelId, modelHash, version, includeUnknown);
        command.Parameters.AddWithValue("cluster_key", clusterKey);
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", limit);

        List<ProvisionalFaceClusterReviewMember> result = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    FaceOccurrenceId.From(reader.GetGuid(2)),
                    ParseRole(reader.GetString(3))));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<ProvisionalFaceNotSameConstraint>> ListNotSameConstraintsAsync(
        IReadOnlyCollection<FaceOccurrenceId> eligibleFaceOccurrenceIds,
        int maximumConstraints = ProvisionalFaceClusterPolicies.MaximumNotSameConstraintsPerRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eligibleFaceOccurrenceIds);
        if (maximumConstraints is < 1 or > ProvisionalFaceClusterPolicies.MaximumNotSameConstraintsPerRun)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConstraints));
        }
        if (eligibleFaceOccurrenceIds.Count < 2)
        {
            return [];
        }

        Guid[] faceIds = eligibleFaceOccurrenceIds.Select(face => face.Value).Distinct().ToArray();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT left_face_occurrence_id, right_face_occurrence_id
            FROM provisional_face_not_same_constraints
            WHERE left_face_occurrence_id = ANY(@face_ids)
              AND right_face_occurrence_id = ANY(@face_ids)
            ORDER BY left_face_occurrence_id, right_face_occurrence_id
            LIMIT @maximum_plus_one;
            """;
        command.Parameters.AddWithValue("face_ids", faceIds);
        command.Parameters.AddWithValue("maximum_plus_one", maximumConstraints + 1);

        List<ProvisionalFaceNotSameConstraint> result = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (result.Count == maximumConstraints)
                {
                    throw new InvalidOperationException(
                        $"Provisional clustering exceeds the bounded not-same evidence limit of {maximumConstraints:N0} constraints.");
                }

                result.Add(new(
                    FaceOccurrenceId.From(reader.GetGuid(0)),
                    FaceOccurrenceId.From(reader.GetGuid(1))));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<int> RecordNotSameAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        FaceOccurrenceId anchorFaceOccurrenceId,
        IReadOnlyCollection<FaceOccurrenceId> otherFaceOccurrenceIds,
        string actor,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(otherFaceOccurrenceIds);
        string version = Required(policyVersion, nameof(policyVersion));
        string clusterKey = Required(derivedClusterKey, nameof(derivedClusterKey));
        string recordedBy = Required(actor, nameof(actor));
        FaceOccurrenceId[] others = otherFaceOccurrenceIds
            .Where(face => face != anchorFaceOccurrenceId)
            .Distinct()
            .ToArray();
        if (others.Length == 0)
        {
            throw new ArgumentException("Choose at least one different cluster member.", nameof(otherFaceOccurrenceIds));
        }
        if (others.Length > MaximumFeedbackFaces)
        {
            throw new ArgumentOutOfRangeException(
                nameof(otherFaceOccurrenceIds),
                $"One not-same feedback operation is limited to {MaximumFeedbackFaces} faces.");
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, transaction, cancellationToken);

        Guid? runId = await ReadCurrentRunIdAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            version,
            includeUnknown,
            cancellationToken);
        if (runId is null)
        {
            throw new InvalidOperationException("No current provisional clustering run exists for this exact model scope.");
        }

        HashSet<Guid> currentMembers = await ReadGroupMemberIdsAsync(
            connection,
            transaction,
            runId.Value,
            clusterKey,
            cancellationToken);
        if (!currentMembers.Contains(anchorFaceOccurrenceId.Value) ||
            others.Any(face => !currentMembers.Contains(face.Value)))
        {
            throw new InvalidOperationException(
                "The selected faces no longer belong to the same current provisional cluster. Refresh and review the current group before recording feedback.");
        }

        int inserted = 0;
        DateTimeOffset now = recordedAtUtc.ToUniversalTime();
        foreach (FaceOccurrenceId other in others)
        {
            ProvisionalFaceNotSameConstraint constraint = ProvisionalFaceNotSameConstraint.Create(
                anchorFaceOccurrenceId,
                other);
            await using NpgsqlCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO provisional_face_not_same_constraints (
                    left_face_occurrence_id,
                    right_face_occurrence_id,
                    source_run_id,
                    source_cluster_key,
                    created_by,
                    created_at_utc)
                VALUES (
                    @left_face_occurrence_id,
                    @right_face_occurrence_id,
                    @source_run_id,
                    @source_cluster_key,
                    @created_by,
                    @created_at_utc)
                ON CONFLICT (left_face_occurrence_id, right_face_occurrence_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("left_face_occurrence_id", constraint.LeftFaceOccurrenceId.Value);
            insert.Parameters.AddWithValue("right_face_occurrence_id", constraint.RightFaceOccurrenceId.Value);
            insert.Parameters.AddWithValue("source_run_id", runId.Value);
            insert.Parameters.AddWithValue("source_cluster_key", clusterKey);
            insert.Parameters.AddWithValue("created_by", recordedBy);
            insert.Parameters.AddWithValue("created_at_utc", now);
            inserted += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    private static async Task<Guid?> ReadCurrentRunIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT run_id
            FROM provisional_face_cluster_current
            WHERE model_id = @model_id
              AND model_hash = @model_hash
              AND policy_version = @policy_version
              AND include_unknown = @include_unknown;
            """;
        AddScopeParameters(command, modelId, modelHash, policyVersion, includeUnknown);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid runId ? runId : null;
    }

    private static async Task<HashSet<Guid>> ReadGroupMemberIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        string clusterKey,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT face_occurrence_id
            FROM provisional_face_cluster_members
            WHERE run_id = @run_id
              AND derived_cluster_key = @cluster_key;
            """;
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("cluster_key", clusterKey);

        HashSet<Guid> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetGuid(0));
        }
        return result;
    }

    private static ProvisionalFaceClusterMemberRole ParseRole(string role) => role switch
    {
        "core" => ProvisionalFaceClusterMemberRole.Core,
        "border" => ProvisionalFaceClusterMemberRole.Border,
        "noise" => ProvisionalFaceClusterMemberRole.Noise,
        _ => throw new InvalidOperationException($"Unknown provisional cluster member role '{role}'."),
    };

    private static void AddScopeParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("policy_version", policyVersion);
        command.Parameters.AddWithValue("include_unknown", includeUnknown);
    }

    private static async Task EnsureSchemaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ProvisionalFaceClusterReviewSchema.Sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    internal static class ProvisionalFaceClusterReviewSchema
    {
        public const string Sql =
            """
            CREATE TABLE IF NOT EXISTS provisional_face_not_same_constraints (
                left_face_occurrence_id uuid NOT NULL,
                right_face_occurrence_id uuid NOT NULL,
                source_run_id uuid NULL,
                source_cluster_key text NULL,
                created_by text NOT NULL CHECK (btrim(created_by) <> ''),
                created_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (left_face_occurrence_id, right_face_occurrence_id),
                CONSTRAINT fk_provisional_face_not_same_left
                    FOREIGN KEY (left_face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                CONSTRAINT fk_provisional_face_not_same_right
                    FOREIGN KEY (right_face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                CONSTRAINT fk_provisional_face_not_same_source_run
                    FOREIGN KEY (source_run_id)
                    REFERENCES provisional_face_cluster_runs (id) ON DELETE SET NULL,
                CHECK (left_face_occurrence_id < right_face_occurrence_id)
            );

            CREATE INDEX IF NOT EXISTS ix_provisional_face_not_same_right
                ON provisional_face_not_same_constraints (right_face_occurrence_id, left_face_occurrence_id);
            """;
    }
}
