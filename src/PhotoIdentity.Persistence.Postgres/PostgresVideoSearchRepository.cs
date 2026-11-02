using System.Data;
using Npgsql;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// WI-0191: opt-in read-only current frame evidence. No source paths, proxy
/// reads, source hydration, jobs, migrations or writes. All eligibility and
/// selected batch rows come from one PostgreSQL repeatable-read snapshot.
/// </summary>
public sealed class PostgresVideoSearchRepository(PostgresCatalogueDatabase database)
{
    public const int MaximumCandidateVideos = 256;

    public async Task<IReadOnlyList<VideoSearchCandidate>> ReadCurrentAsync(
        CancellationToken token = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, token);
        await using (NpgsqlCommand readOnly = connection.CreateCommand())
        {
            readOnly.Transaction = tx;
            readOnly.CommandText = "SET TRANSACTION READ ONLY;";
            await readOnly.ExecuteNonQueryAsync(token);
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            WITH latest_batches AS (
                SELECT DISTINCT ON (batch.asset_revision_id)
                    batch.job_id, batch.asset_revision_id, batch.frame_count,
                    batch.embedding_dimensions, batch.sampling_policy_version,
                    batch.clip_model_id, batch.clip_model_sha256,
                    batch.clip_preprocessing_version, batch.caption_model_id,
                    batch.caption_prompt_version, batch.published_at_utc
                FROM video_frame_evidence_batches batch
                JOIN video_processing_jobs job ON job.id = batch.job_id
                JOIN asset_revisions revision ON revision.id = batch.asset_revision_id
                JOIN assets asset ON asset.id = revision.asset_id
                JOIN sources source ON source.id = asset.source_id
                JOIN video_revision_metadata metadata
                    ON metadata.asset_revision_id = revision.id
                WHERE job.status = 'succeeded'
                  AND job.derivative_kind = 'FrameEvidence'
                  AND job.asset_revision_id = batch.asset_revision_id
                  AND asset.media_kind = 'video'
                  AND revision.media_kind = 'video'
                  AND asset.deleted_at_utc IS NULL
                  AND source.kind = 'local-folder'
                  AND metadata.inspection_status = 'supported'
                  AND metadata.color_transfer = 'bt709'
                  AND batch.frame_count BETWEEN 1 AND 24
                  AND batch.embedding_dimensions BETWEEN 1 AND 2048
                  AND revision.id = (
                      SELECT recent.id FROM asset_revisions recent
                      WHERE recent.asset_id = asset.id
                        AND recent.media_kind = 'video'
                      ORDER BY recent.observed_at_utc DESC, recent.id DESC LIMIT 1)
                  AND NOT EXISTS (
                      SELECT 1 FROM source_copy_exclusions exclusion
                      WHERE exclusion.source_id = asset.source_id
                        AND exclusion.source_key = asset.source_key)
                ORDER BY batch.asset_revision_id,
                         batch.published_at_utc DESC, batch.job_id DESC
            ), bounded AS (
                SELECT * FROM latest_batches
                ORDER BY published_at_utc DESC, job_id DESC
                LIMIT 256
            )
            SELECT batch.asset_revision_id, batch.frame_count,
                   batch.embedding_dimensions, batch.sampling_policy_version,
                   batch.clip_model_id, batch.clip_model_sha256,
                   batch.clip_preprocessing_version, batch.caption_model_id,
                   batch.caption_prompt_version,
                   frame.ordinal, frame.timestamp_ms, frame.caption,
                   frame.embedding_le_f32
            FROM bounded batch
            LEFT JOIN video_frame_evidence_frames frame
                ON frame.job_id = batch.job_id
            ORDER BY batch.asset_revision_id, frame.ordinal;
            """;

        List<VideoSearchCandidate> results = [];
        Guid currentId = Guid.Empty;
        List<VideoSearchFrame> frames = [];
        int expectedFrames = 0, dimensions = 0;
        bool valid = true;
        string sampling = "", clipId = "", clipSha = "", preprocessing = "";
        string captionId = "", prompt = "";

        void Finish()
        {
            if (currentId != Guid.Empty && valid && frames.Count == expectedFrames)
                results.Add(new VideoSearchCandidate(currentId, sampling,
                    clipId, clipSha, preprocessing, captionId, prompt,
                    frames.ToArray()));
        }

        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                Guid id = reader.GetGuid(0);
                if (id != currentId)
                {
                    Finish();
                    currentId = id;
                    expectedFrames = reader.GetInt32(1);
                    dimensions = reader.GetInt32(2);
                    sampling = reader.GetString(3);
                    clipId = reader.GetString(4);
                    clipSha = reader.GetString(5);
                    preprocessing = reader.GetString(6);
                    captionId = reader.GetString(7);
                    prompt = reader.GetString(8);
                    frames = [];
                    valid = true;
                }
                if (reader.IsDBNull(9) || reader.GetInt32(9) != frames.Count ||
                    reader.IsDBNull(10) || reader.IsDBNull(11) ||
                    reader.IsDBNull(12))
                {
                    valid = false;
                    continue;
                }
                long timestamp = reader.GetInt64(10);
                string caption = reader.GetString(11);
                byte[] bytes = reader.GetFieldValue<byte[]>(12);
                if (timestamp < 0 || (frames.Count > 0 &&
                        timestamp <= frames[^1].TimestampMilliseconds) ||
                    string.IsNullOrWhiteSpace(caption) ||
                    bytes.Length != dimensions * sizeof(float))
                {
                    valid = false;
                    continue;
                }
                float[] vector = PostgresVideoFrameEvidenceRepository.DecodeVector(
                    bytes, dimensions);
                if (vector.Any(value => !float.IsFinite(value)))
                {
                    valid = false;
                    continue;
                }
                frames.Add(new VideoSearchFrame(timestamp, caption, vector));
            }
        }
        Finish();
        await tx.CommitAsync(token);
        return results;
    }
}
