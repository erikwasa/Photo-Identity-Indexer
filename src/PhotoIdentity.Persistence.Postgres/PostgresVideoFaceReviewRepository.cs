using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>Internal producer contract: never accepts media paths, raw pixels or embeddings.</summary>
public sealed record VideoFaceRankedPerson(Guid PersonId, double Similarity);
public sealed record VideoFaceProposedObservation(
    int FrameOrdinal, long TimestampMs, int FaceOrdinal,
    int FrameWidth, int FrameHeight,
    double X, double Y, double Width, double Height,
    IReadOnlyList<VideoFaceRankedPerson> Suggestions);
public sealed record VideoFaceEvidencePublication(
    AssetRevisionId RevisionId, string SourceSha256, string ProducerVersion,
    string DetectorModelId, string DetectorModelHash,
    string SFaceModelId, string SFaceModelHash, string AlignmentProtocol,
    IReadOnlyList<VideoFaceProposedObservation> Observations);

public sealed record VideoFaceReviewSuggestion(
    string PersonId, string DisplayName, int Rank, double Similarity, bool Selectable);
public sealed record VideoFaceReviewObservation(
    string Id, int FrameOrdinal, long TimestampMs, int FaceOrdinal,
    int FrameWidth, int FrameHeight, double X, double Y, double Width, double Height,
    IReadOnlyList<VideoFaceReviewSuggestion> Suggestions,
    string? Decision, string? ReviewedPersonId, string? ReviewedPersonName);
public sealed record VideoFaceReviewBatch(
    string RevisionId, string BatchId, string ProducerVersion,
    string DetectorModelId, string SFaceModelId, string AlignmentProtocol,
    IReadOnlyList<VideoFaceReviewObservation> Observations);

