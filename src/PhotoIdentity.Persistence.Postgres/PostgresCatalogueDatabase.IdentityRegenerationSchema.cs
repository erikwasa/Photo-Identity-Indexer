namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Promotes runtime-owned derived-work control tables into fresh PostgreSQL catalogue
/// initialization so schema discovery sees the complete authoritative catalogue before any
/// repository is invoked.
///
/// Repositories retain CREATE IF NOT EXISTS guards for compatibility with PostgreSQL catalogues
/// initialized by earlier development builds. Extending the current migration definition here
/// also keeps fresh databases complete without requiring a repository call first.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string IdentityMatchRegenerationSchema = """
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

    static PostgresCatalogueDatabase()
    {
        int currentMigrationIndex = Array.FindIndex(
            Migrations,
            migration => migration.Version == CurrentSchemaVersion);
        if (currentMigrationIndex < 0)
        {
            throw new InvalidOperationException(
                $"PostgreSQL migration {CurrentSchemaVersion} is missing from the migration registry.");
        }

        Migration current = Migrations[currentMigrationIndex];
        string sql = current.Sql;
        if (!sql.Contains(
                "identity_match_regeneration_runs",
                StringComparison.OrdinalIgnoreCase))
        {
            sql += Environment.NewLine + IdentityMatchRegenerationSchema;
        }

        if (!sql.Contains(
                "provisional_face_cluster_runs",
                StringComparison.OrdinalIgnoreCase))
        {
            sql += Environment.NewLine +
                PostgresProvisionalFaceClusterRepository.ProvisionalFaceClusterSchema.Sql;
        }

        if (!sql.Contains(
                "provisional_face_not_same_constraints",
                StringComparison.OrdinalIgnoreCase))
        {
            sql += Environment.NewLine +
                PostgresProvisionalFaceClusterReviewRepository.ProvisionalFaceClusterReviewSchema.Sql;
        }

        if (!sql.Contains(
                "identity_multi_evidence_auto_assignment_policies",
                StringComparison.OrdinalIgnoreCase))
        {
            sql += Environment.NewLine +
                PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository.MultiEvidencePolicySchema.Sql;
        }

        if (!sql.Contains(
                "person_birth_metadata",
                StringComparison.OrdinalIgnoreCase))
        {
            sql += Environment.NewLine + PostgresPersonFamilyMetadataSchema.Sql;
        }

        if (!string.Equals(sql, current.Sql, StringComparison.Ordinal))
        {
            Migrations[currentMigrationIndex] = current with { Sql = sql };
        }
    }
}
