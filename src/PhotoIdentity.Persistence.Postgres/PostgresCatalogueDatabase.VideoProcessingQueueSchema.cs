namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0188 schema v34: an independent queue for deliberately selected video
/// derivatives. No image/photo job table or existing video metadata is changed.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoProcessingQueueSchemaMigration = """
        CREATE TABLE video_processing_jobs (
            id uuid NOT NULL PRIMARY KEY,
            asset_revision_id uuid NOT NULL REFERENCES video_revision_metadata (asset_revision_id)
                ON DELETE CASCADE,
            idempotency_key text NOT NULL UNIQUE
                CHECK (idempotency_key ~ '^[0-9a-f]{64}$'),
            derivative_kind text NOT NULL
                CHECK (derivative_kind IN ('PlayableProxy', 'FrameEvidence', 'FaceEvidence')),
            policy_version text NOT NULL
                CHECK (policy_version ~ '^[a-z0-9_.-]{1,80}$'),
            preset_version text NOT NULL
                CHECK (preset_version ~ '^[a-z0-9_.-]{1,80}$'),
            tool_sha256 text NOT NULL
                CHECK (tool_sha256 ~ '^[0-9a-f]{64}$'),
            source_bytes bigint NOT NULL CHECK (source_bytes BETWEEN 1 AND 1073741824),
            reserved_temporary_bytes bigint NOT NULL
                CHECK (reserved_temporary_bytes BETWEEN 1 AND 8589934592),
            requested_frames integer NOT NULL CHECK (requested_frames BETWEEN 0 AND 128),
            status text NOT NULL DEFAULT 'queued'
                CHECK (status IN ('queued', 'running', 'succeeded', 'failed', 'cancelled')),
            attempt_count integer NOT NULL DEFAULT 0
                CHECK (attempt_count BETWEEN 0 AND 3),
            progress_percent integer NOT NULL DEFAULT 0
                CHECK (progress_percent BETWEEN 0 AND 100),
            requested_at_utc timestamptz NOT NULL DEFAULT now(),
            available_at_utc timestamptz NOT NULL DEFAULT now(),
            updated_at_utc timestamptz NOT NULL DEFAULT now(),
            completed_at_utc timestamptz NULL,
            cancellation_requested_at_utc timestamptz NULL,
            lease_token uuid NULL,
            leased_until_utc timestamptz NULL,
            failure_code text NULL
                CHECK (failure_code IS NULL OR failure_code IN
                    ('attempt-limit', 'worker-error', 'source-unavailable', 'revision-invalid')),
            CONSTRAINT ck_video_job_lease_pair CHECK
                ((lease_token IS NULL) = (leased_until_utc IS NULL)),
            CONSTRAINT ck_video_job_running_lease CHECK
                ((status = 'running') = (lease_token IS NOT NULL)),
            CONSTRAINT ck_video_job_terminal CHECK
                ((status IN ('succeeded','failed','cancelled')) = (completed_at_utc IS NOT NULL)),
            CONSTRAINT ck_video_job_failed_code CHECK
                ((status = 'failed') = (failure_code IS NOT NULL))
        );

        CREATE INDEX ix_video_processing_jobs_claim
            ON video_processing_jobs (status, available_at_utc, requested_at_utc, id);
        CREATE INDEX ix_video_processing_jobs_revision
            ON video_processing_jobs (asset_revision_id, status);
        """;
}