/// <summary>
/// Revision-fenced video-face evidence and human review actions.
/// Publishing is INTERNAL ONLY. HTTP exposes read and human decision routes,
/// never face-evidence injection, inference or automatic video/person tagging.
/// </summary>
public sealed class PostgresVideoFaceReviewRepository(
    PostgresCatalogueDatabase database, TimeProvider timeProvider)
{
    private const string CurrentVideo = """
        r.media_kind='video' AND a.media_kind='video'
        AND a.deleted_at_utc IS NULL AND s.kind='local-folder'
        AND metadata.inspection_status='supported'
        AND r.id=(SELECT latest.id FROM asset_revisions latest
                  WHERE latest.asset_id=a.id AND latest.media_kind='video'
                  ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
        AND NOT EXISTS (SELECT 1 FROM source_copy_exclusions e
                        WHERE e.source_id=a.source_id AND e.source_key=a.source_key)
        """;

    private static bool Sha(string? value) =>
        value is { Length: 64 } && value.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void ValidatePublication(VideoFaceEvidencePublication proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.RevisionId.IsEmpty || !Sha(proposal.SourceSha256) ||
            !Sha(proposal.DetectorModelHash) || !Sha(proposal.SFaceModelHash) ||
            !Bounded(proposal.ProducerVersion) ||
            proposal.DetectorModelId != "yunet-2023mar-fp32" ||
            proposal.SFaceModelId != "sface-2021dec-fp32" ||
            proposal.AlignmentProtocol != "sface-five-point-v1" ||
            proposal.Observations is not { Count: >= 1 and <= 256 })
            throw new ArgumentException("Invalid bounded video-face publication provenance.");

        var observed = new HashSet<(int, int)>();
        foreach (VideoFaceProposedObservation face in proposal.Observations)
        {
            if (face.FrameOrdinal is < 0 or > 127 ||
                face.TimestampMs is < 0 or > 86400000 ||
                face.FaceOrdinal is < 0 or > 31 ||
                face.FrameWidth is < 1 or > 3840 || face.FrameHeight is < 1 or > 3840 ||
                !double.IsFinite(face.X) || !double.IsFinite(face.Y) ||
                !double.IsFinite(face.Width) || !double.IsFinite(face.Height) ||
                face.X < 0 || face.Y < 0 || face.Width <= 0 || face.Height <= 0 ||
                face.X + face.Width > 1.00001 || face.Y + face.Height > 1.00001 ||
                !observed.Add((face.FrameOrdinal, face.FaceOrdinal)) ||
                face.Suggestions is not { Count: >= 0 and <= 3 })
                throw new ArgumentException("Invalid or duplicate video face observation.");
            var people = new HashSet<Guid>();
            double previous = double.PositiveInfinity;
            foreach (VideoFaceRankedPerson lead in face.Suggestions)
            {
                if (lead.PersonId == Guid.Empty || !people.Add(lead.PersonId) ||
                    !double.IsFinite(lead.Similarity) ||
                    lead.Similarity is < -1 or > 1 || lead.Similarity > previous)
                    throw new ArgumentException("Invalid per-face canonical person ranking.");
                previous = lead.Similarity;
            }
        }
    }

    private static bool Bounded(string? value) =>
        value is { Length: >= 1 and <= 100 } && value == value.Trim();

    private static async Task<string?> LockCurrentVideoAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, AssetRevisionId revision,
        CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $"""
            SELECT r.content_sha256 FROM asset_revisions r
            JOIN assets a ON a.id=r.asset_id
            JOIN sources s ON s.id=a.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=r.id
            WHERE r.id=@revision AND {CurrentVideo}
            FOR UPDATE OF s;
            """;
        command.Parameters.AddWithValue("revision", revision.Value);
        return await command.ExecuteScalarAsync(token) as string;
    }

    /// <summary>
    /// Legacy internal v42 publication contract retained for controlled
    /// already-derived evidence. It cannot create a durable job receipt.
    /// FaceEvidence workers MUST call the lease-bound method instead.
    /// </summary>
    public async Task<Guid> PublishAsync(
        VideoFaceEvidencePublication proposal, CancellationToken token = default) =>
        (await PublishCoreAsync(proposal, null, token))
            ?? throw new InvalidOperationException("Unleased face publication unavailable.");

    /// <summary>
    /// All-or-nothing evidence and job success. Never called from an HTTP
    /// endpoint or hosted service; the separately selected producer/adapter
    /// must hold a live operator lease across extraction and matching.
    /// </summary>
    public Task<Guid?> TryPublishLeasedAsync(
        VideoProcessingJobLease lease, VideoFaceEvidencePublication proposal,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ValidatePublication(proposal);
        if (!VideoFaceEvidenceLeaseIdentity.Matches(
                lease.DerivativeKind, lease.PolicyVersion,
                lease.PresetVersion, lease.RequestedFrames) ||
            lease.Id == Guid.Empty || lease.LeaseToken == Guid.Empty ||
            lease.RevisionId != proposal.RevisionId ||
            !Sha(lease.ToolSha256) ||
            lease.SourceBytes <= 0 || lease.TemporaryBytes < lease.SourceBytes ||
            lease.ControlGeneration < 0 ||
            proposal.Observations.Any(face => face.FrameOrdinal >= lease.RequestedFrames))
            throw new ArgumentException("Exact active FaceEvidence job lease and frame scope required.");
        return PublishCoreAsync(proposal, lease, token);
    }

    private async Task<Guid?> PublishCoreAsync(
        VideoFaceEvidencePublication proposal, VideoProcessingJobLease? lease,
        CancellationToken token)
    {
        ValidatePublication(proposal);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(token);
        string? actualSha = await LockCurrentVideoAsync(
            connection, tx, proposal.RevisionId, token);
        if (actualSha is null)
            throw new KeyNotFoundException("Current included supported video revision unavailable.");
        if (!string.Equals(actualSha, proposal.SourceSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The source revision digest does not match.");

        if (lease is not null)
        {
            // Required lock order: source -> operator control -> job.
            // No broad video enable switch may authorize face inference:
            // a nonexpired specifically selected FaceEvidence job is mandatory.
            await using NpgsqlCommand scope = connection.CreateCommand();
            scope.Transaction = tx;
            scope.CommandText = """
                SELECT generation FROM video_processing_control
                WHERE singleton_id=1 AND enabled AND NOT paused
                  AND generation=@generation AND selected_job_id=@job
                  AND authorization_expires_at_utc>clock_timestamp()
                FOR UPDATE;
                """;
            scope.Parameters.AddWithValue("generation", lease.ControlGeneration);
            scope.Parameters.AddWithValue("job", lease.Id);
            if (await scope.ExecuteScalarAsync(token) is not long ||
                (await PostgresVideoPhotoPriorityProbe.ReadAsync(
                    connection, tx, token)).PhotoWorkActive)
                return null;
        }

        // Retrying identical evidence after an unknown commit result is safe.
        // A different publication cannot replace reviewed observation IDs.
        string payloadSha256 = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                revision = proposal.RevisionId.Value,
                proposal.SourceSha256, proposal.ProducerVersion,
                proposal.DetectorModelId, proposal.DetectorModelHash,
                proposal.SFaceModelId, proposal.SFaceModelHash,
                proposal.AlignmentProtocol,
                observations = proposal.Observations
            })))).ToLowerInvariant();
        await using (NpgsqlCommand existing = connection.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = """
                SELECT id, payload_sha256, publishing_job_id FROM video_face_evidence_batches
                WHERE asset_revision_id=@revision;
                """;
            existing.Parameters.AddWithValue("revision", proposal.RevisionId.Value);
            Guid? oldBatch = null;
            Guid? oldJob = null;
            string? oldHash = null;
            await using (NpgsqlDataReader reader = await existing.ExecuteReaderAsync(token))
            {
                if (await reader.ReadAsync(token))
                {
                    oldBatch = reader.GetGuid(0);
                    oldHash = reader.GetString(1);
                    oldJob = reader.IsDBNull(2) ? null : reader.GetGuid(2);
                }
            }
            if (oldBatch.HasValue)
            {
                if (!string.Equals(oldHash, payloadSha256, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Conflicting face evidence already exists for this video revision.");
                if (lease is not null)
                {
                    // An older unleased or other-job batch cannot authorize
                    // this job. Even identical payloads must not claim it.
                    if (oldJob != lease.Id ||
                        !await TryCompleteLeasedJobAsync(connection, tx, lease,
                            proposal.SourceSha256, token))
                        return null;
                }
                await tx.CommitAsync(token);
                return oldBatch.Value;
            }
        }
        Guid batchId = Guid.NewGuid();
        await using (NpgsqlCommand batch = connection.CreateCommand())
        {
            batch.Transaction = tx;
            batch.CommandText = """
                INSERT INTO video_face_evidence_batches
                  (id, asset_revision_id, source_sha256, payload_sha256, producer_version,
                   detector_model_id, detector_model_hash, sface_model_id,
                   sface_model_hash, alignment_protocol, observation_count, created_at_utc,
                   publishing_job_id)
                VALUES (@id, @revision, @source, @payload, @producer, @detector, @detector_hash,
                        @sface, @sface_hash, @alignment, @count, @at, @job);
                """;
            batch.Parameters.AddWithValue("id", batchId);
            batch.Parameters.AddWithValue("revision", proposal.RevisionId.Value);
            batch.Parameters.AddWithValue("source", proposal.SourceSha256);
            batch.Parameters.AddWithValue("payload", payloadSha256);
            batch.Parameters.AddWithValue("producer", proposal.ProducerVersion);
            batch.Parameters.AddWithValue("detector", proposal.DetectorModelId);
            batch.Parameters.AddWithValue("detector_hash", proposal.DetectorModelHash);
            batch.Parameters.AddWithValue("sface", proposal.SFaceModelId);
            batch.Parameters.AddWithValue("sface_hash", proposal.SFaceModelHash);
            batch.Parameters.AddWithValue("alignment", proposal.AlignmentProtocol);
            batch.Parameters.AddWithValue("count", proposal.Observations.Count);
            batch.Parameters.AddWithValue("at", timeProvider.GetUtcNow());
            batch.Parameters.Add(new Npgsql.NpgsqlParameter("job",
                NpgsqlTypes.NpgsqlDbType.Uuid)
            { Value = lease is null ? DBNull.Value : lease.Id });
            await batch.ExecuteNonQueryAsync(token);
        }

        foreach (VideoFaceProposedObservation face in proposal.Observations)
        {
            Guid faceId = Guid.NewGuid();
            await using (NpgsqlCommand insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO video_face_observations
                      (id, batch_id, frame_ordinal, timestamp_ms, face_ordinal,
                       frame_width, frame_height, face_x, face_y, face_width, face_height)
                    VALUES (@id, @batch, @frame, @timestamp, @face, @fw, @fh, @x, @y, @w, @h);
                    """;
                insert.Parameters.AddWithValue("id", faceId);
                insert.Parameters.AddWithValue("batch", batchId);
                insert.Parameters.AddWithValue("frame", face.FrameOrdinal);
                insert.Parameters.AddWithValue("timestamp", face.TimestampMs);
                insert.Parameters.AddWithValue("face", face.FaceOrdinal);
                insert.Parameters.AddWithValue("fw", face.FrameWidth);
                insert.Parameters.AddWithValue("fh", face.FrameHeight);
                insert.Parameters.AddWithValue("x", face.X);
                insert.Parameters.AddWithValue("y", face.Y);
                insert.Parameters.AddWithValue("w", face.Width);
                insert.Parameters.AddWithValue("h", face.Height);
                await insert.ExecuteNonQueryAsync(token);
            }
            for (int rank = 0; rank < face.Suggestions.Count; rank++)
            {
                VideoFaceRankedPerson lead = face.Suggestions[rank];
                await using NpgsqlCommand insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO video_face_suggestions (observation_id, rank, person_id, similarity)
                    SELECT @face, @rank, p.id, @score
                    FROM people p WHERE p.id=@person AND p.merged_into_person_id IS NULL
                    AND p.display_name IS NOT NULL AND btrim(p.display_name)<>'';
                    """;
                insert.Parameters.AddWithValue("face", faceId);
                insert.Parameters.AddWithValue("rank", rank + 1);
                insert.Parameters.AddWithValue("person", lead.PersonId);
                insert.Parameters.AddWithValue("score", lead.Similarity);
                if (await insert.ExecuteNonQueryAsync(token) != 1)
                    throw new InvalidOperationException("Suggested canonical person has changed.");
            }
        }
        if (lease is not null &&
            !await TryCompleteLeasedJobAsync(connection, tx, lease,
                proposal.SourceSha256, token))
            return null; // Dispose transaction => roll back all observations.
        await tx.CommitAsync(token);
        return batchId;
    }

    /// <summary>
    /// Must execute in the SAME transaction as the batch and after the
    /// source/control locks. A failed update rolls back the entire batch.
    /// PostgreSQL wall-clock checks are intentionally not transaction-start
    /// now(): grants and leases must still be live at the final commit gate.
    /// </summary>
    private static async Task<bool> TryCompleteLeasedJobAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        VideoProcessingJobLease lease, string sourceSha, CancellationToken token)
    {
        if ((await PostgresVideoPhotoPriorityProbe.ReadAsync(
                connection, tx, token)).PhotoWorkActive)
            return false;

        await using NpgsqlCommand finish = connection.CreateCommand();
        finish.Transaction = tx;
        finish.CommandText = """
            UPDATE video_processing_jobs job
            SET status='succeeded', progress_percent=100,
                completed_at_utc=clock_timestamp(), updated_at_utc=clock_timestamp(),
                lease_token=NULL, leased_until_utc=NULL, failure_code=NULL
            WHERE job.id=@job AND job.lease_token=@lease
              AND job.asset_revision_id=@revision
              AND job.derivative_kind='FaceEvidence'
              AND job.policy_version=@policy AND job.preset_version=@preset
              AND job.tool_sha256=@tool AND job.source_bytes=@source_bytes
              AND job.reserved_temporary_bytes=@temporary_bytes
              AND job.requested_frames=@frames
              AND job.control_generation=@generation
              AND job.status='running' AND job.leased_until_utc>clock_timestamp()
              AND job.cancellation_requested_at_utc IS NULL
              AND EXISTS (
                  SELECT 1 FROM video_processing_control control
                  WHERE control.singleton_id=1 AND control.enabled AND NOT control.paused
                    AND control.generation=@generation
                    AND control.selected_job_id=@job
                    AND control.authorization_expires_at_utc>clock_timestamp())
              AND EXISTS (
                  SELECT 1 FROM asset_revisions revision
                  JOIN assets asset ON asset.id=revision.asset_id
                  JOIN sources source ON source.id=asset.source_id
                  JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
                  WHERE revision.id=job.asset_revision_id
                    AND revision.content_sha256=@sha
                    AND revision.size_bytes=job.source_bytes
                    AND revision.media_kind='video' AND asset.media_kind='video'
                    AND asset.deleted_at_utc IS NULL AND source.kind='local-folder'
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
            RETURNING job.id;
            """;
        finish.Parameters.AddWithValue("job", lease.Id);
        finish.Parameters.AddWithValue("lease", lease.LeaseToken);
        finish.Parameters.AddWithValue("revision", lease.RevisionId.Value);
        finish.Parameters.AddWithValue("policy", lease.PolicyVersion);
        finish.Parameters.AddWithValue("preset", lease.PresetVersion);
        finish.Parameters.AddWithValue("tool", lease.ToolSha256);
        finish.Parameters.AddWithValue("source_bytes", lease.SourceBytes);
        finish.Parameters.AddWithValue("temporary_bytes", lease.TemporaryBytes);
        finish.Parameters.AddWithValue("frames", lease.RequestedFrames);
        finish.Parameters.AddWithValue("generation", lease.ControlGeneration);
        finish.Parameters.AddWithValue("sha", sourceSha);
        return await finish.ExecuteScalarAsync(token) is Guid;
    }

    /// <summary>
    /// A durable, current receipt of exactly this job, not merely a
    /// matching video revision or an unleased legacy face batch.
    /// </summary>
    public async Task<bool> HasCurrentJobReceiptAsync(
        Guid jobId, AssetRevisionId revisionId,
        CancellationToken token = default)
    {
        if (jobId == Guid.Empty || revisionId.IsEmpty) return false;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand receipt = connection.CreateCommand();
        receipt.CommandText = $"""
            SELECT EXISTS(
                SELECT 1 FROM video_face_evidence_batches b
                JOIN video_processing_jobs job ON job.id=b.publishing_job_id
                JOIN asset_revisions r ON r.id=b.asset_revision_id
                JOIN assets a ON a.id=r.asset_id
                JOIN sources s ON s.id=a.source_id
                JOIN video_revision_metadata metadata ON metadata.asset_revision_id=r.id
                WHERE b.publishing_job_id=@job AND b.asset_revision_id=@revision
                  AND job.asset_revision_id=r.id
                  AND job.derivative_kind='FaceEvidence' AND job.status='succeeded'
                  AND b.source_sha256=r.content_sha256
                  AND metadata.color_transfer='bt709' AND {CurrentVideo}
                  AND b.observation_count=(
                      SELECT COUNT(*) FROM video_face_observations face
                      WHERE face.batch_id=b.id)
            );
            """;
        receipt.Parameters.AddWithValue("job", jobId);
        receipt.Parameters.AddWithValue("revision", revisionId.Value);
        return await receipt.ExecuteScalarAsync(token) is true;
    }

    public async Task<VideoFaceReviewBatch?> GetCurrentAsync(
        AssetRevisionId revision, CancellationToken token = default)
    {
        if (revision.IsEmpty) return null;
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, token);
        await using NpgsqlCommand batch = connection.CreateCommand();
        batch.Transaction = tx;
        batch.CommandText = $"""
            SELECT b.id, b.producer_version, b.detector_model_id,
                   b.sface_model_id, b.alignment_protocol
            FROM video_face_evidence_batches b
            JOIN asset_revisions r ON r.id=b.asset_revision_id
            JOIN assets a ON a.id=r.asset_id
            JOIN sources s ON s.id=a.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=r.id
            WHERE b.asset_revision_id=@revision
              AND b.source_sha256=r.content_sha256 AND {CurrentVideo}
              AND b.observation_count=(SELECT count(*) FROM video_face_observations
                                       WHERE batch_id=b.id);
            """;
        batch.Parameters.AddWithValue("revision", revision.Value);
        Guid batchId;
        string producer, detector, sface, alignment;
        await using (NpgsqlDataReader reader = await batch.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return null;
            batchId = reader.GetGuid(0);
            producer = reader.GetString(1);
            detector = reader.GetString(2);
            sface = reader.GetString(3);
            alignment = reader.GetString(4);
        }

        var faces = new List<VideoFaceReviewObservation>();
        await using (NpgsqlCommand query = connection.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = """
                SELECT f.id, f.frame_ordinal, f.timestamp_ms, f.face_ordinal,
                       f.frame_width, f.frame_height, f.face_x, f.face_y,
                       f.face_width, f.face_height,
                       action.decision, action.person_id, reviewed.display_name
                FROM video_face_observations f
                LEFT JOIN LATERAL (
                    SELECT decision, person_id FROM video_face_review_actions
                    WHERE observation_id=f.id ORDER BY id DESC LIMIT 1
                ) action ON TRUE
                LEFT JOIN people reviewed ON reviewed.id=action.person_id
                WHERE f.batch_id=@batch ORDER BY f.frame_ordinal, f.face_ordinal;
                """;
            query.Parameters.AddWithValue("batch", batchId);
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                faces.Add(new VideoFaceReviewObservation(
                    reader.GetGuid(0).ToString("D"), reader.GetInt32(1),
                    reader.GetInt64(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.GetInt32(5), reader.GetDouble(6), reader.GetDouble(7),
                    reader.GetDouble(8), reader.GetDouble(9), [],
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetGuid(11).ToString("D"),
                    reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        var suggestions = new Dictionary<string, List<VideoFaceReviewSuggestion>>(StringComparer.Ordinal);
        await using (NpgsqlCommand query = connection.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = """
                SELECT suggestion.observation_id, suggestion.person_id,
                       COALESCE(person.display_name,'Unnamed person'),
                       suggestion.rank, suggestion.similarity,
                       (person.merged_into_person_id IS NULL
                        AND person.display_name IS NOT NULL
                        AND btrim(person.display_name)<>'')
                FROM video_face_suggestions suggestion
                JOIN video_face_observations face ON face.id=suggestion.observation_id
                JOIN people person ON person.id=suggestion.person_id
                WHERE face.batch_id=@batch
                ORDER BY suggestion.observation_id, suggestion.rank;
                """;
            query.Parameters.AddWithValue("batch", batchId);
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                string id = reader.GetGuid(0).ToString("D");
                if (!suggestions.TryGetValue(id, out List<VideoFaceReviewSuggestion>? list))
                {
                    list = [];
                    suggestions.Add(id, list);
                }
                list.Add(new VideoFaceReviewSuggestion(
                    reader.GetGuid(1).ToString("D"), reader.GetString(2),
                    reader.GetInt32(3), reader.GetDouble(4), reader.GetBoolean(5)));
            }
        }

        await tx.CommitAsync(token);
        return new VideoFaceReviewBatch(revision.ToString(), batchId.ToString("D"),
            producer, detector, sface, alignment,
            faces.Select(face => face with {
                Suggestions = suggestions.TryGetValue(face.Id, out var list) ? list : []
            }).ToArray());
    }

    /// <returns>True for a new audit action, false for a repeated identical decision.</returns>
    public async Task<bool> RecordDecisionAsync(
        AssetRevisionId revision, Guid observationId, string decision,
        Guid? personId, string actor, CancellationToken token = default)
    {
        if (revision.IsEmpty || observationId == Guid.Empty ||
            decision is not ("known" or "unknown" or "not-face" or "cannot-tell" or "clear") ||
            (decision == "known") != personId.HasValue ||
            personId == Guid.Empty || !BoundedActor(actor))
            throw new ArgumentException("Invalid human video-face review decision.");

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(token);
        string? actual = await LockCurrentVideoAsync(connection, tx, revision, token);
        if (actual is null)
            throw new KeyNotFoundException("The video revision is unavailable.");
        await using (NpgsqlCommand current = connection.CreateCommand())
        {
            current.Transaction = tx;
            current.CommandText = """
                SELECT 1 FROM video_face_observations f
                JOIN video_face_evidence_batches b ON b.id=f.batch_id
                WHERE f.id=@face AND b.asset_revision_id=@revision
                  AND b.source_sha256=@sha FOR UPDATE OF f;
                """;
            current.Parameters.AddWithValue("face", observationId);
            current.Parameters.AddWithValue("revision", revision.Value);
            current.Parameters.AddWithValue("sha", actual);
            if (await current.ExecuteScalarAsync(token) is null)
                throw new KeyNotFoundException("Current video face evidence unavailable.");
        }
        if (personId.HasValue)
        {
            await using NpgsqlCommand person = connection.CreateCommand();
            person.Transaction = tx;
            person.CommandText = """
                SELECT 1 FROM people WHERE id=@person
                  AND merged_into_person_id IS NULL AND display_name IS NOT NULL
                  AND btrim(display_name)<>'' FOR UPDATE;
                """;
            person.Parameters.AddWithValue("person", personId.Value);
            if (await person.ExecuteScalarAsync(token) is null)
                throw new InvalidOperationException("The person is no longer canonical.");
        }

        bool unchanged = false;
        await using (NpgsqlCommand previous = connection.CreateCommand())
        {
            previous.Transaction = tx;
            previous.CommandText = """
                SELECT decision, person_id FROM video_face_review_actions
                WHERE observation_id=@face ORDER BY id DESC LIMIT 1;
                """;
            previous.Parameters.AddWithValue("face", observationId);
            // Npgsql requires the reader to be disposed before committing.
            await using (NpgsqlDataReader reader = await previous.ExecuteReaderAsync(token))
            {
                unchanged = await reader.ReadAsync(token) &&
                    reader.GetString(0) == decision &&
                    (reader.IsDBNull(1) ? null : reader.GetGuid(1)) == personId;
            }
        }
        if (unchanged)
        {
            await tx.CommitAsync(token);
            return false;
        }
        await using (NpgsqlCommand action = connection.CreateCommand())
        {
            action.Transaction = tx;
            action.CommandText = """
                INSERT INTO video_face_review_actions
                  (observation_id, decision, person_id, actor, created_at_utc)
                VALUES (@face, @decision, @person, @actor, @at);
                """;
            action.Parameters.AddWithValue("face", observationId);
            action.Parameters.AddWithValue("decision", decision);
            action.Parameters.Add(new NpgsqlParameter("person", NpgsqlTypes.NpgsqlDbType.Uuid)
            { Value = personId.HasValue ? personId.Value : DBNull.Value });
            action.Parameters.AddWithValue("actor", actor.Trim());
            action.Parameters.AddWithValue("at", timeProvider.GetUtcNow());
            await action.ExecuteNonQueryAsync(token);
        }
        await tx.CommitAsync(token);
        return true;
    }

    private static bool BoundedActor(string? actor) =>
        actor is { Length: >= 1 and <= 120 } && actor.Trim().Length is >= 1 and <= 120;
}
