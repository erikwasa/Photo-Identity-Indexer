namespace PhotoIdentity.Persistence.Postgres;

/// <summary>WI-0188 v37: opt-in, time-limited single-job authorization, off by default.</summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoScopedAttemptSchemaMigration = """
        ALTER TABLE video_processing_control
        ADD COLUMN selected_job_id uuid NULL REFERENCES video_processing_jobs(id),
        ADD COLUMN authorization_expires_at_utc timestamptz NULL;

        ALTER TABLE video_processing_control
        DROP CONSTRAINT video_processing_control_last_action_check;
        ALTER TABLE video_processing_control
        ADD CONSTRAINT ck_video_control_last_action
        CHECK (last_action IN ('initial','pause','resume','stop',
                              'arm-selected','disarm-selected'));

        ALTER TABLE video_processing_control
        ADD CONSTRAINT ck_video_control_selected_pair
        CHECK ((selected_job_id IS NULL) = (authorization_expires_at_utc IS NULL));

        ALTER TABLE video_processing_control
        ADD CONSTRAINT ck_video_control_scoped_enablement
        CHECK (NOT enabled OR (selected_job_id IS NOT NULL
             AND authorization_expires_at_utc IS NOT NULL));
        """;
}
