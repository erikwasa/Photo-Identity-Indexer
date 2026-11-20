namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0155 v45: allow an immutable, zero-observation batch only as a durable
/// result of fully sampled, source-attested, selected FaceEvidence work.
/// Legacy v42-v44 rows remain unchanged; no photo/person tables are touched.
/// </summary>
public sealed partial class PostgresCatalogueDatabase
{
    private const string VideoFaceEmptyResultSchemaMigration = """
        ALTER TABLE video_face_evidence_batches
            DROP CONSTRAINT video_face_evidence_batches_observation_count_check;
        ALTER TABLE video_face_evidence_batches
            ADD CONSTRAINT ck_video_face_batch_observation_count
            CHECK (observation_count BETWEEN 0 AND 256);
        """;
}
