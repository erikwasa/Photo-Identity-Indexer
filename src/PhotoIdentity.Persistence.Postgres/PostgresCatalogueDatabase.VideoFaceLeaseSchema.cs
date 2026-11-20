namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0155 v43: bind each leased face-evidence publication to the exact
/// video job. Historical v42/unleased batches retain NULL, so they cannot
/// accidentally satisfy the durable-success receipt of a different job.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoFaceLeaseSchemaMigration = """
        ALTER TABLE video_face_evidence_batches
        ADD COLUMN publishing_job_id uuid NULL
            REFERENCES video_processing_jobs(id) ON DELETE SET NULL;

        CREATE UNIQUE INDEX ux_video_face_batch_publishing_job
            ON video_face_evidence_batches(publishing_job_id)
            WHERE publishing_job_id IS NOT NULL;
        """;
}
