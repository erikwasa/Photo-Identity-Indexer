using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

public sealed record VideoFacePresenceOutcome(string Status, bool VideoPersonTagChanged);

/// <summary>
/// Human-only promotion from a currently reviewed, source-attested, leased
/// FaceEvidence observation into the existing manual video-person action log.
/// A link is written only when the command itself creates the tag. Unlink
/// removes only its own still-current add, never an independently created
/// or subsequently edited manual presence. No inference or source-media I/O.
/// </summary>
public sealed class PostgresVideoFacePresenceRepository(
    PostgresCatalogueDatabase database, TimeProvider timeProvider)
{
    public async Task<VideoFacePresenceOutcome> PromoteAsync(
        AssetRevisionId revision, Guid observation, Guid person,
        string actor, CancellationToken token = default)
    {
        Validate(revision, observation, actor);
        if (person == Guid.Empty) throw new ArgumentException("A person is required.");

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        long reviewAction = await LockReviewedKnownFaceAsync(
            connection, transaction, revision, observation, person, token);

        // Person merges and display-name retirement cannot turn a stale
        // choice into a new video tag while the transaction is running.
        await using (NpgsqlCommand canonical = connection.CreateCommand())
        {
            canonical.Transaction = transaction;
            canonical.CommandText = """
                SELECT 1 FROM people WHERE id=@person
                  AND merged_into_person_id IS NULL
                  AND display_name IS NOT NULL AND btrim(display_name)<>''
                FOR UPDATE;
                """;
            canonical.Parameters.AddWithValue("person", person);
            if (await canonical.ExecuteScalarAsync(token) is null)
                throw new InvalidOperationException("The confirmed person is no longer canonical.");
        }

        (string? kind, Guid? linkedPerson, long? added) =
            await ReadLatestLinkAsync(connection, transaction, observation, token);
        if (kind == "link")
        {
            if (linkedPerson != person)
                throw new InvalidOperationException(
                    "Undo the prior face-to-presence link before changing the person.");
            await transaction.CommitAsync(token);
            return new("already-linked", false);
        }

        // Canonical presence may already come from a manually tagged alias.
        // Never manufacture a second add (or claim undo ownership of it).
        await using (NpgsqlCommand present = connection.CreateCommand())
        {
            present.Transaction = transaction;
            present.CommandText = """
                WITH RECURSIVE aliases(id) AS (
                    SELECT @person::uuid
                    UNION
                    SELECT p.id FROM people p JOIN aliases a
                      ON p.merged_into_person_id=a.id
                )
                SELECT EXISTS (
                    SELECT 1 FROM aliases a
                    JOIN LATERAL (
                        SELECT action_kind FROM video_person_actions
                        WHERE asset_revision_id=@revision AND person_id=a.id
                        ORDER BY id DESC LIMIT 1
                    ) latest ON latest.action_kind='add'
                );
                """;
            present.Parameters.AddWithValue("person", person);
            present.Parameters.AddWithValue("revision", revision.Value);
            if (await present.ExecuteScalarAsync(token) is true)
            {
                await transaction.CommitAsync(token);
                return new("already-present", false);
            }
        }

        long addAction;
        await using (NpgsqlCommand add = connection.CreateCommand())
        {
            add.Transaction = transaction;
            add.CommandText = """
                INSERT INTO video_person_actions
                    (asset_revision_id, person_id, action_kind, actor, created_at_utc)
                VALUES (@revision, @person, 'add', @actor, @at)
                RETURNING id;
                """;
            add.Parameters.AddWithValue("revision", revision.Value);
            add.Parameters.AddWithValue("person", person);
            add.Parameters.AddWithValue("actor", actor.Trim());
            add.Parameters.AddWithValue("at", timeProvider.GetUtcNow());
            addAction = (long)(await add.ExecuteScalarAsync(token)
                ?? throw new InvalidOperationException("The video presence action was not stored."));
        }

        await WriteLinkAsync(connection, transaction, revision, observation,
            person, reviewAction, addAction, null, "link", actor, token);
        await transaction.CommitAsync(token);
        return new("linked", true);
    }

    public async Task<VideoFacePresenceOutcome> UndoAsync(
        AssetRevisionId revision, Guid observation, string actor,
        CancellationToken token = default)
    {
        Validate(revision, observation, actor);

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        // Even an undo requires a current, source-revision-locked original
        // observation. Its person comes only from persisted review history.
        Guid? confirmed = await LockReviewedFaceAsync(
            connection, transaction, revision, observation, token);
        (string? kind, Guid? person, long? addAction) =
            await ReadLatestLinkAsync(connection, transaction, observation, token);
        if (kind != "link")
        {
            await transaction.CommitAsync(token);
            return new("already-unlinked", false);
        }
        if (person is null || addAction is null || confirmed != person)
            throw new InvalidOperationException(
                "The face-to-video link has stale identity provenance.");

        // A later manual action (including a removal) owns the current state.
        // In that case release the face linkage but do not alter manual tags.
        long? removed = null;
        await using (NpgsqlCommand latest = connection.CreateCommand())
        {
            latest.Transaction = transaction;
            latest.CommandText = """
                SELECT id FROM video_person_actions
                WHERE asset_revision_id=@revision AND person_id=@person
                  AND action_kind='add'
                  AND id=(SELECT MAX(id) FROM video_person_actions
                          WHERE asset_revision_id=@revision AND person_id=@person);
                """;
            latest.Parameters.AddWithValue("revision", revision.Value);
            latest.Parameters.AddWithValue("person", person.Value);
            if (await latest.ExecuteScalarAsync(token) is long currentAdd &&
                currentAdd == addAction.Value)
            {
                // Do not remove an alias-merged presence; another manual
                // canonical action may now represent it. Require explicit
                // reconciliation in Video Details instead.
                await using NpgsqlCommand canonical = connection.CreateCommand();
                canonical.Transaction = transaction;
                canonical.CommandText = """
                    SELECT merged_into_person_id IS NULL
                    FROM people WHERE id=@person FOR UPDATE;
                    """;
                canonical.Parameters.AddWithValue("person", person.Value);
                if (await canonical.ExecuteScalarAsync(token) is true)
                {
                    await using NpgsqlCommand remove = connection.CreateCommand();
                    remove.Transaction = transaction;
                    remove.CommandText = """
                        INSERT INTO video_person_actions
                            (asset_revision_id, person_id, action_kind, actor, created_at_utc)
                        VALUES (@revision, @person, 'remove', @actor, @at)
                        RETURNING id;
                        """;
                    remove.Parameters.AddWithValue("revision", revision.Value);
                    remove.Parameters.AddWithValue("person", person.Value);
                    remove.Parameters.AddWithValue("actor", actor.Trim());
                    remove.Parameters.AddWithValue("at", timeProvider.GetUtcNow());
                    removed = (long)(await remove.ExecuteScalarAsync(token)
                        ?? throw new InvalidOperationException("The undo action was not stored."));
                }
            }
        }

        // Preserve both link and unlink in the audit trail. A successful
        // unlink does not rewrite the reviewed face identity.
        long reviewAction = await ReadKnownReviewActionAsync(
            connection, transaction, observation, person.Value, token);
        await WriteLinkAsync(connection, transaction, revision, observation,
            person.Value, reviewAction, addAction.Value, removed, "unlink", actor, token);
        await transaction.CommitAsync(token);
        return new("unlinked", removed.HasValue);
    }

    private static void Validate(AssetRevisionId revision, Guid observation, string actor)
    {
        if (revision.IsEmpty || observation == Guid.Empty ||
            string.IsNullOrWhiteSpace(actor) || actor.Trim().Length > 120)
            throw new ArgumentException("Invalid human video presence action.");
    }

    private static async Task<long> LockReviewedKnownFaceAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        AssetRevisionId revision, Guid observation, Guid person, CancellationToken token)
    {
        Guid? confirmed = await LockReviewedFaceAsync(
            connection, tx, revision, observation, token);
        if (confirmed != person)
            throw new InvalidOperationException(
                "The confirmed face identity changed; review again before promoting.");
        return await ReadKnownReviewActionAsync(connection, tx, observation, person, token);
    }

    private static async Task<Guid?> LockReviewedFaceAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        AssetRevisionId revision, Guid observation, CancellationToken token)
    {
        // Same lock order as native crop and manual Video Details edits:
        // source first; then asset; then face observation; then person.
        await using (NpgsqlCommand source = connection.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT source.id FROM asset_revisions r
                JOIN assets asset ON asset.id=r.asset_id
                JOIN sources source ON source.id=asset.source_id
                WHERE r.id=@revision AND source.kind='local-folder'
                FOR UPDATE OF source;
                """;
            source.Parameters.AddWithValue("revision", revision.Value);
            if (await source.ExecuteScalarAsync(token) is not Guid)
                throw new KeyNotFoundException("The video source is not available.");
        }

        await using (NpgsqlCommand asset = connection.CreateCommand())
        {
            asset.Transaction = tx;
            asset.CommandText = """
                SELECT asset.id FROM asset_revisions r
                JOIN assets asset ON asset.id=r.asset_id
                WHERE r.id=@revision FOR UPDATE OF asset;
                """;
            asset.Parameters.AddWithValue("revision", revision.Value);
            if (await asset.ExecuteScalarAsync(token) is not Guid)
                throw new KeyNotFoundException("The video is not available.");
        }

        await using (NpgsqlCommand face = connection.CreateCommand())
        {
            face.Transaction = tx;
            face.CommandText = """
                SELECT metadata.duration_seconds, f.frame_ordinal,
                       f.timestamp_ms, job.requested_frames
                FROM video_face_observations f
                JOIN video_face_evidence_batches b ON b.id=f.batch_id
                JOIN video_processing_jobs job ON job.id=b.publishing_job_id
                JOIN asset_revisions revision ON revision.id=b.asset_revision_id
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                JOIN video_revision_metadata metadata
                  ON metadata.asset_revision_id=revision.id
                WHERE f.id=@face AND b.asset_revision_id=@revision
                  AND b.source_sha256=revision.content_sha256
                  AND b.producer_version='wi0155-native-face-evidence-v1'
                  AND b.detector_model_id=@detector
                  AND b.detector_model_hash=@detector_hash
                  AND b.sface_model_id=@embedder
                  AND b.sface_model_hash=@embedder_hash
                  AND b.alignment_protocol=@alignment
                  AND b.observation_count=(
                     SELECT count(*) FROM video_face_observations
                     WHERE batch_id=b.id)
                  AND job.asset_revision_id=revision.id
                  AND job.status='succeeded' AND job.derivative_kind=@kind
                  AND job.policy_version=@policy AND job.preset_version=@preset
                  AND job.source_bytes=revision.size_bytes
                  AND job.requested_frames BETWEEN 1 AND 4
                  AND metadata.duration_seconds BETWEEN 0.2 AND 3600
                  AND revision.size_bytes BETWEEN 1 AND 268435456
                  AND f.frame_ordinal BETWEEN 0 AND 3
                  AND f.frame_ordinal < job.requested_frames
                  AND revision.media_kind='video' AND asset.media_kind='video'
                  AND source.kind='local-folder' AND asset.deleted_at_utc IS NULL
                  AND metadata.inspection_status='supported'
                  AND metadata.color_transfer='bt709'
                  AND revision.id=(SELECT latest.id FROM asset_revisions latest
                     WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                     ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                  AND NOT EXISTS (
                     SELECT 1 FROM source_copy_exclusions e
                     WHERE e.source_id=asset.source_id AND e.source_key=asset.source_key)
                FOR UPDATE OF f;
                """;
            face.Parameters.AddWithValue("face", observation);
            face.Parameters.AddWithValue("revision", revision.Value);
            face.Parameters.AddWithValue("detector", VideoFaceEvidenceLeaseIdentity.DetectorModelId);
            face.Parameters.AddWithValue("detector_hash", VideoFaceEvidenceLeaseIdentity.DetectorSha256);
            face.Parameters.AddWithValue("embedder", VideoFaceEvidenceLeaseIdentity.EmbedderModelId);
            face.Parameters.AddWithValue("embedder_hash", VideoFaceEvidenceLeaseIdentity.EmbedderSha256);
            face.Parameters.AddWithValue("alignment", VideoFaceEvidenceLeaseIdentity.AlignmentProtocol);
            face.Parameters.AddWithValue("kind", VideoFaceEvidenceLeaseIdentity.Kind);
            face.Parameters.AddWithValue("policy", VideoFaceEvidenceLeaseIdentity.PolicyVersion);
            face.Parameters.AddWithValue("preset", VideoFaceEvidenceLeaseIdentity.PresetVersion);
            await using NpgsqlDataReader reader = await face.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                throw new KeyNotFoundException("Current leased face evidence is not available.");
            int ordinal = reader.GetInt32(1);
            long timestamp = reader.GetInt64(2);
            int requested = reader.GetInt32(3);
            VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
                new VideoFrameSamplingRequest(reader.GetDecimal(0),
                    MaximumFrames: requested,
                    MaximumReservedDecodeMilliseconds: 12000,
                    ReservedDecodeMillisecondsPerFrame: 3000));
            if (plan.Samples.Count != requested || ordinal < 0 ||
                ordinal >= plan.Samples.Count ||
                plan.Samples[ordinal].TimestampMilliseconds != timestamp)
                throw new KeyNotFoundException(
                    "The face observation does not match its attested sampling position.");
        }

        // Source lock held: changes to current review, person tags, or
        // exclusion cannot slip between this read and the presence commit.
        await using NpgsqlCommand review = connection.CreateCommand();
        review.Transaction = tx;
        review.CommandText = """
            SELECT decision, person_id FROM video_face_review_actions
            WHERE observation_id=@face ORDER BY id DESC LIMIT 1;
            """;
        review.Parameters.AddWithValue("face", observation);
        await using NpgsqlDataReader reader = await review.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || reader.GetString(0) != "known" ||
            reader.IsDBNull(1))
            throw new InvalidOperationException(
                "An explicit known-person face review is required.");
        return reader.GetGuid(1);
    }

    private static async Task<long> ReadKnownReviewActionAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid observation, Guid person, CancellationToken token)
    {
        await using NpgsqlCommand review = connection.CreateCommand();
        review.Transaction = tx;
        review.CommandText = """
            SELECT id FROM video_face_review_actions
            WHERE observation_id=@face AND decision='known' AND person_id=@person
              AND id=(SELECT MAX(id) FROM video_face_review_actions
                      WHERE observation_id=@face);
            """;
        review.Parameters.AddWithValue("face", observation);
        review.Parameters.AddWithValue("person", person);
        if (await review.ExecuteScalarAsync(token) is not long id)
            throw new InvalidOperationException("The human face confirmation is stale.");
        return id;
    }

    private static async Task<(string? Kind, Guid? Person, long? Added)> ReadLatestLinkAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid observation, CancellationToken token)
    {
        await using NpgsqlCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT action_kind, person_id, add_action_id
            FROM video_face_presence_actions WHERE observation_id=@face
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("face", observation);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? (reader.GetString(0), reader.GetGuid(1), reader.GetInt64(2))
            : (null, null, null);
    }

    private async Task WriteLinkAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx,
        AssetRevisionId revision, Guid observation, Guid person,
        long reviewAction, long addAction, long? removeAction,
        string kind, string actor, CancellationToken token)
    {
        await using NpgsqlCommand write = connection.CreateCommand();
        write.Transaction = tx;
        write.CommandText = """
            INSERT INTO video_face_presence_actions (
                asset_revision_id, observation_id, person_id, review_action_id,
                add_action_id, remove_action_id, action_kind, actor, created_at_utc)
            VALUES (@revision, @face, @person, @review,
                    @add, @remove, @kind, @actor, @at);
            """;
        write.Parameters.AddWithValue("revision", revision.Value);
        write.Parameters.AddWithValue("face", observation);
        write.Parameters.AddWithValue("person", person);
        write.Parameters.AddWithValue("review", reviewAction);
        write.Parameters.AddWithValue("add", addAction);
        write.Parameters.Add(new Npgsql.NpgsqlParameter("remove", NpgsqlTypes.NpgsqlDbType.Bigint)
            { Value = removeAction.HasValue ? removeAction.Value : DBNull.Value });
        write.Parameters.AddWithValue("kind", kind);
        write.Parameters.AddWithValue("actor", actor.Trim());
        write.Parameters.AddWithValue("at", timeProvider.GetUtcNow());
        await write.ExecuteNonQueryAsync(token);
    }

}
