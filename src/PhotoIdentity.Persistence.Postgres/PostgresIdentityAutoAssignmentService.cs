using System.Buffers.Binary;
using System.Data;
using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Promotes persisted High rank-one suggestions and, when separately enabled, evaluated
/// multi-evidence candidates through the canonical PostgreSQL suggestion acceptance path.
/// All candidate evidence is read before any canonical assignment is created, preserving
/// fixed-snapshot/no-same-run-cascade semantics. Human review that wins the race remains
/// authoritative and is skipped.
/// </summary>
public sealed class PostgresIdentityAutoAssignmentService :
    IIdentityAutoAssignmentService
{
    public const string AutomaticActor = "identity-matcher:auto";
    public const string MultiEvidenceAutomaticActor = "identity-matcher:auto-multi-evidence";

    private readonly PostgresCatalogueDatabase _database;
    private readonly PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository _multiEvidencePolicies;
    private readonly TimeProvider _timeProvider;

    public PostgresIdentityAutoAssignmentService(
        PostgresCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _multiEvidencePolicies = new(database, _timeProvider);
    }

    public async Task<ReviewIdentityAutoAssignmentSummary> ApplyAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        if (!policy.AutoAssignEnabled)
        {
            return new ReviewIdentityAutoAssignmentSummary(0, 0, 0);
        }

        IReadOnlyList<AutoAssignmentCandidate> highCandidates = await ReadHighCandidatesAsync(
            modelId,
            modelHash,
            policy.HighScoreThreshold,
            policy.HighMarginThreshold,
            cancellationToken);

        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration multiConfiguration =
            await _multiEvidencePolicies.GetAsync(modelId, modelHash, cancellationToken);
        IReadOnlyList<AutoAssignmentCandidate> multiCandidates = [];
        if (multiConfiguration.Enabled)
        {
            ReviewIdentityMultiEvidenceAutoAssignmentPolicy multiPolicy =
                ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial;
            if (!string.Equals(
                    multiConfiguration.AlgorithmPolicyVersion,
                    multiPolicy.Version,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Multi-evidence automatic-assignment configuration references unsupported policy '{multiConfiguration.AlgorithmPolicyVersion}'. Save the current policy before regenerating matches.");
            }

            DateTimeOffset requestedAtUtc = await RequireActiveRegenerationRequestedAtAsync(
                modelId,
                modelHash,
                cancellationToken);
            if (multiConfiguration.UpdatedAtUtc > requestedAtUtc)
            {
                throw new InvalidOperationException(
                    $"Multi-evidence automatic-assignment policy changed to version {multiConfiguration.Version} after the active regeneration was requested. Start a new regeneration.");
            }

            PostgresProvisionalFaceClusterRepository clusterRepository = new(_database);
            ProvisionalFaceClusterRun? clusterRun = await clusterRepository.GetLatestAsync(
                modelId,
                modelHash,
                multiPolicy.ClusterPolicyVersion,
                multiPolicy.ClusterIncludeUnknown,
                cancellationToken);
            if (clusterRun is not null
                && string.Equals(
                    clusterRun.Status,
                    ProvisionalFaceClusterRunStatuses.Completed,
                    StringComparison.Ordinal)
                && await clusterRepository.EvidenceStillMatchesAsync(clusterRun, cancellationToken))
            {
                multiCandidates = await ReadMultiEvidenceCandidatesAsync(
                    modelId,
                    modelHash,
                    policy,
                    multiConfiguration,
                    multiPolicy,
                    clusterRun,
                    cancellationToken);
            }
        }

        AutoAssignmentCandidate[] candidates = highCandidates
            .Concat(multiCandidates)
            .GroupBy(candidate => candidate.FaceOccurrenceId)
            .Select(group => group
                .OrderBy(candidate => candidate.Kind == AutoAssignmentKind.High ? 0 : 1)
                .First())
            .OrderBy(candidate => candidate.FaceOccurrenceId.ToString(), StringComparer.Ordinal)
            .ToArray();

        IReviewSuggestionRepository reviewSuggestions =
            new PostgresReviewSuggestionRepository(_database);
        int assignedCount = 0;
        int skippedCount = 0;
        foreach (AutoAssignmentCandidate candidate in candidates)
        {
            DateTimeOffset decidedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            string actor = candidate.Kind == AutoAssignmentKind.High
                ? AutomaticActor
                : MultiEvidenceAutomaticActor;
            string note = BuildNote(candidate, modelId, modelHash, policy, multiConfiguration);

            try
            {
                _ = await reviewSuggestions.AcceptAsync(
                    candidate.FaceOccurrenceId,
                    candidate.SuggestionId,
                    actor,
                    decidedAtUtc,
                    note,
                    cancellationToken);
                assignedCount++;
            }
            catch (InvalidOperationException exception)
                when (IsConcurrentOrSupersedingDecision(exception))
            {
                skippedCount++;
            }
            catch (KeyNotFoundException)
            {
                skippedCount++;
            }
        }

        return new ReviewIdentityAutoAssignmentSummary(
            candidates.Length,
            assignedCount,
            skippedCount);
    }

    private async Task<IReadOnlyList<AutoAssignmentCandidate>> ReadHighCandidatesAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        double scoreThreshold,
        double marginThreshold,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                suggestion.id,
                ranking.face_occurrence_id,
                suggestion.score,
                ranking.score_margin
            FROM identity_suggestion_rankings AS ranking
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
            WHERE ranking.model_id = @model_id
              AND ranking.model_hash = @model_hash
              AND ranking.rank = 1
              AND suggestion.status = 'pending'
              AND suggestion.score >= @score_threshold
              AND ranking.score_margin IS NOT NULL
              AND ranking.score_margin >= @margin_threshold
              AND NOT EXISTS (
                  SELECT 1
                  FROM review_actions AS action
                  WHERE action.face_occurrence_id = ranking.face_occurrence_id
                    AND action.action_kind IN ('assign', 'unknown', 'reject')
                    AND action.reversed_at_utc IS NULL)
            ORDER BY ranking.face_occurrence_id;
            """;
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("score_threshold", scoreThreshold);
        command.Parameters.AddWithValue("margin_threshold", marginThreshold);

        List<AutoAssignmentCandidate> candidates = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new AutoAssignmentCandidate(
                reader.GetInt64(0),
                FaceOccurrenceId.From(reader.GetGuid(1)),
                reader.GetDouble(2),
                reader.GetDouble(3),
                AutoAssignmentKind.High,
                null));
        }

        return candidates;
    }

    private async Task<IReadOnlyList<AutoAssignmentCandidate>> ReadMultiEvidenceCandidatesAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy identityPolicy,
        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration configuration,
        ReviewIdentityMultiEvidenceAutoAssignmentPolicy multiPolicy,
        ProvisionalFaceClusterRun clusterRun,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MultiEvidenceTarget> targets = await ReadMultiEvidenceTargetsAsync(
            modelId,
            modelHash,
            identityPolicy,
            multiPolicy,
            clusterRun.Id,
            cancellationToken);
        if (targets.Count == 0)
        {
            return [];
        }

        Dictionary<string, ClusterSnapshot> clusters = await ReadClusterSnapshotsAsync(
            modelId,
            modelHash,
            clusterRun.Id,
            cancellationToken);
        HashSet<string> conflictedClusters = await ReadConflictedClusterKeysAsync(
            clusterRun.Id,
            cancellationToken);
        Dictionary<PersonId, List<ReferenceExemplar>> references = await ReadReferenceExemplarsAsync(
            modelId,
            modelHash,
            cancellationToken);

        ReviewIdentitySuggestionPolicy advisoryIdentityPolicy = identityPolicy with
        {
            MediumScoreThreshold = Math.Max(
                identityPolicy.MediumScoreThreshold,
                multiPolicy.MinimumTargetScore),
        };
        DateTimeOffset evaluatedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        List<AutoAssignmentCandidate> result = [];
        foreach (MultiEvidenceTarget target in targets)
        {
            if (!clusters.TryGetValue(target.ClusterKey, out ClusterSnapshot? cluster))
            {
                continue;
            }

            string[] otherContentGroups = cluster.Members
                .Where(member => !string.Equals(member.ContentHash, target.ContentHash, StringComparison.Ordinal))
                .Select(member => member.ContentHash)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (otherContentGroups.Length == 0)
            {
                continue;
            }

            ProvisionalFaceClusterKnownPersonMemberEvidence[] advisoryEvidence = cluster.Members
                .Where(member => member.FaceOccurrenceId != target.FaceOccurrenceId)
                .Where(member => !string.Equals(member.ContentHash, target.ContentHash, StringComparison.Ordinal))
                .Where(member => member.SuggestedPersonId is not null)
                .Select(member => new ProvisionalFaceClusterKnownPersonMemberEvidence(
                    member.FaceOccurrenceId,
                    member.ContentHash,
                    member.SuggestedPersonId!.Value,
                    member.SuggestedPersonDisplayName!,
                    member.SuggestionScore!.Value,
                    member.ScoreMargin))
                .ToArray();

            ProvisionalFaceClusterKnownPersonAdvisory advisory =
                ProvisionalFaceClusterKnownPersonAdvisoryPolicy.Initial.Evaluate(
                    clusterRun.Id,
                    modelId,
                    modelHash,
                    clusterRun.Policy.Version,
                    clusterRun.IncludeUnknown,
                    target.ClusterKey,
                    cluster.Members.Count,
                    otherContentGroups.Length,
                    cluster.CoreCount,
                    conflictedClusters.Contains(target.ClusterKey) ? 1 : 0,
                    advisoryIdentityPolicy,
                    advisoryEvidence,
                    evaluatedAtUtc);
            bool strongClusterAgreement = string.Equals(
                    advisory.Status,
                    ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Strong,
                    StringComparison.Ordinal)
                && advisory.Candidate?.PersonId == target.SuggestedPersonId;
            if (!strongClusterAgreement)
            {
                continue;
            }

            int referenceSupportCount = CountIndependentReferenceSupport(
                target,
                references,
                multiPolicy.MinimumReferenceScore);
            if (!multiPolicy.Qualifies(
                    target.Score,
                    target.ScoreMargin,
                    referenceSupportCount,
                    strongClusterAgreement,
                    identityPolicy.MediumScoreThreshold))
            {
                continue;
            }

            result.Add(new AutoAssignmentCandidate(
                target.SuggestionId,
                target.FaceOccurrenceId,
                target.Score,
                target.ScoreMargin,
                AutoAssignmentKind.MultiEvidence,
                new MultiEvidenceProvenance(
                    configuration.Version,
                    multiPolicy.Version,
                    clusterRun.Id,
                    clusterRun.Policy.Version,
                    target.ClusterKey,
                    advisory.Candidate?.SupportCount ?? 0,
                    advisory.Candidate?.SupportShare ?? 0,
                    advisory.CoreShare,
                    advisory.CompetingCandidate?.SupportCount ?? 0,
                    advisory.CompetingCandidate?.SupportShare ?? 0,
                    referenceSupportCount,
                    multiPolicy.MinimumReferenceScore,
                    multiPolicy.MinimumTargetMargin)));
        }

        return result;
    }

    private async Task<IReadOnlyList<MultiEvidenceTarget>> ReadMultiEvidenceTargetsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy identityPolicy,
        ReviewIdentityMultiEvidenceAutoAssignmentPolicy multiPolicy,
        Guid clusterRunId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH matching_embeddings AS (
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
            )
            SELECT
                suggestion.id,
                ranking.face_occurrence_id,
                suggestion.suggested_person_id,
                suggestion.score,
                ranking.score_margin,
                member.derived_cluster_key,
                asset_revision.content_sha256,
                matching.dimensions,
                matching.l2_norm,
                matching.vector_blob
            FROM identity_suggestion_rankings AS ranking
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
            INNER JOIN provisional_face_cluster_members AS member
                ON member.run_id = @cluster_run_id
               AND member.face_occurrence_id = ranking.face_occurrence_id
               AND member.derived_cluster_key IS NOT NULL
            INNER JOIN face_occurrences AS face
                ON face.id = ranking.face_occurrence_id
            INNER JOIN asset_revisions AS asset_revision
                ON asset_revision.id = face.asset_revision_id
            INNER JOIN matching_embeddings AS matching
                ON matching.face_occurrence_id = ranking.face_occurrence_id
               AND matching.row_number = 1
            WHERE ranking.model_id = @model_id
              AND ranking.model_hash = @model_hash
              AND ranking.rank = 1
              AND suggestion.status = 'pending'
              AND suggestion.score >= @minimum_target_score
              AND ranking.score_margin IS NOT NULL
              AND ranking.score_margin >= @minimum_target_margin
              AND NOT EXISTS (
                  SELECT 1
                  FROM review_actions AS action
                  WHERE action.face_occurrence_id = ranking.face_occurrence_id
                    AND action.action_kind IN ('assign', 'unknown', 'reject')
                    AND action.reversed_at_utc IS NULL)
            ORDER BY ranking.face_occurrence_id;
            """;
        command.Parameters.AddWithValue("cluster_run_id", clusterRunId);
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue(
            "minimum_target_score",
            Math.Max(multiPolicy.MinimumTargetScore, identityPolicy.MediumScoreThreshold));
        command.Parameters.AddWithValue("minimum_target_margin", multiPolicy.MinimumTargetMargin);

        List<MultiEvidenceTarget> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            double score = reader.GetDouble(3);
            double margin = reader.GetDouble(4);
            if (score >= identityPolicy.HighScoreThreshold
                && margin >= identityPolicy.HighMarginThreshold)
            {
                continue;
            }

            result.Add(new MultiEvidenceTarget(
                reader.GetInt64(0),
                FaceOccurrenceId.From(reader.GetGuid(1)),
                PersonId.From(reader.GetGuid(2)),
                score,
                margin,
                reader.GetString(5),
                reader.GetString(6),
                ReadVector(reader, 7, 8, 9)));
        }

        return result;
    }

    private async Task<Dictionary<string, ClusterSnapshot>> ReadClusterSnapshotsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        Guid clusterRunId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                member.derived_cluster_key,
                member.face_occurrence_id,
                member.role,
                asset_revision.content_sha256,
                suggestion.suggested_person_id,
                COALESCE(person.display_name, 'Unnamed person'),
                suggestion.score,
                ranking.score_margin
            FROM provisional_face_cluster_members AS member
            INNER JOIN face_occurrences AS face
                ON face.id = member.face_occurrence_id
            INNER JOIN asset_revisions AS asset_revision
                ON asset_revision.id = face.asset_revision_id
            LEFT JOIN identity_suggestion_rankings AS ranking
                ON ranking.face_occurrence_id = member.face_occurrence_id
               AND ranking.model_id = @model_id
               AND ranking.model_hash = @model_hash
               AND ranking.rank = 1
            LEFT JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
               AND suggestion.status = 'pending'
               AND NOT EXISTS (
                   SELECT 1
                   FROM review_actions AS action
                   WHERE action.face_occurrence_id = member.face_occurrence_id
                     AND action.action_kind IN ('assign', 'unknown', 'reject')
                     AND action.reversed_at_utc IS NULL)
               AND NOT EXISTS (
                   SELECT 1
                   FROM identity_suggestions AS rejected
                   WHERE rejected.face_occurrence_id = member.face_occurrence_id
                     AND rejected.suggested_person_id = suggestion.suggested_person_id
                     AND rejected.model_id = @model_id
                     AND rejected.model_hash = @model_hash
                     AND rejected.status = 'rejected')
            LEFT JOIN people AS person
                ON person.id = suggestion.suggested_person_id
               AND person.merged_into_person_id IS NULL
            WHERE member.run_id = @cluster_run_id
              AND member.derived_cluster_key IS NOT NULL
            ORDER BY member.derived_cluster_key, member.face_occurrence_id;
            """;
        command.Parameters.AddWithValue("cluster_run_id", clusterRunId);
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());

        Dictionary<string, List<ClusterMemberSnapshot>> members = new(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string key = reader.GetString(0);
            if (!members.TryGetValue(key, out List<ClusterMemberSnapshot>? group))
            {
                group = [];
                members.Add(key, group);
            }

            bool hasSuggestion = !reader.IsDBNull(4) && !reader.IsDBNull(6);
            group.Add(new ClusterMemberSnapshot(
                FaceOccurrenceId.From(reader.GetGuid(1)),
                string.Equals(reader.GetString(2), "core", StringComparison.Ordinal),
                reader.GetString(3),
                hasSuggestion ? PersonId.From(reader.GetGuid(4)) : null,
                hasSuggestion ? reader.GetString(5) : null,
                hasSuggestion ? reader.GetDouble(6) : null,
                reader.IsDBNull(7) ? null : reader.GetDouble(7)));
        }

        return members.ToDictionary(
            pair => pair.Key,
            pair => new ClusterSnapshot(
                pair.Value,
                pair.Value.Count(member => member.IsCore)),
            StringComparer.Ordinal);
    }

    private async Task<HashSet<string>> ReadConflictedClusterKeysAsync(
        Guid clusterRunId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT left_member.derived_cluster_key
            FROM provisional_face_not_same_constraints AS constraint_row
            INNER JOIN provisional_face_cluster_members AS left_member
                ON left_member.run_id = @run_id
               AND left_member.face_occurrence_id = constraint_row.left_face_occurrence_id
            INNER JOIN provisional_face_cluster_members AS right_member
                ON right_member.run_id = @run_id
               AND right_member.face_occurrence_id = constraint_row.right_face_occurrence_id
               AND right_member.derived_cluster_key = left_member.derived_cluster_key
            WHERE left_member.derived_cluster_key IS NOT NULL;
            """;
        command.Parameters.AddWithValue("run_id", clusterRunId);

        HashSet<string> result = new(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private async Task<Dictionary<PersonId, List<ReferenceExemplar>>> ReadReferenceExemplarsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            WITH latest_review AS (
                SELECT
                    face_occurrence_id,
                    action_kind,
                    person_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'unknown', 'reject')
                  AND reversed_at_utc IS NULL
            ),
            confirmed_faces AS (
                SELECT face_occurrence_id, person_id
                FROM latest_review
                WHERE row_number = 1
                  AND action_kind = 'assign'
                  AND person_id IS NOT NULL
                UNION
                SELECT label.face_occurrence_id, label.person_id
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
            )
            SELECT
                confirmed.person_id,
                asset_revision.content_sha256,
                matching.dimensions,
                matching.l2_norm,
                matching.vector_blob
            FROM confirmed_faces AS confirmed
            INNER JOIN matching_embeddings AS matching
                ON matching.face_occurrence_id = confirmed.face_occurrence_id
               AND matching.row_number = 1
            INNER JOIN face_occurrences AS face
                ON face.id = confirmed.face_occurrence_id
            INNER JOIN asset_revisions AS asset_revision
                ON asset_revision.id = face.asset_revision_id
            INNER JOIN people AS person
                ON person.id = confirmed.person_id
            WHERE person.merged_into_person_id IS NULL
            ORDER BY confirmed.person_id, asset_revision.content_sha256, confirmed.face_occurrence_id;
            """;
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());

        Dictionary<PersonId, List<ReferenceExemplar>> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            PersonId personId = PersonId.From(reader.GetGuid(0));
            if (!result.TryGetValue(personId, out List<ReferenceExemplar>? group))
            {
                group = [];
                result.Add(personId, group);
            }
            group.Add(new ReferenceExemplar(
                reader.GetString(1),
                ReadVector(reader, 2, 3, 4)));
        }
        return result;
    }

    private static int CountIndependentReferenceSupport(
        MultiEvidenceTarget target,
        IReadOnlyDictionary<PersonId, List<ReferenceExemplar>> references,
        double minimumScore)
    {
        if (!references.TryGetValue(target.SuggestedPersonId, out List<ReferenceExemplar>? personReferences))
        {
            return 0;
        }

        HashSet<string> supportingGroups = new(StringComparer.Ordinal);
        foreach (ReferenceExemplar reference in personReferences)
        {
            if (string.Equals(reference.ContentHash, target.ContentHash, StringComparison.Ordinal))
            {
                continue;
            }

            double score;
            try
            {
                score = target.Vector.CosineSimilarity(reference.Vector);
            }
            catch (ArgumentException exception)
            {
                throw new DataException(
                    "Embeddings from one model revision have inconsistent dimensions.",
                    exception);
            }
            if (!double.IsFinite(score))
            {
                throw new DataException("Cosine similarity produced a non-finite score.");
            }
            if (score >= minimumScore)
            {
                supportingGroups.Add(reference.ContentHash);
            }
        }

        return supportingGroups.Count;
    }

    private async Task<DateTimeOffset> RequireActiveRegenerationRequestedAtAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT requested_at_utc
            FROM identity_match_regeneration_runs
            WHERE model_id = @model_id
              AND model_hash = @model_hash
              AND status IN ('pending', 'running')
            ORDER BY requested_at_utc DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not DateTimeOffset requestedAtUtc)
        {
            throw new InvalidOperationException(
                "Multi-evidence automatic assignment requires an active exact-model regeneration run.");
        }
        return requestedAtUtc.ToUniversalTime();
    }

    private static string BuildNote(
        AutoAssignmentCandidate candidate,
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy identityPolicy,
        ReviewIdentityMultiEvidenceAutoAssignmentConfiguration multiConfiguration)
    {
        if (candidate.Kind == AutoAssignmentKind.High)
        {
            return FormattableString.Invariant(
                $"Automatic assignment from persisted High rank-1 identity suggestion; model-id={modelId}; model-hash={modelHash}; score={candidate.Score:R}; rank1-rank2-margin={candidate.ScoreMargin:R}; policy-version={identityPolicy.Version}; high-score-threshold={identityPolicy.HighScoreThreshold:R}; high-margin-threshold={identityPolicy.HighMarginThreshold:R}; medium-score-threshold={identityPolicy.MediumScoreThreshold:R}.");
        }

        MultiEvidenceProvenance evidence = candidate.MultiEvidence
            ?? throw new InvalidOperationException("Multi-evidence provenance is required.");
        return FormattableString.Invariant(
            $"Automatic assignment from validated multi-evidence rank-1 identity suggestion; model-id={modelId}; model-hash={modelHash}; score={candidate.Score:R}; rank1-rank2-margin={candidate.ScoreMargin:R}; suggestion-policy-version={identityPolicy.Version}; multi-evidence-config-version={evidence.ConfigurationVersion}; multi-evidence-policy={evidence.AlgorithmPolicyVersion}; cluster-run-id={evidence.ClusterRunId}; cluster-policy={evidence.ClusterPolicyVersion}; cluster-key={evidence.ClusterKey}; cluster-support-count={evidence.ClusterSupportCount}; cluster-support-share={evidence.ClusterSupportShare:R}; cluster-core-share={evidence.ClusterCoreShare:R}; competing-support-count={evidence.CompetingSupportCount}; competing-support-share={evidence.CompetingSupportShare:R}; independent-reference-support-count={evidence.IndependentReferenceSupportCount}; reference-support-threshold={evidence.ReferenceSupportThreshold:R}; minimum-target-margin={evidence.MinimumTargetMargin:R}; configured-by={multiConfiguration.UpdatedBy}.");
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
            throw new DataException("The stored embedding dimensions do not match its vector data.");
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
            throw new DataException("The stored embedding norm does not match its vector data.");
        }
        return vector;
    }

    private static bool IsConcurrentOrSupersedingDecision(
        InvalidOperationException exception) =>
        exception.Message.Contains("already been reviewed", StringComparison.Ordinal)
        || exception.Message.Contains("already been accepted", StringComparison.Ordinal)
        || exception.Message.Contains("already been rejected", StringComparison.Ordinal)
        || exception.Message.Contains(
            "changed before the review decision",
            StringComparison.Ordinal);

    private enum AutoAssignmentKind
    {
        High,
        MultiEvidence,
    }

    private sealed record AutoAssignmentCandidate(
        long SuggestionId,
        FaceOccurrenceId FaceOccurrenceId,
        double Score,
        double ScoreMargin,
        AutoAssignmentKind Kind,
        MultiEvidenceProvenance? MultiEvidence);

    private sealed record MultiEvidenceTarget(
        long SuggestionId,
        FaceOccurrenceId FaceOccurrenceId,
        PersonId SuggestedPersonId,
        double Score,
        double ScoreMargin,
        string ClusterKey,
        string ContentHash,
        EmbeddingVector Vector);

    private sealed record ClusterMemberSnapshot(
        FaceOccurrenceId FaceOccurrenceId,
        bool IsCore,
        string ContentHash,
        PersonId? SuggestedPersonId,
        string? SuggestedPersonDisplayName,
        double? SuggestionScore,
        double? ScoreMargin);

    private sealed record ClusterSnapshot(
        IReadOnlyList<ClusterMemberSnapshot> Members,
        int CoreCount);

    private sealed record ReferenceExemplar(
        string ContentHash,
        EmbeddingVector Vector);

    private sealed record MultiEvidenceProvenance(
        int ConfigurationVersion,
        string AlgorithmPolicyVersion,
        Guid ClusterRunId,
        string ClusterPolicyVersion,
        string ClusterKey,
        int ClusterSupportCount,
        double ClusterSupportShare,
        double ClusterCoreShare,
        int CompetingSupportCount,
        double CompetingSupportShare,
        int IndependentReferenceSupportCount,
        double ReferenceSupportThreshold,
        double MinimumTargetMargin);
}
