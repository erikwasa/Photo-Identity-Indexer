namespace PhotoIdentity.Persistence.Postgres;

public sealed partial class PostgresCatalogueDatabase
{
    private const string DetectorRolloutSchema = """
        CREATE TABLE detector_pipelines (
            pipeline_hash text NOT NULL PRIMARY KEY,
            detector_model_id text NOT NULL,
            detector_model_hash text NOT NULL,
            canonical_definition text NOT NULL,
            recorded_at_utc timestamp with time zone NOT NULL
        );

        CREATE TABLE processing_run_detector_pipelines (
            processing_run_id uuid NOT NULL PRIMARY KEY,
            pipeline_hash text NOT NULL,
            recorded_at_utc timestamp with time zone NOT NULL,
            FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE CASCADE,
            FOREIGN KEY (pipeline_hash) REFERENCES detector_pipelines (pipeline_hash) ON DELETE RESTRICT
        );

        CREATE TABLE detector_reconciliation_plans (
            processing_run_id uuid NOT NULL,
            asset_revision_id uuid NOT NULL,
            pipeline_hash text NOT NULL,
            planned_at_utc timestamp with time zone NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id),
            FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE CASCADE,
            FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
            FOREIGN KEY (pipeline_hash) REFERENCES detector_pipelines (pipeline_hash) ON DELETE RESTRICT
        );

        CREATE TABLE detector_reconciliation_candidates (
            processing_run_id uuid NOT NULL,
            asset_revision_id uuid NOT NULL,
            candidate_index integer NOT NULL CHECK (candidate_index >= 0),
            disposition text NOT NULL CHECK (disposition IN ('existing', 'new', 'ambiguous')),
            proposed_face_occurrence_id uuid NULL,
            bounding_box_json jsonb NOT NULL,
            landmarks_json jsonb NOT NULL,
            applied_face_occurrence_id uuid NULL,
            applied_at_utc timestamp with time zone NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, candidate_index),
            FOREIGN KEY (processing_run_id, asset_revision_id)
                REFERENCES detector_reconciliation_plans (processing_run_id, asset_revision_id) ON DELETE CASCADE,
            FOREIGN KEY (proposed_face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT,
            FOREIGN KEY (applied_face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT,
            CHECK (
                (disposition = 'existing' AND proposed_face_occurrence_id IS NOT NULL)
                OR (disposition IN ('new', 'ambiguous') AND proposed_face_occurrence_id IS NULL)
            ),
            CHECK (
                (applied_face_occurrence_id IS NULL AND applied_at_utc IS NULL)
                OR (applied_face_occurrence_id IS NOT NULL AND applied_at_utc IS NOT NULL)
            )
        );

        CREATE TABLE detector_reconciliation_candidate_options (
            processing_run_id uuid NOT NULL,
            asset_revision_id uuid NOT NULL,
            candidate_index integer NOT NULL,
            face_occurrence_id uuid NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, candidate_index, face_occurrence_id),
            FOREIGN KEY (processing_run_id, asset_revision_id, candidate_index)
                REFERENCES detector_reconciliation_candidates (
                    processing_run_id, asset_revision_id, candidate_index) ON DELETE CASCADE,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT
        );

        CREATE TABLE detector_reconciliation_unmatched_existing (
            processing_run_id uuid NOT NULL,
            asset_revision_id uuid NOT NULL,
            face_occurrence_id uuid NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, face_occurrence_id),
            FOREIGN KEY (processing_run_id, asset_revision_id)
                REFERENCES detector_reconciliation_plans (processing_run_id, asset_revision_id) ON DELETE CASCADE,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT
        );

        CREATE TABLE detector_reconciliation_candidate_inspections (
            processing_run_id uuid NOT NULL,
            asset_revision_id uuid NOT NULL,
            candidate_index integer NOT NULL CHECK (candidate_index >= 0),
            detector_model_id text NOT NULL,
            detector_model_hash text NOT NULL,
            confidence double precision NOT NULL CHECK (confidence >= 0 AND confidence <= 1),
            crop_id uuid NOT NULL,
            crop_protocol text NOT NULL,
            crop_content_sha256 text NOT NULL,
            crop_storage_path text NOT NULL,
            crop_width integer NOT NULL CHECK (crop_width > 0),
            crop_height integer NOT NULL CHECK (crop_height > 0),
            embedder_model_id text NOT NULL,
            embedder_model_hash text NOT NULL,
            embedding_dimensions integer NOT NULL CHECK (embedding_dimensions > 0),
            embedding_l2_norm double precision NOT NULL CHECK (embedding_l2_norm > 0),
            embedding_vector_blob bytea NOT NULL,
            observed_at_utc timestamp with time zone NOT NULL,
            PRIMARY KEY (processing_run_id, asset_revision_id, candidate_index),
            UNIQUE (crop_id),
            FOREIGN KEY (processing_run_id, asset_revision_id, candidate_index)
                REFERENCES detector_reconciliation_candidates (
                    processing_run_id, asset_revision_id, candidate_index) ON DELETE CASCADE
        );

        CREATE TABLE detector_reconciliation_resolution_actions (
            id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            processing_run_id uuid NOT NULL,
            asset_revision_id uuid NOT NULL,
            candidate_index integer NOT NULL CHECK (candidate_index >= 0),
            action_kind text NOT NULL CHECK (action_kind IN ('existing', 'new', 'defer')),
            face_occurrence_id uuid NULL,
            actor text NOT NULL,
            note text NULL,
            created_at_utc timestamp with time zone NOT NULL,
            FOREIGN KEY (processing_run_id, asset_revision_id, candidate_index)
                REFERENCES detector_reconciliation_candidates (
                    processing_run_id, asset_revision_id, candidate_index) ON DELETE CASCADE,
            FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE RESTRICT,
            CHECK (
                (action_kind = 'existing' AND face_occurrence_id IS NOT NULL)
                OR (action_kind IN ('new', 'defer') AND face_occurrence_id IS NULL)
            )
        );

        CREATE INDEX ix_detector_reconciliation_resolution_history
            ON detector_reconciliation_resolution_actions (
                processing_run_id, asset_revision_id, candidate_index, id DESC);
        CREATE INDEX ix_detector_reconciliation_inspection_model
            ON detector_reconciliation_candidate_inspections (
                detector_model_id, detector_model_hash, embedder_model_id, embedder_model_hash);

        CREATE INDEX ix_detector_reconciliation_pending ON detector_reconciliation_candidates (processing_run_id, disposition, applied_face_occurrence_id);
        CREATE INDEX ix_detector_reconciliation_revision ON detector_reconciliation_plans (asset_revision_id, pipeline_hash);
        """;
}
