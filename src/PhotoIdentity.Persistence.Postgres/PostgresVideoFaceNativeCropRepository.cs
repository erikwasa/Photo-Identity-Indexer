using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Private source authority, never serialized to HTTP. The source AND asset
/// locks remain held through staging, native decoding and the JPEG response.
/// Source-exclusion inserts need a source FK lock; new revisions need an asset
/// FK lock. Both must wait while this verified image is served.
/// </summary>
public sealed record VideoFaceNativeCropSource(
    Guid SourceId, string RootLocator, string SourceKey,
    string SourceSha256, long SourceBytes, Guid BatchId,
    int FrameOrdinal, long TimestampMs, int FrameWidth, int FrameHeight,
    double X, double Y, double Width, double Height);

public sealed class VideoFaceNativeCropSourceLease(
    VideoFaceNativeCropSource source, NpgsqlTransaction transaction,
    NpgsqlConnection connection) : IAsyncDisposable
{
    public VideoFaceNativeCropSource Source { get; } = source;

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}

/// <summary>
/// Only an immutable, selected FaceEvidence job receipt may authorize a
/// native source-backed crop. Legacy/unleased or experimental V06 proposals
/// are explicitly ineligible. This queries catalogue facts only; it neither
/// opens media nor performs recognition or publication.
/// </summary>
public sealed class PostgresVideoFaceNativeCropRepository(PostgresCatalogueDatabase database)
{
    public async Task<VideoFaceNativeCropSourceLease?> OpenAsync(
        AssetRevisionId revision, Guid observationId, string approvedToolSha256,
        CancellationToken token = default)
    {
        if (revision.IsEmpty || observationId == Guid.Empty ||
            approvedToolSha256 is not { Length: 64 } ||
            !approvedToolSha256.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return null;

        NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(token);
            // Two separate, ordered statements avoid stale snapshots after a
            // lock wait. Asset FK updates and inserts cannot invalidate the
            // latest revision between validation and response completion.
            await using (NpgsqlCommand lockSource = connection.CreateCommand())
            {
                lockSource.Transaction = transaction;
                lockSource.CommandText = """
                    SELECT source.id
                    FROM asset_revisions r
                    JOIN assets asset ON asset.id=r.asset_id
                    JOIN sources source ON source.id=asset.source_id
                    WHERE r.id=@revision AND source.kind='local-folder'
                    FOR UPDATE OF source;
                    """;
                lockSource.Parameters.AddWithValue("revision", revision.Value);
                if (await lockSource.ExecuteScalarAsync(token) is not Guid)
                {
                    await transaction.DisposeAsync();
                    await connection.DisposeAsync();
                    return null;
                }
            }
            await using (NpgsqlCommand lockAsset = connection.CreateCommand())
            {
                lockAsset.Transaction = transaction;
                lockAsset.CommandText = """
                    SELECT asset.id FROM asset_revisions r
                    JOIN assets asset ON asset.id=r.asset_id
                    WHERE r.id=@revision FOR UPDATE OF asset;
                    """;
                lockAsset.Parameters.AddWithValue("revision", revision.Value);
                if (await lockAsset.ExecuteScalarAsync(token) is not Guid)
                {
                    await transaction.DisposeAsync();
                    await connection.DisposeAsync();
                    return null;
                }
            }

            // Source, asset, batch, observation, job and model provenance all
            // come from the database. Never use client paths, timestamps, crop
            // coordinates or an unleased batch as the source of authority.
            await using NpgsqlCommand current = connection.CreateCommand();
            current.Transaction = transaction;
            current.CommandText = """
                SELECT source.id, source.root_locator, asset.source_key,
                       r.content_sha256, r.size_bytes, f.frame_ordinal,
                       f.timestamp_ms, f.frame_width, f.frame_height,
                       f.face_x, f.face_y, f.face_width, f.face_height,
                       metadata.duration_seconds, job.requested_frames, b.id
                FROM video_face_observations f
                JOIN video_face_evidence_batches b ON b.id=f.batch_id
                JOIN video_processing_jobs job ON job.id=b.publishing_job_id
                JOIN asset_revisions r ON r.id=b.asset_revision_id
                JOIN assets asset ON asset.id=r.asset_id
                JOIN sources source ON source.id=asset.source_id
                JOIN video_revision_metadata metadata
                  ON metadata.asset_revision_id=r.id
                WHERE f.id=@face AND b.asset_revision_id=@revision
                  AND b.source_sha256=r.content_sha256
                  AND b.producer_version='wi0155-native-face-evidence-v1'
                  AND b.detector_model_id=@detector
                  AND b.detector_model_hash=@detector_hash
                  AND b.sface_model_id=@embedder
                  AND b.sface_model_hash=@embedder_hash
                  AND b.alignment_protocol=@alignment
                  AND b.observation_count=(
                      SELECT count(*) FROM video_face_observations observation
                      WHERE observation.batch_id=b.id)
                  AND job.asset_revision_id=r.id AND job.status='succeeded'
                  AND job.derivative_kind=@kind
                  AND job.policy_version=@policy AND job.preset_version=@preset
                  AND job.tool_sha256=@tool
                  AND job.requested_frames BETWEEN 1 AND 4
                  AND f.frame_ordinal < job.requested_frames
                  AND r.media_kind='video' AND asset.media_kind='video'
                  AND source.kind='local-folder' AND asset.deleted_at_utc IS NULL
                  AND r.size_bytes BETWEEN 1 AND 268435456
                  AND metadata.inspection_status='supported'
                  AND metadata.color_transfer='bt709'
                  AND metadata.duration_seconds BETWEEN 0.2 AND 3600
                  AND metadata.coded_width BETWEEN 1 AND 3840
                  AND metadata.coded_height BETWEEN 1 AND 3840
                  AND r.id=(SELECT latest.id FROM asset_revisions latest
                            WHERE latest.asset_id=asset.id
                              AND latest.media_kind='video'
                            ORDER BY latest.observed_at_utc DESC, latest.id DESC
                            LIMIT 1)
                  AND NOT EXISTS (
                      SELECT 1 FROM source_copy_exclusions excluded
                      WHERE excluded.source_id=asset.source_id
                        AND excluded.source_key=asset.source_key);
                """;
            current.Parameters.AddWithValue("face", observationId);
            current.Parameters.AddWithValue("revision", revision.Value);
            current.Parameters.AddWithValue("detector", VideoFaceEvidenceLeaseIdentity.DetectorModelId);
            current.Parameters.AddWithValue("detector_hash", VideoFaceEvidenceLeaseIdentity.DetectorSha256);
            current.Parameters.AddWithValue("embedder", VideoFaceEvidenceLeaseIdentity.EmbedderModelId);
            current.Parameters.AddWithValue("embedder_hash", VideoFaceEvidenceLeaseIdentity.EmbedderSha256);
            current.Parameters.AddWithValue("alignment", VideoFaceEvidenceLeaseIdentity.AlignmentProtocol);
            current.Parameters.AddWithValue("kind", VideoFaceEvidenceLeaseIdentity.Kind);
            current.Parameters.AddWithValue("policy", VideoFaceEvidenceLeaseIdentity.PolicyVersion);
            current.Parameters.AddWithValue("preset", VideoFaceEvidenceLeaseIdentity.PresetVersion);
            current.Parameters.AddWithValue("tool", approvedToolSha256);

            VideoFaceNativeCropSource? sourceRow = null;
            decimal duration = 0;
            int requestedFrames = 0;
            await using (NpgsqlDataReader reader = await current.ExecuteReaderAsync(token))
            {
                if (await reader.ReadAsync(token))
                {
                    sourceRow = new VideoFaceNativeCropSource(
                        reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetInt64(4), reader.GetGuid(15),
                        reader.GetInt32(5), reader.GetInt64(6),
                        reader.GetInt32(7), reader.GetInt32(8),
                        reader.GetDouble(9), reader.GetDouble(10),
                        reader.GetDouble(11), reader.GetDouble(12));
                    duration = reader.GetDecimal(13);
                    requestedFrames = reader.GetInt32(14);
                }
            }

            // A forged timestamp/ordinal inside a leased batch may not select
            // arbitrary source frames. The requested ordinal and presentation
            // timestamp must match the exact deterministic producer plan.
            if (sourceRow is not null)
            {
                VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
                    new VideoFrameSamplingRequest(duration,
                        MaximumFrames: requestedFrames,
                        MaximumReservedDecodeMilliseconds: 12000,
                        ReservedDecodeMillisecondsPerFrame: 3000));
                if (plan.Samples.Count != requestedFrames ||
                    sourceRow.FrameOrdinal < 0 ||
                    sourceRow.FrameOrdinal >= plan.Samples.Count ||
                    plan.Samples[sourceRow.FrameOrdinal].TimestampMilliseconds !=
                        sourceRow.TimestampMs)
                    sourceRow = null;
            }

            if (sourceRow is null)
            {
                await transaction.DisposeAsync();
                await connection.DisposeAsync();
                return null;
            }
            return new VideoFaceNativeCropSourceLease(
                sourceRow, transaction, connection);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }
}
