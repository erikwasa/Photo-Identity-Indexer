using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Owns the PostgreSQL connection pool and versioned migration bootstrap while
/// PostgreSQL is introduced alongside the still-authoritative SQLite catalogue.
/// </summary>
public sealed class PostgresCatalogueDatabase : IAsyncDisposable
{
    public const int CurrentSchemaVersion = 14;

    private const long MigrationAdvisoryLockKey = 504091701;

    private static readonly Migration[] Migrations =
    [
        new(
            1,
            "postgres-runtime-foundation",
            """
            SELECT 1;
            """),
        new(
            2,
            "foundational-catalogue-and-processing-schema",
            """
            CREATE TABLE sources (
                id uuid NOT NULL PRIMARY KEY,
                kind text NOT NULL CHECK (btrim(kind) <> ''),
                root_locator text NOT NULL CHECK (btrim(root_locator) <> ''),
                created_at_utc timestamp with time zone NOT NULL,
                UNIQUE (kind, root_locator)
            );

            CREATE TABLE assets (
                id uuid NOT NULL PRIMARY KEY,
                source_id uuid NOT NULL,
                source_key text NOT NULL CHECK (btrim(source_key) <> ''),
                created_at_utc timestamp with time zone NOT NULL,
                last_seen_at_utc timestamp with time zone NULL,
                deleted_at_utc timestamp with time zone NULL,
                CONSTRAINT fk_assets_source
                    FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE RESTRICT,
                UNIQUE (source_id, source_key),
                CHECK (last_seen_at_utc IS NULL OR last_seen_at_utc >= created_at_utc),
                CHECK (deleted_at_utc IS NULL OR deleted_at_utc >= created_at_utc)
            );

            CREATE TABLE asset_revisions (
                id uuid NOT NULL PRIMARY KEY,
                asset_id uuid NOT NULL,
                content_sha256 text NOT NULL
                    CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
                size_bytes bigint NOT NULL CHECK (size_bytes >= 0),
                observed_at_utc timestamp with time zone NOT NULL,
                media_type text NULL,
                width integer NULL CHECK (width IS NULL OR width > 0),
                height integer NULL CHECK (height IS NULL OR height > 0),
                CONSTRAINT fk_asset_revisions_asset
                    FOREIGN KEY (asset_id) REFERENCES assets (id) ON DELETE CASCADE,
                UNIQUE (asset_id, content_sha256)
            );

            CREATE OR REPLACE FUNCTION photo_identity_guard_asset_revision_identity()
            RETURNS trigger
            LANGUAGE plpgsql
            AS '
            BEGIN
                IF NEW.id <> OLD.id
                   OR NEW.asset_id <> OLD.asset_id
                   OR NEW.content_sha256 <> OLD.content_sha256 THEN
                    RAISE EXCEPTION ''asset revision identity is immutable'';
                END IF;
                RETURN NEW;
            END;
            ';

            CREATE TRIGGER trg_asset_revision_identity_immutable
                BEFORE UPDATE ON asset_revisions
                FOR EACH ROW
                EXECUTE FUNCTION photo_identity_guard_asset_revision_identity();

            CREATE TABLE face_occurrences (
                id uuid NOT NULL PRIMARY KEY,
                asset_revision_id uuid NOT NULL,
                ordinal integer NOT NULL CHECK (ordinal >= 0),
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_face_occurrences_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                UNIQUE (asset_revision_id, ordinal)
            );

            CREATE TABLE face_observations (
                face_occurrence_id uuid NOT NULL,
                detector_model_id text NOT NULL CHECK (btrim(detector_model_id) <> ''),
                detector_model_hash text NOT NULL
                    CHECK (detector_model_hash ~ '^[0-9a-f]{64}$'),
                confidence double precision NOT NULL
                    CHECK (confidence >= 0 AND confidence <= 1),
                bounding_box_json jsonb NOT NULL,
                landmarks_json jsonb NOT NULL,
                observed_at_utc timestamp with time zone NOT NULL,
                detector_pipeline_hash text NULL
                    CHECK (
                        detector_pipeline_hash IS NULL
                        OR detector_pipeline_hash ~ '^[0-9a-f]{64}$'),
                PRIMARY KEY (
                    face_occurrence_id,
                    detector_model_id,
                    detector_model_hash),
                CONSTRAINT fk_face_observations_occurrence
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE
            );

            CREATE TABLE face_crops (
                id uuid NOT NULL PRIMARY KEY,
                face_occurrence_id uuid NOT NULL,
                crop_protocol text NOT NULL CHECK (btrim(crop_protocol) <> ''),
                content_sha256 text NOT NULL
                    CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
                storage_path text NOT NULL CHECK (btrim(storage_path) <> ''),
                width integer NOT NULL CHECK (width > 0),
                height integer NOT NULL CHECK (height > 0),
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_face_crops_occurrence
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                UNIQUE (face_occurrence_id, crop_protocol, content_sha256)
            );

            CREATE TABLE embeddings (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                face_crop_id uuid NOT NULL,
                model_id text NOT NULL CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                dimensions integer NOT NULL CHECK (dimensions > 0),
                l2_norm double precision NOT NULL CHECK (l2_norm > 0),
                vector_blob bytea NOT NULL,
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_embeddings_crop
                    FOREIGN KEY (face_crop_id)
                    REFERENCES face_crops (id) ON DELETE CASCADE,
                UNIQUE (face_crop_id, model_id, model_hash)
            );

            CREATE TABLE processing_runs (
                id uuid NOT NULL PRIMARY KEY,
                status text NOT NULL
                    CHECK (status IN (
                        'pending',
                        'running',
                        'completed',
                        'failed',
                        'cancelled')),
                configuration_json jsonb NOT NULL,
                started_at_utc timestamp with time zone NOT NULL,
                completed_at_utc timestamp with time zone NULL,
                error text NULL,
                cancellation_requested_at_utc timestamp with time zone NULL,
                CHECK (
                    completed_at_utc IS NULL
                    OR completed_at_utc >= started_at_utc),
                CHECK (
                    cancellation_requested_at_utc IS NULL
                    OR cancellation_requested_at_utc >= started_at_utc),
                CHECK (
                    (status IN ('completed', 'failed', 'cancelled'))
                    = (completed_at_utc IS NOT NULL)),
                CHECK (status <> 'failed' OR error IS NOT NULL)
            );

            CREATE TABLE processing_jobs (
                id uuid NOT NULL PRIMARY KEY,
                processing_run_id uuid NOT NULL,
                asset_revision_id uuid NOT NULL,
                status text NOT NULL
                    CHECK (status IN (
                        'queued',
                        'running',
                        'succeeded',
                        'failed',
                        'cancelled')),
                attempt_count integer NOT NULL DEFAULT 0
                    CHECK (attempt_count >= 0),
                available_at_utc timestamp with time zone NOT NULL,
                started_at_utc timestamp with time zone NULL,
                completed_at_utc timestamp with time zone NULL,
                error text NULL,
                idempotency_key text NOT NULL CHECK (btrim(idempotency_key) <> ''),
                lease_token uuid NULL,
                leased_until_utc timestamp with time zone NULL,
                checkpoint_json jsonb NULL,
                last_failure_kind text NULL
                    CHECK (
                        last_failure_kind IS NULL
                        OR last_failure_kind IN ('transient', 'permanent')),
                CONSTRAINT fk_processing_jobs_run
                    FOREIGN KEY (processing_run_id)
                    REFERENCES processing_runs (id) ON DELETE CASCADE,
                CONSTRAINT fk_processing_jobs_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                UNIQUE (processing_run_id, asset_revision_id),
                UNIQUE (idempotency_key),
                CHECK (
                    (lease_token IS NULL) = (leased_until_utc IS NULL)),
                CHECK (
                    status = 'running'
                    OR (lease_token IS NULL AND leased_until_utc IS NULL)),
                CHECK (
                    status <> 'running'
                    OR (started_at_utc IS NOT NULL
                        AND attempt_count > 0
                        AND lease_token IS NOT NULL
                        AND leased_until_utc IS NOT NULL)),
                CHECK (
                    status NOT IN ('succeeded', 'failed', 'cancelled')
                    OR completed_at_utc IS NOT NULL),
                CHECK (status <> 'failed' OR error IS NOT NULL)
            );

            CREATE INDEX ix_assets_source_presence
                ON assets (source_id, deleted_at_utc, source_key);
            CREATE INDEX ix_asset_revisions_asset_observed
                ON asset_revisions (asset_id, observed_at_utc DESC);
            CREATE INDEX ix_face_occurrences_revision
                ON face_occurrences (asset_revision_id, ordinal);
            CREATE UNIQUE INDEX ux_face_observations_pipeline
                ON face_observations (face_occurrence_id, detector_pipeline_hash)
                WHERE detector_pipeline_hash IS NOT NULL;
            CREATE INDEX ix_embeddings_crop
                ON embeddings (face_crop_id);
            CREATE INDEX ix_processing_jobs_ready
                ON processing_jobs (status, available_at_utc);
            CREATE INDEX ix_processing_jobs_claimable
                ON processing_jobs (
                    processing_run_id,
                    status,
                    available_at_utc,
                    leased_until_utc);
            """),
        new(
            3,
            "archive-analysis-state",
            """
            CREATE TABLE archive_analysis_profiles (
                profile_hash text NOT NULL PRIMARY KEY
                    CHECK (profile_hash ~ '^[0-9a-f]{64}$'),
                detector_pipeline_hash text NOT NULL
                    CHECK (detector_pipeline_hash ~ '^[0-9a-f]{64}$'),
                detector_model_id text NOT NULL
                    CHECK (btrim(detector_model_id) <> ''),
                detector_model_hash text NOT NULL
                    CHECK (detector_model_hash ~ '^[0-9a-f]{64}$'),
                embedder_model_id text NOT NULL
                    CHECK (btrim(embedder_model_id) <> ''),
                embedder_model_hash text NOT NULL
                    CHECK (embedder_model_hash ~ '^[0-9a-f]{64}$'),
                alignment_protocol text NOT NULL
                    CHECK (btrim(alignment_protocol) <> ''),
                canonical_definition text NOT NULL
                    CHECK (btrim(canonical_definition) <> ''),
                recorded_at_utc timestamp with time zone NOT NULL
            );

            CREATE TABLE archive_analysis_runs (
                processing_run_id uuid NOT NULL PRIMARY KEY,
                profile_hash text NOT NULL,
                registered_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_archive_analysis_runs_processing_run
                    FOREIGN KEY (processing_run_id)
                    REFERENCES processing_runs (id) ON DELETE CASCADE,
                CONSTRAINT fk_archive_analysis_runs_profile
                    FOREIGN KEY (profile_hash)
                    REFERENCES archive_analysis_profiles (profile_hash) ON DELETE RESTRICT
            );

            CREATE TABLE asset_revision_analysis (
                asset_revision_id uuid NOT NULL,
                profile_hash text NOT NULL,
                processing_run_id uuid NOT NULL,
                completed_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (asset_revision_id, profile_hash),
                CONSTRAINT fk_asset_revision_analysis_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CONSTRAINT fk_asset_revision_analysis_profile
                    FOREIGN KEY (profile_hash)
                    REFERENCES archive_analysis_profiles (profile_hash) ON DELETE RESTRICT,
                CONSTRAINT fk_asset_revision_analysis_processing_run
                    FOREIGN KEY (processing_run_id)
                    REFERENCES processing_runs (id) ON DELETE RESTRICT
            );

            CREATE INDEX ix_asset_revision_analysis_profile
                ON asset_revision_analysis (profile_hash, asset_revision_id);
            """),
        new(
            4,
            "archive-asset-availability",
            """
            CREATE TABLE archive_asset_availability (
                asset_id uuid NOT NULL PRIMARY KEY,
                availability text NOT NULL
                    CHECK (availability IN (
                        'local',
                        'online-only',
                        'downloading',
                        'unavailable',
                        'error')),
                checked_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_archive_asset_availability_asset
                    FOREIGN KEY (asset_id)
                    REFERENCES assets (id) ON DELETE CASCADE
            );

            CREATE INDEX ix_archive_asset_availability_state
                ON archive_asset_availability (availability, asset_id);
            """),
        new(
            5,
            "archive-source-observations",
            """
            CREATE TABLE archive_source_observations (
                asset_id uuid NOT NULL PRIMARY KEY,
                observed_size_bytes bigint NOT NULL CHECK (observed_size_bytes >= 0),
                observed_last_write_utc timestamp with time zone NOT NULL,
                observed_media_type text NOT NULL CHECK (btrim(observed_media_type) <> ''),
                observed_at_utc timestamp with time zone NOT NULL,
                verification_state text NOT NULL
                    CHECK (verification_state IN (
                        'verified',
                        'needs-source-verification',
                        'unverified')),
                verified_revision_id uuid NULL,
                verified_size_bytes bigint NULL
                    CHECK (verified_size_bytes IS NULL OR verified_size_bytes >= 0),
                verified_last_write_utc timestamp with time zone NULL,
                verified_media_type text NULL,
                verified_at_utc timestamp with time zone NULL,
                CONSTRAINT fk_archive_source_observations_asset
                    FOREIGN KEY (asset_id)
                    REFERENCES assets (id) ON DELETE CASCADE,
                CONSTRAINT fk_archive_source_observations_revision
                    FOREIGN KEY (verified_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE SET NULL,
                CHECK (
                    verified_revision_id IS NOT NULL
                    OR (
                        verified_size_bytes IS NULL
                        AND verified_last_write_utc IS NULL
                        AND verified_media_type IS NULL
                        AND verified_at_utc IS NULL))
            );

            CREATE INDEX ix_archive_source_observations_verification
                ON archive_source_observations (
                    verification_state,
                    observed_at_utc,
                    asset_id);
            """),
        new(
            6,
            "archive-coverage",
            """
            CREATE TABLE archive_configuration (
                id smallint NOT NULL PRIMARY KEY CHECK (id = 1),
                source_id uuid NOT NULL UNIQUE,
                configured_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_archive_configuration_source
                    FOREIGN KEY (source_id)
                    REFERENCES sources (id) ON DELETE RESTRICT
            );

            CREATE TABLE archive_included_folders (
                source_id uuid NOT NULL,
                relative_path text NOT NULL,
                included_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (source_id, relative_path),
                CONSTRAINT fk_archive_included_folders_source
                    FOREIGN KEY (source_id)
                    REFERENCES sources (id) ON DELETE CASCADE
            );
            """),
        new(
            7,
            "archive-advancement-control",
            """
            CREATE TABLE archive_advancement_control (
                source_id uuid NOT NULL PRIMARY KEY,
                desired_state text NOT NULL
                    CHECK (desired_state IN ('running', 'paused')),
                runtime_state text NOT NULL
                    CHECK (btrim(runtime_state) <> ''),
                sync_required boolean NOT NULL,
                message text NULL,
                updated_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_archive_advancement_control_source
                    FOREIGN KEY (source_id)
                    REFERENCES sources (id) ON DELETE CASCADE
            );
            """),
        new(
            8,
            "archive-review-proxies-and-post-analysis",
            """
            CREATE TABLE archive_review_proxy_profiles (
                profile_id text NOT NULL PRIMARY KEY,
                protocol_version text NOT NULL,
                encoder text NOT NULL,
                format text NOT NULL,
                jpeg_quality integer NOT NULL
                    CHECK (jpeg_quality BETWEEN 1 AND 100),
                maximum_long_edge integer NOT NULL
                    CHECK (maximum_long_edge > 0),
                resize_policy text NOT NULL,
                canonical_definition text NOT NULL,
                recorded_at_utc timestamp with time zone NOT NULL
            );

            CREATE TABLE asset_revision_review_proxies (
                asset_revision_id uuid NOT NULL,
                profile_id text NOT NULL,
                encoded_byte_length bigint NOT NULL
                    CHECK (encoded_byte_length > 0),
                content_sha256 text NOT NULL
                    CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
                width integer NOT NULL CHECK (width > 0),
                height integer NOT NULL CHECK (height > 0),
                generated_at_utc timestamp with time zone NOT NULL,
                relative_path text NOT NULL
                    CHECK (btrim(relative_path) <> ''),
                PRIMARY KEY (asset_revision_id, profile_id),
                UNIQUE (relative_path),
                CONSTRAINT fk_asset_revision_review_proxies_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CONSTRAINT fk_asset_revision_review_proxies_profile
                    FOREIGN KEY (profile_id)
                    REFERENCES archive_review_proxy_profiles (profile_id)
                    ON DELETE RESTRICT
            );

            CREATE INDEX ix_asset_revision_review_proxies_profile
                ON asset_revision_review_proxies (
                    profile_id,
                    asset_revision_id);
            """),
        new(
            9,
            "archive-managed-hydration-ownership",
            """
            CREATE TABLE asset_revision_managed_hydrations (
                asset_revision_id uuid NOT NULL PRIMARY KEY,
                requested_at_utc timestamp with time zone NOT NULL,
                release_requested_at_utc timestamp with time zone NULL,
                released_at_utc timestamp with time zone NULL,
                CONSTRAINT fk_asset_revision_managed_hydrations_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE
            );

            CREATE INDEX ix_asset_revision_managed_hydrations_active
                ON asset_revision_managed_hydrations (
                    released_at_utc,
                    asset_revision_id);

            CREATE TABLE asset_revision_managed_hydration_usage (
                asset_revision_id uuid NOT NULL PRIMARY KEY,
                last_needed_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_asset_revision_managed_hydration_usage_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE
            );

            CREATE TABLE archive_source_managed_hydrations (
                asset_id uuid NOT NULL PRIMARY KEY,
                requested_at_utc timestamp with time zone NOT NULL,
                release_requested_at_utc timestamp with time zone NULL,
                released_at_utc timestamp with time zone NULL,
                CONSTRAINT fk_archive_source_managed_hydrations_asset
                    FOREIGN KEY (asset_id)
                    REFERENCES assets (id) ON DELETE CASCADE
            );

            CREATE INDEX ix_archive_source_managed_hydrations_active
                ON archive_source_managed_hydrations (
                    released_at_utc,
                    asset_id);

            CREATE TABLE archive_source_managed_hydration_usage (
                asset_id uuid NOT NULL PRIMARY KEY,
                last_needed_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_archive_source_managed_hydration_usage_asset
                    FOREIGN KEY (asset_id)
                    REFERENCES assets (id) ON DELETE CASCADE
            );
            """),
        new(
            10,
            "capture-metadata-and-place-enrichment-state",
            """
            CREATE TABLE photo_capture_metadata (
                asset_revision_id uuid NOT NULL PRIMARY KEY,
                taken_at_local timestamp without time zone NULL,
                utc_offset_minutes smallint NULL
                    CHECK (
                        utc_offset_minutes IS NULL
                        OR utc_offset_minutes BETWEEN -840 AND 840),
                latitude double precision NULL
                    CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
                longitude double precision NULL
                    CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
                extracted_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_photo_capture_metadata_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CHECK ((latitude IS NULL) = (longitude IS NULL)),
                CHECK (
                    utc_offset_minutes IS NULL
                    OR taken_at_local IS NOT NULL)
            );

            CREATE INDEX ix_photo_capture_metadata_taken
                ON photo_capture_metadata (
                    taken_at_local,
                    asset_revision_id);
            CREATE INDEX ix_photo_capture_metadata_location
                ON photo_capture_metadata (
                    latitude,
                    longitude,
                    asset_revision_id);

            CREATE TABLE photo_place_reverse_geocode_cache (
                provider text NOT NULL
                    CHECK (char_length(provider) BETWEEN 1 AND 80),
                contract_key text NOT NULL
                    CHECK (char_length(contract_key) BETWEEN 1 AND 500),
                latitude double precision NOT NULL
                    CHECK (latitude BETWEEN -90 AND 90),
                longitude double precision NOT NULL
                    CHECK (longitude BETWEEN -180 AND 180),
                place_value text NOT NULL
                    CHECK (char_length(place_value) BETWEEN 1 AND 500),
                provider_result_id text NULL,
                country_code text NULL,
                resolved_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (
                    provider,
                    contract_key,
                    latitude,
                    longitude)
            );

            CREATE TABLE photo_place_enrichment_attempts (
                asset_revision_id uuid NOT NULL,
                provider text NOT NULL
                    CHECK (char_length(provider) BETWEEN 1 AND 80),
                contract_key text NOT NULL
                    CHECK (char_length(contract_key) BETWEEN 1 AND 500),
                latitude double precision NOT NULL
                    CHECK (latitude BETWEEN -90 AND 90),
                longitude double precision NOT NULL
                    CHECK (longitude BETWEEN -180 AND 180),
                status text NOT NULL
                    CHECK (
                        status IN (
                            'succeeded',
                            'skipped',
                            'deferred',
                            'failed')),
                attempt_count integer NOT NULL DEFAULT 0
                    CHECK (attempt_count >= 0),
                place_value text NULL
                    CHECK (
                        place_value IS NULL
                        OR char_length(place_value) BETWEEN 1 AND 500),
                provider_result_id text NULL,
                country_code text NULL,
                last_error_code text NULL,
                last_error_message text NULL,
                last_attempted_at_utc timestamp with time zone NOT NULL,
                completed_at_utc timestamp with time zone NULL,
                PRIMARY KEY (
                    asset_revision_id,
                    provider,
                    contract_key),
                CONSTRAINT fk_photo_place_enrichment_attempts_revision
                    FOREIGN KEY (asset_revision_id)
                    REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CHECK (
                    (status = 'succeeded'
                        AND completed_at_utc IS NOT NULL
                        AND place_value IS NOT NULL)
                    OR (status = 'skipped'
                        AND completed_at_utc IS NOT NULL
                        AND place_value IS NULL)
                    OR status IN ('deferred', 'failed'))
            );

            CREATE INDEX ix_photo_place_enrichment_attempts_resume
                ON photo_place_enrichment_attempts (
                    provider,
                    contract_key,
                    status,
                    last_attempted_at_utc,
                    asset_revision_id);
            """),
        new(
            11,
            "review-actions-and-people",
            """
            CREATE TABLE people (
                id uuid NOT NULL PRIMARY KEY,
                display_name text NULL
                    CHECK (
                        display_name IS NULL
                        OR char_length(display_name) BETWEEN 1 AND 200),
                created_at_utc timestamp with time zone NOT NULL,
                merged_into_person_id uuid NULL,
                CONSTRAINT fk_people_merged_into
                    FOREIGN KEY (merged_into_person_id)
                    REFERENCES people (id) ON DELETE RESTRICT,
                CHECK (
                    merged_into_person_id IS NULL
                    OR merged_into_person_id <> id)
            );

            CREATE TABLE person_labels (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                person_id uuid NOT NULL,
                face_occurrence_id uuid NOT NULL,
                label_kind text NOT NULL
                    CHECK (btrim(label_kind) <> ''),
                assigned_by text NOT NULL
                    CHECK (btrim(assigned_by) <> ''),
                assigned_at_utc timestamp with time zone NOT NULL,
                note text NULL,
                CONSTRAINT fk_person_labels_person
                    FOREIGN KEY (person_id)
                    REFERENCES people (id) ON DELETE CASCADE,
                CONSTRAINT fk_person_labels_face
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                UNIQUE (person_id, face_occurrence_id, label_kind)
            );

            CREATE INDEX ix_person_labels_occurrence
                ON person_labels (face_occurrence_id);

            CREATE TABLE identity_suggestions (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                face_occurrence_id uuid NOT NULL,
                suggested_person_id uuid NOT NULL,
                model_id text NOT NULL
                    CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL
                    CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                score double precision NOT NULL,
                status text NOT NULL
                    CHECK (status IN ('pending', 'accepted', 'rejected')),
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_identity_suggestions_face
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                CONSTRAINT fk_identity_suggestions_person
                    FOREIGN KEY (suggested_person_id)
                    REFERENCES people (id) ON DELETE CASCADE,
                UNIQUE (
                    face_occurrence_id,
                    suggested_person_id,
                    model_id,
                    model_hash)
            );

            CREATE INDEX ix_identity_suggestions_occurrence
                ON identity_suggestions (face_occurrence_id, status);

            CREATE TABLE review_actions (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                face_occurrence_id uuid NOT NULL,
                action_kind text NOT NULL
                    CHECK (action_kind IN ('assign', 'unknown', 'reject', 'undo')),
                person_id uuid NULL,
                person_label_id bigint NULL,
                actor text NOT NULL
                    CHECK (btrim(actor) <> ''),
                note text NULL,
                created_at_utc timestamp with time zone NOT NULL,
                reversed_at_utc timestamp with time zone NULL,
                reverses_action_id bigint NULL,
                CONSTRAINT fk_review_actions_face
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                CONSTRAINT fk_review_actions_person
                    FOREIGN KEY (person_id)
                    REFERENCES people (id) ON DELETE RESTRICT,
                CONSTRAINT fk_review_actions_label
                    FOREIGN KEY (person_label_id)
                    REFERENCES person_labels (id) ON DELETE RESTRICT,
                CONSTRAINT fk_review_actions_reverses
                    FOREIGN KEY (reverses_action_id)
                    REFERENCES review_actions (id) ON DELETE RESTRICT,
                CHECK (
                    (action_kind = 'assign'
                        AND person_id IS NOT NULL
                        AND person_label_id IS NOT NULL
                        AND reverses_action_id IS NULL)
                    OR (action_kind IN ('unknown', 'reject')
                        AND person_id IS NULL
                        AND person_label_id IS NULL
                        AND reverses_action_id IS NULL)
                    OR (action_kind = 'undo'
                        AND reverses_action_id IS NOT NULL))
            );

            CREATE INDEX ix_review_actions_face_history
                ON review_actions (face_occurrence_id, id DESC);
            CREATE INDEX ix_review_actions_face_active
                ON review_actions (
                    face_occurrence_id,
                    action_kind,
                    reversed_at_utc,
                    id DESC);

            CREATE TABLE identity_suggestion_review_actions (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                suggestion_id bigint NOT NULL,
                action_kind text NOT NULL
                    CHECK (action_kind IN ('accept', 'reject')),
                review_action_id bigint NULL,
                actor text NOT NULL
                    CHECK (btrim(actor) <> ''),
                note text NULL,
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_identity_suggestion_review_actions_suggestion
                    FOREIGN KEY (suggestion_id)
                    REFERENCES identity_suggestions (id) ON DELETE CASCADE,
                CONSTRAINT fk_identity_suggestion_review_actions_review
                    FOREIGN KEY (review_action_id)
                    REFERENCES review_actions (id) ON DELETE RESTRICT,
                CHECK (
                    (action_kind = 'accept' AND review_action_id IS NOT NULL)
                    OR (action_kind = 'reject' AND review_action_id IS NULL))
            );

            CREATE INDEX ix_identity_suggestion_review_history
                ON identity_suggestion_review_actions (
                    suggestion_id,
                    id DESC);
            """),
        new(
            12,
            "identity-suggestion-rankings",
            """
            CREATE TABLE identity_suggestion_rankings (
                face_occurrence_id uuid NOT NULL,
                model_id text NOT NULL
                    CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL
                    CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                rank integer NOT NULL
                    CHECK (rank IN (1, 2)),
                suggestion_id bigint NOT NULL UNIQUE,
                score_margin double precision NULL
                    CHECK (score_margin IS NULL OR score_margin >= 0),
                generated_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (
                    face_occurrence_id,
                    model_id,
                    model_hash,
                    rank),
                CONSTRAINT fk_identity_suggestion_rankings_face
                    FOREIGN KEY (face_occurrence_id)
                    REFERENCES face_occurrences (id) ON DELETE CASCADE,
                CONSTRAINT fk_identity_suggestion_rankings_suggestion
                    FOREIGN KEY (suggestion_id)
                    REFERENCES identity_suggestions (id) ON DELETE CASCADE
            );

            CREATE INDEX ix_identity_suggestion_rankings_model
                ON identity_suggestion_rankings (
                    model_id,
                    model_hash);
            """),
        new(
            13,
            "person-maintenance-and-favorites",
            """
            CREATE TABLE person_favorites (
                person_id uuid NOT NULL PRIMARY KEY,
                favorited_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT fk_person_favorites_person
                    FOREIGN KEY (person_id)
                    REFERENCES people (id) ON DELETE CASCADE
            );

            CREATE TABLE person_maintenance_actions (
                id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                action_kind text NOT NULL
                    CHECK (action_kind IN ('rename', 'merge')),
                person_id uuid NOT NULL,
                previous_display_name text NOT NULL
                    CHECK (char_length(previous_display_name) BETWEEN 1 AND 200),
                target_person_id uuid NULL,
                new_display_name text NOT NULL
                    CHECK (char_length(new_display_name) BETWEEN 1 AND 200),
                actor text NOT NULL
                    CHECK (char_length(btrim(actor)) BETWEEN 1 AND 200),
                note text NULL
                    CHECK (note IS NULL OR char_length(note) <= 1000),
                created_at_utc timestamp with time zone NOT NULL,
                reversible boolean NOT NULL,
                CONSTRAINT fk_person_maintenance_person
                    FOREIGN KEY (person_id)
                    REFERENCES people (id) ON DELETE RESTRICT,
                CONSTRAINT fk_person_maintenance_target
                    FOREIGN KEY (target_person_id)
                    REFERENCES people (id) ON DELETE RESTRICT,
                CHECK (
                    (action_kind = 'rename'
                        AND target_person_id IS NULL
                        AND reversible)
                    OR (action_kind = 'merge'
                        AND target_person_id IS NOT NULL
                        AND NOT reversible))
            );

            CREATE INDEX ix_person_maintenance_history
                ON person_maintenance_actions (id DESC);
            CREATE INDEX ix_person_maintenance_person
                ON person_maintenance_actions (person_id, id DESC);
            """),
        new(
            14,
            "identity-suggestion-policy",
            """
            CREATE TABLE identity_suggestion_policies (
                model_id text NOT NULL
                    CHECK (btrim(model_id) <> ''),
                model_hash text NOT NULL
                    CHECK (model_hash ~ '^[0-9a-f]{64}$'),
                policy_version integer NOT NULL
                    CHECK (policy_version >= 1),
                auto_assign_enabled boolean NOT NULL,
                high_score_threshold double precision NOT NULL
                    CHECK (high_score_threshold BETWEEN 0 AND 1),
                high_margin_threshold double precision NOT NULL
                    CHECK (high_margin_threshold BETWEEN 0 AND 2),
                medium_score_threshold double precision NOT NULL
                    CHECK (medium_score_threshold BETWEEN 0 AND 1),
                updated_by text NOT NULL
                    CHECK (btrim(updated_by) <> ''),
                updated_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (model_id, model_hash),
                CHECK (medium_score_threshold <= high_score_threshold)
            );
            """),
    ];

