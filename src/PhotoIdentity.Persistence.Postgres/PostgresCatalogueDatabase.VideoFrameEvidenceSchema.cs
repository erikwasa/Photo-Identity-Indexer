namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0190 v38: private, lease-published frame evidence. The batch receipt,
/// all frame rows and FrameEvidence job success commit in one transaction.
/// No public search, caption API, worker or automatic video activation.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoFrameEvidenceSchemaMigration = """
        CREATE TABLE video_frame_evidence_batches (
            job_id uuid PRIMARY KEY
                REFERENCES video_processing_jobs(id) ON DELETE CASCADE,
            asset_revision_id uuid NOT NULL
                REFERENCES video_revision_metadata(asset_revision_id) ON DELETE CASCADE,
            sampling_policy_version text NOT NULL
                CHECK (sampling_policy_version ~ '^[a-z0-9_./-]{1,100}$'),
            extraction_profile_version text NOT NULL
                CHECK (extraction_profile_version ~ '^[a-z0-9_./-]{1,100}$'),
            clip_model_id text NOT NULL CHECK (char_length(clip_model_id) BETWEEN 1 AND 100),
            clip_model_sha256 text NOT NULL
                CHECK (clip_model_sha256 ~ '^[0-9a-f]{64}$'),
            clip_preprocessing_version text NOT NULL
                CHECK (char_length(clip_preprocessing_version) BETWEEN 1 AND 100),
            caption_model_id text NOT NULL
                CHECK (char_length(caption_model_id) BETWEEN 1 AND 100),
            caption_model_sha256 text NOT NULL
                CHECK (caption_model_sha256 ~ '^[0-9a-f]{64}$'),
            caption_prompt_version text NOT NULL
                CHECK (char_length(caption_prompt_version) BETWEEN 1 AND 100),
            frame_count integer NOT NULL CHECK (frame_count BETWEEN 1 AND 128),
            embedding_dimensions integer NOT NULL
                CHECK (embedding_dimensions BETWEEN 1 AND 2048),
            published_at_utc timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX ix_video_frame_evidence_batches_revision
            ON video_frame_evidence_batches(asset_revision_id, published_at_utc DESC);

        CREATE TABLE video_frame_evidence_frames (
            job_id uuid NOT NULL
                REFERENCES video_frame_evidence_batches(job_id) ON DELETE CASCADE,
            ordinal integer NOT NULL CHECK (ordinal BETWEEN 0 AND 127),
            timestamp_ms bigint NOT NULL
                CHECK (timestamp_ms BETWEEN 0 AND 3600000),
            scene_index integer NULL CHECK (scene_index IS NULL OR scene_index >= 0),
            caption text NOT NULL
                CHECK (char_length(caption) BETWEEN 1 AND 500
                       AND btrim(caption) = caption),
            embedding_le_f32 bytea NOT NULL
                CHECK (octet_length(embedding_le_f32) BETWEEN 4 AND 8192
                       AND octet_length(embedding_le_f32) % 4 = 0),
            PRIMARY KEY(job_id, ordinal),
            UNIQUE(job_id, timestamp_ms)
        );
        """;
}
