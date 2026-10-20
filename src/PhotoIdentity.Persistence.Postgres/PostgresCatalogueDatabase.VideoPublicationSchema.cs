namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0188 v36: private, revision-bound derivative publication receipts.
/// A registry entry and successful job state must be committed together.
/// No public media route, worker registration or FFmpeg profile is installed.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoPublicationSchemaMigration = """
        CREATE TABLE video_published_derivatives (
            job_id uuid NOT NULL PRIMARY KEY
                REFERENCES video_processing_jobs(id) ON DELETE CASCADE,
            asset_revision_id uuid NOT NULL
                REFERENCES video_revision_metadata(asset_revision_id) ON DELETE CASCADE,
            artifact_relative_path text NOT NULL UNIQUE
                CHECK (artifact_relative_path ~
                    '^video-derivatives/[0-9a-f]{32}/[0-9a-f]{32}[.]mp4$'),
            content_sha256 text NOT NULL
                CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
            encoded_bytes bigint NOT NULL
                CHECK (encoded_bytes BETWEEN 1 AND 2147483648),
            created_at_utc timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX ix_video_published_derivatives_revision
            ON video_published_derivatives(asset_revision_id);
        """;
}
