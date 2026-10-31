using System.Buffers.Binary;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Explicit, unregistered publication boundary. Receives only fully computed
/// frame evidence and an existing FrameEvidence job lease; never opens media,
/// calls Ollama, hydrates OneDrive or starts a worker.
/// </summary>
public sealed class PostgresVideoFrameEvidenceRepository(PostgresCatalogueDatabase database)
{
    public async Task<bool> TryPublishAsync(
        VideoProcessingJobLease lease, VideoFrameSamplingPlan plan,
        VideoFrameEvidenceBatch batch, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(batch);
        if (lease.DerivativeKind != "FrameEvidence" ||
            lease.Id == Guid.Empty || lease.LeaseToken == Guid.Empty ||
            lease.RevisionId.IsEmpty ||
            lease.RequestedFrames != plan.Samples.Count ||
            lease.PolicyVersion != plan.PolicyVersion ||
            lease.PresetVersion != batch.Provenance.ExtractionProfileVersion ||
            batch.Provenance.RevisionId != lease.RevisionId ||
            !VideoDerivativeArtifactPath.IsHash(lease.ToolSha256))
            throw new ArgumentException("Frame evidence lease and sampling identity must match.");

        VideoFrameEvidenceValidation validation = VideoFrameEvidenceContract.Validate(
            plan, batch, batch.Provenance,
            CurrentRevision: true, Excluded: false, FullyCompleted: true);
        if (!validation.Accepted)
            throw new ArgumentException("Invalid frame evidence: " + validation.ReasonCode);

        // v38 deliberately accepts complete caption+embedding pairs only.
        // A future policy may support partial evidence, but cannot publish
        // a superficially successful mixed/missing batch by accident.
        if (batch.Items.Any(f => f.Caption is null || f.Embedding is null) ||
            batch.Provenance.CaptionModelId is null ||
            batch.Provenance.CaptionModelSha256 is null ||
            batch.Provenance.CaptionPromptVersion is null)
            throw new ArgumentException("Frame evidence requires caption and embedding for every frame.");
        int dimensions = batch.Items[0].Embedding!.Count;
        if (dimensions is < 1 or > VideoFrameEvidenceContract.MaximumEmbeddingDimensions)
            throw new ArgumentException("Frame embedding dimensionality is invalid.");

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);