    private readonly NpgsqlDataSource _dataSource;

    public PostgresCatalogueDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlDataSourceBuilder builder = new(connectionString);
        builder.ConnectionStringBuilder.ApplicationName = "PhotoIdentity";
        _dataSource = builder.Build();
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default) =>
        await _dataSource.OpenConnectionAsync(cancellationToken);

    public async Task<PostgresInitializationResult> TryInitializeAsync(
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection;
        try
        {
            connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsAuthenticationFailure(exception))
        {
            return new(
                PostgresCatalogueHealth.AuthenticationFailed,
                exception);
        }
        catch (Exception exception) when (IsConnectionUnavailable(exception))
        {
            return new(
                PostgresCatalogueHealth.Unavailable,
                exception);
        }

        await using (connection)
        {
            try
            {
                int schemaVersion = await InitializeAsync(connection, cancellationToken);
                return new(
                    PostgresCatalogueHealth.Ready(schemaVersion),
                    null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new(
                    PostgresCatalogueHealth.MigrationFailed,
                    exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    private static async Task<int> InitializeAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand migrationLock = connection.CreateCommand())
        {
            migrationLock.Transaction = transaction;
            migrationLock.CommandText =
                "SELECT pg_advisory_xact_lock(@migration_lock_key);";
            migrationLock.Parameters.AddWithValue(
                "migration_lock_key",
                MigrationAdvisoryLockKey);
            await migrationLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand ensureHistory = connection.CreateCommand())
        {
            ensureHistory.Transaction = transaction;
            ensureHistory.CommandText =
                """
                CREATE TABLE IF NOT EXISTS photo_identity_schema_migrations (
                    version integer NOT NULL PRIMARY KEY,
                    name text NOT NULL,
                    applied_at_utc timestamp with time zone NOT NULL
                );
                """;
            await ensureHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        HashSet<int> appliedVersions = [];
        await using (NpgsqlCommand readHistory = connection.CreateCommand())
        {
            readHistory.Transaction = transaction;
            readHistory.CommandText =
                """
                SELECT version
                FROM photo_identity_schema_migrations
                ORDER BY version;
                """;

            await using NpgsqlDataReader reader =
                await readHistory.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int version = reader.GetInt32(0);
                if (version > CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"PostgreSQL catalogue schema version {version} is newer than supported version {CurrentSchemaVersion}.");
                }

                appliedVersions.Add(version);
            }
        }

        foreach (Migration migration in Migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            await using (NpgsqlCommand applyMigration = connection.CreateCommand())
            {
                applyMigration.Transaction = transaction;
                applyMigration.CommandText = migration.Sql;
                await applyMigration.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (NpgsqlCommand recordMigration = connection.CreateCommand())
            {
                recordMigration.Transaction = transaction;
                recordMigration.CommandText =
                    """
                    INSERT INTO photo_identity_schema_migrations (
                        version,
                        name,
                        applied_at_utc)
                    VALUES (
                        @version,
                        @name,
                        @applied_at_utc);
                    """;
                recordMigration.Parameters.AddWithValue(
                    "version",
                    migration.Version);
                recordMigration.Parameters.AddWithValue(
                    "name",
                    migration.Name);
                recordMigration.Parameters.AddWithValue(
                    "applied_at_utc",
                    DateTimeOffset.UtcNow);
                await recordMigration.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return CurrentSchemaVersion;
    }

    private static bool IsAuthenticationFailure(PostgresException exception) =>
        exception.SqlState.StartsWith("28", StringComparison.Ordinal);

    private static bool IsConnectionUnavailable(Exception exception) =>
        exception is NpgsqlException or TimeoutException;

    private sealed record Migration(int Version, string Name, string Sql);
}
