using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Read-only PostgreSQL projection that combines the current provisional cluster with the current
/// exact-model per-face rank-1 suggestion evidence. It never creates canonical assignments and does
/// not mutate the ordinary suggestion rankings.
/// </summary>
public sealed class PostgresProvisionalFaceClusterKnownPersonAdvisoryRepository :
    IProvisionalFaceClusterKnownPersonAdvisoryRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly IIdentitySuggestionPolicyRepository _identitySuggestionPolicies;
    private readonly TimeProvider _timeProvider;

    public PostgresProvisionalFaceClusterKnownPersonAdvisoryRepository(
        PostgresCatalogueDatabase database,
        IIdentitySuggestionPolicyRepository identitySuggestionPolicies,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(identitySuggestionPolicies);
        _database = database;
        _identitySuggestionPolicies = identitySuggestionPolicies;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProvisionalFaceClusterKnownPersonAdvisory?> GetCurrentAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string clusterPolicyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterPolicyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(derivedClusterKey);

        ReviewIdentitySuggestionPolicy identityPolicy = await _identitySuggestionPolicies.GetAsync(
            modelId,
            modelHash,
            cancellationToken);

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        (Guid RunId, FaceOccurrenceId[] FaceIds, int CoreCount, int IndependentMemberCount)? scope = await ReadScopeAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            clusterPolicyVersion,
            includeUnknown,
            derivedClusterKey,
            cancellationToken);
        if (scope is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        int internalConflictCount = await CountInternalConflictsAsync(
            connection,
            transaction,
            scope.Value.FaceIds,
            cancellationToken);
        IReadOnlyList<ProvisionalFaceClusterKnownPersonMemberEvidence> evidence = await ReadEvidenceAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            scope.Value.FaceIds,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return ProvisionalFaceClusterKnownPersonAdvisoryPolicy.Initial.Evaluate(
            scope.Value.RunId,
            modelId,
            modelHash,
            clusterPolicyVersion,
            includeUnknown,
            derivedClusterKey,
            scope.Value.FaceIds.Length,
            scope.Value.IndependentMemberCount,
            scope.Value.CoreCount,
            internalConflictCount,
            identityPolicy,
            evidence,
            _timeProvider.GetUtcNow());
    }

    private static async Task<(Guid RunId, FaceOccurrenceId[] FaceIds, int CoreCount, int IndependentMemberCount)?> ReadScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        string clusterPolicyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                current_scope.run_id,
                member.face_occurrence_id,
                member.role,
                asset_revision.content_sha256
            FROM provisional_face_cluster_current AS current_scope
            INNER JOIN provisional_face_cluster_members AS member
                ON member.run_id = current_scope.run_id
            INNER JOIN face_occurrences AS face
                ON face.id = member.face_occurrence_id
            INNER JOIN asset_revisions AS asset_revision
                ON asset_revision.id = face.asset_revision_id
            WHERE current_scope.model_id = @model_id
              AND current_scope.model_hash = @model_hash
              AND current_scope.policy_version = @policy_version
              AND current_scope.include_unknown = @include_unknown
              AND member.derived_cluster_key = @cluster_key
            ORDER BY CASE WHEN member.role = 'core' THEN 0 ELSE 1 END,
                     member.face_occurrence_id;
            """;
        AddScopeParameters(
            command,
            modelId,
            modelHash,
            clusterPolicyVersion,
            includeUnknown,
            derivedClusterKey);

        Guid? runId = null;
        int coreCount = 0;
        List<FaceOccurrenceId> faceIds = [];
        HashSet<string> independentContentGroups = new(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runId ??= reader.GetGuid(0);
            faceIds.Add(FaceOccurrenceId.From(reader.GetGuid(1)));
            if (string.Equals(reader.GetString(2), "core", StringComparison.Ordinal))
            {
                coreCount++;
            }
            independentContentGroups.Add(reader.GetString(3));
        }

        return runId is null || faceIds.Count == 0
            ? null
            : (runId.Value, faceIds.ToArray(), coreCount, independentContentGroups.Count);
    }

    private static async Task<int> CountInternalConflictsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<FaceOccurrenceId> faceIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = faceIds.Select(face => face.Value).ToArray();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(*)::integer
            FROM provisional_face_not_same_constraints
            WHERE left_face_occurrence_id = ANY(@face_ids)
              AND right_face_occurrence_id = ANY(@face_ids);
            """;
        command.Parameters.AddWithValue("face_ids", ids);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return checked(Convert.ToInt32(value));
    }

    private static async Task<IReadOnlyList<ProvisionalFaceClusterKnownPersonMemberEvidence>> ReadEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        IReadOnlyCollection<FaceOccurrenceId> faceIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = faceIds.Select(face => face.Value).ToArray();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                ranking.face_occurrence_id,
                asset_revision.content_sha256,
                suggestion.suggested_person_id,
                COALESCE(person.display_name, 'Unnamed person') AS display_name,
                suggestion.score,
                ranking.score_margin
            FROM identity_suggestion_rankings AS ranking
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
            INNER JOIN people AS person
                ON person.id = suggestion.suggested_person_id
               AND person.merged_into_person_id IS NULL
            INNER JOIN face_occurrences AS face
                ON face.id = ranking.face_occurrence_id
            INNER JOIN asset_revisions AS asset_revision
                ON asset_revision.id = face.asset_revision_id
            WHERE ranking.face_occurrence_id = ANY(@face_ids)
              AND ranking.model_id = @model_id
              AND ranking.model_hash = @model_hash
              AND ranking.rank = 1
              AND suggestion.status = 'pending'
              AND NOT EXISTS (
                  SELECT 1
                  FROM review_actions AS action
                  WHERE action.face_occurrence_id = ranking.face_occurrence_id
                    AND action.action_kind IN ('assign', 'unknown', 'reject')
                    AND action.reversed_at_utc IS NULL)
              AND NOT EXISTS (
                  SELECT 1
                  FROM identity_suggestions AS rejected
                  WHERE rejected.face_occurrence_id = ranking.face_occurrence_id
                    AND rejected.suggested_person_id = suggestion.suggested_person_id
                    AND rejected.model_id = @model_id
                    AND rejected.model_hash = @model_hash
                    AND rejected.status = 'rejected')
            ORDER BY ranking.face_occurrence_id;
            """;
        command.Parameters.AddWithValue("face_ids", ids);
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());

        List<ProvisionalFaceClusterKnownPersonMemberEvidence> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                FaceOccurrenceId.From(reader.GetGuid(0)),
                reader.GetString(1),
                PersonId.From(reader.GetGuid(2)),
                reader.GetString(3),
                reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5)));
        }
        return result;
    }

    private static void AddScopeParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash,
        string clusterPolicyVersion,
        bool includeUnknown,
        string derivedClusterKey)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("policy_version", clusterPolicyVersion.Trim());
        command.Parameters.AddWithValue("include_unknown", includeUnknown);
        command.Parameters.AddWithValue("cluster_key", derivedClusterKey.Trim());
    }
}