        // Same lock order as published playable proxies: source -> control
        // -> job. Source FK locks serialize exclusion insertion and revision
        // registration with this exact commit, not just with a prior check.
        await using (NpgsqlCommand source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = """
                SELECT source.id FROM video_processing_jobs job
                JOIN asset_revisions revision ON revision.id=job.asset_revision_id
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                WHERE job.id=@job AND job.lease_token=@lease
                  AND job.status='running' AND source.kind='local-folder'
                FOR UPDATE OF source;
                """;
            source.Parameters.AddWithValue("job", lease.Id);
            source.Parameters.AddWithValue("lease", lease.LeaseToken);
            if (await source.ExecuteScalarAsync(token) is not Guid)
                return false;
        }

        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = transaction;
            control.CommandText = """
                SELECT generation FROM video_processing_control
                WHERE singleton_id=1 AND enabled AND NOT paused
                  AND generation=@generation
                  AND (selected_job_id IS NULL OR
                       (selected_job_id=@job AND authorization_expires_at_utc>now()))
                FOR UPDATE;
                """;
            control.Parameters.AddWithValue("generation", lease.ControlGeneration);
            control.Parameters.AddWithValue("job", lease.Id);
            if (await control.ExecuteScalarAsync(token) is not long)
                return false;
        }

        // Photo work takes priority even when a video lease exists.
        if ((await PostgresVideoPhotoPriorityProbe
                .ReadAsync(connection, transaction, token)).PhotoWorkActive)
            return false;

        // The job transition and every evidence row are in this transaction;
        // no query can observe success without the complete batch.
        await using (NpgsqlCommand complete = connection.CreateCommand())
        {
            complete.Transaction = transaction;
            complete.CommandText = """
                UPDATE video_processing_jobs job
                SET status='succeeded', progress_percent=100,
                    completed_at_utc=now(), updated_at_utc=now(),
                    lease_token=NULL, leased_until_utc=NULL, failure_code=NULL
                WHERE job.id=@job AND job.lease_token=@lease
                  AND job.asset_revision_id=@revision
                  AND job.derivative_kind='FrameEvidence'
                  AND job.policy_version=@policy
                  AND job.preset_version=@preset
                  AND job.tool_sha256=@tool
                  AND job.source_bytes=@source_bytes
                  AND job.reserved_temporary_bytes=@temporary_bytes
                  AND job.requested_frames=@frames
                  AND job.control_generation=@generation
                  AND job.status='running' AND job.leased_until_utc>now()
                  AND job.cancellation_requested_at_utc IS NULL
                  AND EXISTS (
                    SELECT 1 FROM asset_revisions revision
                    JOIN assets asset ON asset.id=revision.asset_id
                    JOIN sources source ON source.id=asset.source_id
                    JOIN video_revision_metadata metadata
                      ON metadata.asset_revision_id=revision.id
                    WHERE revision.id=job.asset_revision_id
                      AND revision.size_bytes=job.source_bytes
                      AND revision.media_kind='video' AND asset.media_kind='video'
                      AND asset.deleted_at_utc IS NULL
                      AND source.kind='local-folder'
                      AND metadata.inspection_status='supported'
                      AND metadata.color_transfer='bt709'
                      AND revision.id=(
                        SELECT latest.id FROM asset_revisions latest
                        WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                        ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                      AND NOT EXISTS (
                        SELECT 1 FROM source_copy_exclusions excluded
                        WHERE excluded.source_id=asset.source_id
                          AND excluded.source_key=asset.source_key))
                  AND NOT EXISTS (
                    SELECT 1 FROM video_frame_evidence_batches existing
                    WHERE existing.job_id=job.id)
                RETURNING job.id;
                """;
            complete.Parameters.AddWithValue("job", lease.Id);
            complete.Parameters.AddWithValue("lease", lease.LeaseToken);
            complete.Parameters.AddWithValue("revision", lease.RevisionId.Value);
            complete.Parameters.AddWithValue("policy", lease.PolicyVersion);
            complete.Parameters.AddWithValue("preset", lease.PresetVersion);
            complete.Parameters.AddWithValue("tool", lease.ToolSha256);
            complete.Parameters.AddWithValue("source_bytes", lease.SourceBytes);
            complete.Parameters.AddWithValue("temporary_bytes", lease.TemporaryBytes);
            complete.Parameters.AddWithValue("frames", lease.RequestedFrames);
            complete.Parameters.AddWithValue("generation", lease.ControlGeneration);
            if (await complete.ExecuteScalarAsync(token) is not Guid)
                return false;
        }

        VideoFrameEvidenceProvenance p = batch.Provenance;
        await using (NpgsqlCommand header = connection.CreateCommand())
        {
            header.Transaction = transaction;
            header.CommandText = """
                INSERT INTO video_frame_evidence_batches (
                    job_id, asset_revision_id, sampling_policy_version,
                    extraction_profile_version, clip_model_id,
                    clip_model_sha256, clip_preprocessing_version,
                    caption_model_id, caption_model_sha256,
                    caption_prompt_version, frame_count, embedding_dimensions)
                VALUES (@job,@revision,@sampling,@extraction,@clip_model,
                    @clip_sha,@preprocessing,@caption_model,@caption_sha,
                    @prompt,@count,@dimensions);
                """;
            header.Parameters.AddWithValue("job", lease.Id);
            header.Parameters.AddWithValue("revision", p.RevisionId.Value);
            header.Parameters.AddWithValue("sampling", p.SamplingPolicyVersion);
            header.Parameters.AddWithValue("extraction", p.ExtractionProfileVersion);
            header.Parameters.AddWithValue("clip_model", p.ModelId);
            header.Parameters.AddWithValue("clip_sha", p.ModelSha256);
            header.Parameters.AddWithValue("preprocessing", p.PreprocessingVersion);
            header.Parameters.AddWithValue("caption_model", p.CaptionModelId!);
            header.Parameters.AddWithValue("caption_sha", p.CaptionModelSha256!);
            header.Parameters.AddWithValue("prompt", p.CaptionPromptVersion!);
            header.Parameters.AddWithValue("count", batch.Items.Count);
            header.Parameters.AddWithValue("dimensions", dimensions);
            await header.ExecuteNonQueryAsync(token);
        }

        for (int ordinal = 0; ordinal < batch.Items.Count; ordinal++)
        {
            VideoFrameEvidenceItem item = batch.Items[ordinal];
            await using NpgsqlCommand frame = connection.CreateCommand();
            frame.Transaction = transaction;
            frame.CommandText = """
                INSERT INTO video_frame_evidence_frames (
                    job_id, ordinal, timestamp_ms, scene_index, caption,
                    embedding_le_f32)
                VALUES (@job,@ordinal,@timestamp,@scene,@caption,@embedding);
                """;
            frame.Parameters.AddWithValue("job", lease.Id);
            frame.Parameters.AddWithValue("ordinal", ordinal);
            frame.Parameters.AddWithValue("timestamp", item.TimestampMilliseconds);
            frame.Parameters.AddWithValue("scene", (object?)item.SceneIndex ?? DBNull.Value);
            frame.Parameters.AddWithValue("caption", item.Caption!);
            frame.Parameters.AddWithValue("embedding", EncodeVector(item.Embedding!));
            await frame.ExecuteNonQueryAsync(token);
        }

        await transaction.CommitAsync(token);
        return true;
    }

    /// <summary>
    /// A private currentness-gated read, not an API or a search index.
    /// The batch is visible only when its job is succeeded, frame count
    /// complete, revision latest, source unexcluded and inspection supported.
    /// </summary>
    public async Task<VideoFrameEvidenceBatch?> GetCurrentAsync(
        Guid jobId, CancellationToken token = default)
    {
        if (jobId == Guid.Empty) return null;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.asset_revision_id, b.sampling_policy_version,
                b.extraction_profile_version, b.clip_model_id,
                b.clip_model_sha256, b.clip_preprocessing_version,
                b.caption_model_id, b.caption_model_sha256,
                b.caption_prompt_version, b.frame_count, b.embedding_dimensions
            FROM video_frame_evidence_batches b
            JOIN video_processing_jobs job ON job.id=b.job_id
            JOIN asset_revisions revision ON revision.id=b.asset_revision_id
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN sources source ON source.id=asset.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
            WHERE b.job_id=@job AND job.status='succeeded'
              AND job.derivative_kind='FrameEvidence'
              AND job.asset_revision_id=revision.id
              AND source.kind='local-folder'
              AND revision.media_kind='video' AND asset.media_kind='video'
              AND asset.deleted_at_utc IS NULL
              AND metadata.inspection_status='supported'
              AND metadata.color_transfer='bt709'
              AND revision.id=(
                  SELECT latest.id FROM asset_revisions latest
                  WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                  ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key)
              AND b.frame_count=(
                  SELECT COUNT(*) FROM video_frame_evidence_frames frames
                  WHERE frames.job_id=b.job_id)
              AND NOT EXISTS (
                  SELECT 1 FROM video_frame_evidence_frames frames
                  WHERE frames.job_id=b.job_id
                    AND octet_length(frames.embedding_le_f32) <> b.embedding_dimensions*4);
            """;
        command.Parameters.AddWithValue("job", jobId);
        VideoFrameEvidenceProvenance? provenance = null;
        int expectedCount = 0, dimensions = 0;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                provenance = new VideoFrameEvidenceProvenance(
                    AssetRevisionId.From(reader.GetGuid(0)),
                    reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5))
                {
                    CaptionModelId = reader.GetString(6),
                    CaptionModelSha256 = reader.GetString(7),
                    CaptionPromptVersion = reader.GetString(8)
                };
                expectedCount = reader.GetInt32(9);
                dimensions = reader.GetInt32(10);
            }
        }
        if (provenance is null) return null;
        List<VideoFrameEvidenceItem> items = [];
        await using (NpgsqlCommand rows = connection.CreateCommand())
        {
            rows.Transaction = transaction;
            rows.CommandText = """
                SELECT ordinal, timestamp_ms, scene_index, caption, embedding_le_f32
                FROM video_frame_evidence_frames
                WHERE job_id=@job
                ORDER BY ordinal;
                """;
            rows.Parameters.AddWithValue("job", jobId);
            await using NpgsqlDataReader reader = await rows.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (reader.GetInt32(0) != items.Count) return null;
                byte[] raw = reader.GetFieldValue<byte[]>(4);
                if (raw.Length != dimensions * 4) return null;
                float[] vector = DecodeVector(raw, dimensions);
                if (vector.Any(value => !float.IsFinite(value))) return null;
                items.Add(new VideoFrameEvidenceItem(
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.GetString(3), vector));
            }
        }
        if (items.Count != expectedCount) return null;
        await transaction.CommitAsync(token);
        return new VideoFrameEvidenceBatch(provenance, items);
    }

    public static byte[] EncodeVector(IReadOnlyList<float> vector)
    {
        if (vector.Count is < 1 or > VideoFrameEvidenceContract.MaximumEmbeddingDimensions ||
            vector.Any(v => !float.IsFinite(v)))
            throw new ArgumentException("Frame embedding must contain only bounded finite values.");
        byte[] bytes = new byte[checked(vector.Count * sizeof(float))];
        for (int i = 0; i < vector.Count; i++)
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(vector[i]));
        return bytes;
    }

    public static float[] DecodeVector(ReadOnlySpan<byte> bytes, int dimensions)
    {
        if (dimensions is < 1 or > VideoFrameEvidenceContract.MaximumEmbeddingDimensions ||
            bytes.Length != dimensions * sizeof(float))
            throw new ArgumentException("Stored frame vector dimensions are inconsistent.");
        float[] vector = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
            vector[i] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.Slice(i * sizeof(float), sizeof(float))));
        return vector;
    }
}
