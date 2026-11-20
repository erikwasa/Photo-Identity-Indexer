using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Disposable PostgreSQL and synthetic 128D embeddings. Never reads original
/// media, executes FFmpeg, invokes ONNX models or changes production identities.
/// </summary>
public sealed class VideoFaceLeasedPublicationTests
{
    private static readonly string SourceSha = new('a', 64);
    private static readonly string ToolSha = new('f', 64);

    private static VideoFaceEmbeddingPublication Evidence(AssetRevisionId revision)
    {
        float[] vector = new float[128];
        vector[0] = 1;
        return new(revision, SourceSha, "wi0155-native-face-evidence-v1",
            VideoFaceEvidenceLeaseIdentity.DetectorModelId,
            VideoFaceEvidenceLeaseIdentity.DetectorSha256,
            VideoFaceEvidenceLeaseIdentity.EmbedderModelId,
            VideoFaceEvidenceLeaseIdentity.EmbedderSha256,
            VideoFaceEvidenceLeaseIdentity.AlignmentProtocol,
            [new(0, 4000, 0, 1600, 900, .2, .1, .05, .1,
                new EmbeddingVector(vector))]);
    }

    [Fact]
    public async Task Selected_FaceEvidence_job_atomically_publishes_and_consumes_live_lease_without_assignments()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string name = "photoidentity_face_lease_" + Guid.NewGuid().ToString("N");

        await using NpgsqlConnection administrator = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await administrator.OpenAsync();
        await using (NpgsqlCommand create = administrator.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using PostgresCatalogueDatabase database = new(
                new NpgsqlConnectionStringBuilder(admin)
                { Database = name, Pooling = false }.ConnectionString);
            await database.InitializeAsync();
            // The v43 video-face lease migration remains required even when
            // later, unrelated catalogue migrations advance the schema.
            Assert.True(PostgresCatalogueDatabase.CurrentSchemaVersion >= 43);
            await using (NpgsqlConnection schemaProbe = await database.OpenConnectionAsync())
            await using (NpgsqlCommand schemaProbeCommand = schemaProbe.CreateCommand())
            {
                schemaProbeCommand.CommandText = """
                    SELECT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'video_face_evidence_batches'
                          AND column_name = 'publishing_job_id');
                    """;
                Assert.True((bool)(await schemaProbeCommand.ExecuteScalarAsync())!);
            }
            Guid source = Guid.NewGuid(), videoAsset = Guid.NewGuid(),
                photoAsset = Guid.NewGuid(), videoId = Guid.NewGuid(),
                photoId = Guid.NewGuid(), person = Guid.NewGuid(),
                face = Guid.NewGuid(), crop = Guid.NewGuid();
            byte[] vectorBlob = new byte[128 * sizeof(float)];
            float[] vector = new float[128];
            vector[0] = 1;
            Buffer.BlockCopy(vector, 0, vectorBlob, 0, vectorBlob.Length);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-face-lease',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@video_asset,@source,'synthetic.mp4',now(),'video'),
                           (@photo_asset,@source,'exemplar.jpg',now(),'photo');
                    INSERT INTO asset_revisions
                      (id,asset_id,media_kind,content_sha256,size_bytes,observed_at_utc,media_type)
                    VALUES (@video,@video_asset,'video',repeat('a',64),4000,now(),'video/mp4'),
                           (@photo,@photo_asset,'photo',repeat('e',64),3000,now(),'image/jpeg');
                    INSERT INTO video_revision_metadata
                      (asset_revision_id,inspection_status,duration_seconds,
                       color_transfer,coded_width,coded_height,
                       inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@video,'supported',8,'bt709',1600,900,
                            now(),repeat('b',64),'synthetic');
                    INSERT INTO people(id,display_name,created_at_utc)
                    VALUES (@person,'Test person',now());
                    INSERT INTO face_occurrences(id,asset_revision_id,ordinal,created_at_utc)
                    VALUES (@face,@photo,0,now());
                    INSERT INTO face_crops(
                      id,face_occurrence_id,crop_protocol,content_sha256,
                      storage_path,width,height,created_at_utc)
                    VALUES (@crop,@face,'sface-five-point-v1',repeat('d',64),
                            'synthetic/face.jpg',112,112,now());
                    INSERT INTO embeddings(
                      face_crop_id,model_id,model_hash,dimensions,
                      l2_norm,vector_blob,created_at_utc)
                    VALUES (@crop,@model,@model_hash,128,1.0,@vector,now());
                    INSERT INTO person_labels(
                      person_id,face_occurrence_id,label_kind,assigned_by,assigned_at_utc)
                    VALUES (@person,@face,'confirmed','synthetic',now());
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("video_asset", videoAsset);
                seed.Parameters.AddWithValue("photo_asset", photoAsset);
                seed.Parameters.AddWithValue("video", videoId);
                seed.Parameters.AddWithValue("photo", photoId);
                seed.Parameters.AddWithValue("face", face);
                seed.Parameters.AddWithValue("crop", crop);
                seed.Parameters.AddWithValue("person", person);
                seed.Parameters.AddWithValue("model", VideoFaceEvidenceLeaseIdentity.EmbedderModelId);
                seed.Parameters.AddWithValue("model_hash", VideoFaceEvidenceLeaseIdentity.EmbedderSha256);
                seed.Parameters.AddWithValue("vector", vectorBlob);
                await seed.ExecuteNonQueryAsync();
            }

            AssetRevisionId revision = AssetRevisionId.From(videoId);
            VideoFaceEmbeddingPublication proposal = Evidence(revision);
            var budget = new VideoProcessingBudget(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 5000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 4);
            var candidate = new VideoProcessingCandidate(
                true, false, true, true, 4000, 16000, 1);
            var queue = new PostgresVideoProcessingJobRepository(database);
            var scopes = new PostgresVideoScopedAttemptControl(database);
            var review = new PostgresVideoFaceReviewRepository(database, TimeProvider.System);
            var publisher = new PostgresVideoFaceMatchPublisher(
                new PostgresIdentityMatchRegenerationScorer(database), review);

            Guid frameJob = await queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(revision, VideoDerivativeKind.FrameEvidence,
                    VideoFaceEvidenceLeaseIdentity.PolicyVersion,
                    VideoFaceEvidenceLeaseIdentity.PresetVersion, new string('c', 64)),
                budget, candidate);
            Guid faceJob = await queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(revision, VideoDerivativeKind.FaceEvidence,
                    VideoFaceEvidenceLeaseIdentity.PolicyVersion,
                    VideoFaceEvidenceLeaseIdentity.PresetVersion, ToolSha),
                budget, candidate);
            // A scoped grant cannot silently turn the unrelated FrameEvidence
            // job into an authorized YuNet/SFace run.
            Assert.Null(await scopes.ArmAsync(frameJob,
                new string('c', 64), VideoFaceEvidenceLeaseIdentity.PresetVersion,
                TimeSpan.FromMinutes(5), derivativeKind: "FaceEvidence"));

            VideoScopedAttemptGrant grant = Assert.IsType<VideoScopedAttemptGrant>(
                await scopes.ArmAsync(faceJob, ToolSha,
                    VideoFaceEvidenceLeaseIdentity.PresetVersion,
                    TimeSpan.FromMinutes(5), derivativeKind: "FaceEvidence"));
            Assert.False(await review.HasCurrentJobReceiptAsync(faceJob, revision));
            // A fabricated lease may carry the correct UUID but has no
            // matching live token; scoring must not commit anything.
            var forged = new VideoProcessingJobLease(
                faceJob, revision, Guid.NewGuid(), "FaceEvidence",
                VideoFaceEvidenceLeaseIdentity.PolicyVersion,
                VideoFaceEvidenceLeaseIdentity.PresetVersion, ToolSha,
                4000, 16000, 1, grant.Generation);
            Assert.Null(await publisher.TryPublishLeasedAsync(forged, proposal));
            Assert.Null(await review.GetCurrentAsync(revision));
            Assert.Equal("queued", (await queue.GetAsync(faceJob))?.Status);

            VideoProcessingJobLease? captured = null;
            var producer = new SyntheticProducer((lease, _) =>
            {
                captured = lease;
                return Task.FromResult<VideoFaceEmbeddingPublication?>(proposal);
            });
            var adapter = new VideoFaceEvidenceWorkerAdapter(database, producer);
            Assert.Equal("FaceEvidence", adapter.RequiredDerivativeKind);
            VideoWorkerCycleResult result = await new VideoWorkerAttemptCoordinator(database)
                .RunOneAsync(budget, adapter, selectedJobId: faceJob);
            Assert.True(result.Claimed);
            Assert.True(result.Published);
            Assert.Equal("published", result.ReasonCode);
            Assert.Equal(faceJob, captured?.Id);
            Assert.Equal("queued", (await queue.GetAsync(frameJob))?.Status);
            Assert.Equal("succeeded", (await queue.GetAsync(faceJob))?.Status);

            Assert.True(await review.HasCurrentJobReceiptAsync(faceJob, revision));
            Assert.False(await review.HasCurrentJobReceiptAsync(frameJob, revision));
            var stored = Assert.IsType<VideoFaceReviewBatch>(await review.GetCurrentAsync(revision));
            var observation = Assert.Single(stored.Observations);
            var lead = Assert.Single(observation.Suggestions);
            Assert.Equal(person.ToString("D"), lead.PersonId);
            Assert.InRange(lead.Similarity, .999, 1.001);
            Assert.Null(observation.Decision);

            // Synthetic PostgreSQL proves exact leased-job native crop
            // authorization without opening a real source or invoking FFmpeg.
            var cropAccess = new PostgresVideoFaceNativeCropRepository(database);
            await using (VideoFaceNativeCropSourceLease? nativeCrop =
                await cropAccess.OpenAsync(revision, Guid.Parse(observation.Id), ToolSha))
            {
                Assert.NotNull(nativeCrop);
                Assert.Equal(SourceSha, nativeCrop!.Source.SourceSha256);
                Assert.Equal(4000L, nativeCrop.Source.TimestampMs);
                Assert.Equal(8000L, nativeCrop.Source.DurationMilliseconds);
                Assert.Equal(1600, nativeCrop.Source.FrameWidth);
                Assert.Equal(900, nativeCrop.Source.FrameHeight);
            }
            Assert.Null(await cropAccess.OpenAsync(revision,
                Guid.Parse(observation.Id), new string('d', 64)));
            Assert.Null(await cropAccess.OpenAsync(revision, Guid.NewGuid(), ToolSha));
            Assert.Null(await cropAccess.OpenAsync(
                AssetRevisionId.From(photoId), Guid.Parse(observation.Id), ToolSha));

            // The first lease was consumed; retry cannot publish or
            // re-mark the already succeeded job.
            Assert.NotNull(captured);
            Assert.Null(await publisher.TryPublishLeasedAsync(captured!, proposal));

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand check = connection.CreateCommand())
            {
                check.CommandText = """
                    SELECT (SELECT count(*) FROM video_face_evidence_batches),
                           (SELECT count(*) FROM video_face_observations),
                           (SELECT count(*) FROM video_face_review_actions),
                           (SELECT count(*) FROM video_person_actions),
                           (SELECT count(*) FROM photo_person_actions);
                    """;
                await using NpgsqlDataReader reader = await check.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
                Assert.Equal(0L, reader.GetInt64(3));
                Assert.Equal(0L, reader.GetInt64(4));
            }

            Assert.True(await scopes.DisarmAsync(grant));
            Assert.True(await review.HasCurrentJobReceiptAsync(faceJob, revision));

            // A separately selected revision with ZERO detected faces still
            // needs an immutable, current, job-bound receipt. It is never a
            // claim that no person appears elsewhere in the full video.
            Guid emptyAsset = Guid.NewGuid(), emptyVideo = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seedEmpty = connection.CreateCommand())
            {
                seedEmpty.CommandText = """
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'empty-sampled.mp4',now(),'video');
                    INSERT INTO asset_revisions
                      (id,asset_id,media_kind,content_sha256,size_bytes,observed_at_utc,media_type)
                    VALUES (@video,@asset,'video',repeat('a',64),4000,now(),'video/mp4');
                    INSERT INTO video_revision_metadata
                      (asset_revision_id,inspection_status,duration_seconds,
                       color_transfer,inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@video,'supported',8,'bt709',now(),repeat('b',64),'synthetic');
                    """;
                seedEmpty.Parameters.AddWithValue("asset", emptyAsset);
                seedEmpty.Parameters.AddWithValue("source", source);
                seedEmpty.Parameters.AddWithValue("video", emptyVideo);
                await seedEmpty.ExecuteNonQueryAsync();
            }

            AssetRevisionId emptyRevision = AssetRevisionId.From(emptyVideo);
            VideoFaceEmbeddingPublication emptyEvidence = Evidence(emptyRevision)
                with { Observations = [] };
            // An unleased caller must never mint a zero-face receipt.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                publisher.PublishAsync(emptyEvidence));
            Assert.Null(await review.GetCurrentAsync(emptyRevision));

            Guid emptyJob = await queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(emptyRevision, VideoDerivativeKind.FaceEvidence,
                    VideoFaceEvidenceLeaseIdentity.PolicyVersion,
                    VideoFaceEvidenceLeaseIdentity.PresetVersion, ToolSha),
                budget, candidate);
            VideoScopedAttemptGrant emptyGrant = Assert.IsType<VideoScopedAttemptGrant>(
                await scopes.ArmAsync(emptyJob, ToolSha,
                    VideoFaceEvidenceLeaseIdentity.PresetVersion,
                    TimeSpan.FromMinutes(5), derivativeKind: "FaceEvidence"));

            var forgedEmpty = new VideoProcessingJobLease(
                emptyJob, emptyRevision, Guid.NewGuid(), "FaceEvidence",
                VideoFaceEvidenceLeaseIdentity.PolicyVersion,
                VideoFaceEvidenceLeaseIdentity.PresetVersion, ToolSha,
                4000, 16000, 1, emptyGrant.Generation);
            Assert.Null(await publisher.TryPublishLeasedAsync(forgedEmpty, emptyEvidence));
            Assert.Null(await review.GetCurrentAsync(emptyRevision));

            var zeroDetector = new SyntheticProducer((_, _) =>
                Task.FromResult<VideoFaceEmbeddingPublication?>(emptyEvidence));
            var zeroAdapter = new VideoFaceEvidenceWorkerAdapter(database, zeroDetector);
            VideoWorkerCycleResult emptyCycle = await new VideoWorkerAttemptCoordinator(database)
                .RunOneAsync(budget, zeroAdapter, selectedJobId: emptyJob);
            Assert.True(emptyCycle.Claimed);
            Assert.True(emptyCycle.Published);
            Assert.Equal("published", emptyCycle.ReasonCode);
            Assert.Equal("succeeded", (await queue.GetAsync(emptyJob))?.Status);
            Assert.True(await review.HasCurrentJobReceiptAsync(emptyJob, emptyRevision));
            VideoFaceReviewBatch emptyBatch = Assert.IsType<VideoFaceReviewBatch>(
                await review.GetCurrentAsync(emptyRevision));
            Assert.Empty(emptyBatch.Observations);
            Assert.NotEqual(stored.BatchId, emptyBatch.BatchId);

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand counts = connection.CreateCommand())
            {
                counts.CommandText = """
                    SELECT (SELECT COUNT(*) FROM video_face_evidence_batches
                            WHERE asset_revision_id=@revision AND observation_count=0),
                           (SELECT COUNT(*) FROM video_face_observations
                            WHERE batch_id=@batch),
                           (SELECT COUNT(*) FROM video_person_actions),
                           (SELECT COUNT(*) FROM photo_person_actions);
                    """;
                counts.Parameters.AddWithValue("revision", emptyVideo);
                counts.Parameters.AddWithValue("batch", Guid.Parse(emptyBatch.BatchId));
                await using NpgsqlDataReader reader = await counts.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
                Assert.Equal(0L, reader.GetInt64(3));
            }
            Assert.True(await scopes.DisarmAsync(emptyGrant));

            // Source exclusion revokes even an empty job receipt, without
            // affecting the distinct source key's positive evidence.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = connection.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions(
                      source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'empty-sampled.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            Assert.Null(await review.GetCurrentAsync(emptyRevision));
            Assert.False(await review.HasCurrentJobReceiptAsync(emptyJob, emptyRevision));
            Assert.True(await review.HasCurrentJobReceiptAsync(faceJob, revision));

            // v46: human review alone never creates video-person tags. Only a
            // separately approved, revision/source/lease-fenced operation
            // can write one owned add, and undo cannot touch other edits.
            var presence = new PostgresVideoFacePresenceRepository(
                database, TimeProvider.System);
            var details = new PostgresVideoDetailsRepository(database, TimeProvider.System);
            Guid observationId = Guid.Parse(observation.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                presence.PromoteAsync(revision, observationId, person, "synthetic"));
            Assert.Empty((await details.GetAsync(revision))!.People);
            Assert.True(await review.RecordDecisionAsync(
                revision, observationId, "known", person, "synthetic"));
            Assert.Empty((await details.GetAsync(revision))!.People);

            VideoFacePresenceOutcome added = await presence.PromoteAsync(
                revision, observationId, person, "synthetic");
            Assert.Equal("linked", added.Status);
            Assert.True(added.VideoPersonTagChanged);
            Assert.Single((await details.GetAsync(revision))!.People);
            VideoFacePresenceLink link = Assert.IsType<VideoFacePresenceLink>(
                Assert.Single((await review.GetCurrentAsync(revision))!.Observations).Presence);
            Assert.Equal(person.ToString("D"), link.PersonId);
            Assert.True(link.VideoTagStillCurrent);

            VideoFacePresenceOutcome retry = await presence.PromoteAsync(
                revision, observationId, person, "synthetic");
            Assert.Equal("already-linked", retry.Status);
            Assert.False(retry.VideoPersonTagChanged);
            Assert.False(await review.RecordDecisionAsync(
                revision, observationId, "known", person, "synthetic"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.RecordDecisionAsync(
                    revision, observationId, "unknown", null, "synthetic"));

            VideoFacePresenceOutcome undone = await presence.UndoAsync(
                revision, observationId, "synthetic");
            Assert.Equal("unlinked", undone.Status);
            Assert.True(undone.VideoPersonTagChanged);
            Assert.Empty((await details.GetAsync(revision))!.People);
            Assert.Null(Assert.Single((await review.GetCurrentAsync(revision))!.Observations).Presence);
            Assert.Equal("already-unlinked",
                (await presence.UndoAsync(revision, observationId, "synthetic")).Status);
            Assert.True(await review.RecordDecisionAsync(
                revision, observationId, "unknown", null, "synthetic"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                presence.PromoteAsync(revision, observationId, person, "synthetic"));

            // Existing manual presence is never claimed as promotion-owned;
            // a no-op promote must not make that manual tag undoable.
            Assert.True(await review.RecordDecisionAsync(
                revision, observationId, "known", person, "synthetic"));
            await details.SetPersonAsync(revision, person, true, "manual-test");
            VideoFacePresenceOutcome existing = await presence.PromoteAsync(
                revision, observationId, person, "synthetic");
            Assert.Equal("already-present", existing.Status);
            Assert.False(existing.VideoPersonTagChanged);
            Assert.Equal("already-unlinked",
                (await presence.UndoAsync(revision, observationId, "synthetic")).Status);
            Assert.Single((await details.GetAsync(revision))!.People);
            await details.SetPersonAsync(revision, person, false, "manual-test");

            // A subsequent manual removal takes precedence over the original
            // face-owned add. Undo only records unlink, not a second removal.
            Assert.Equal("linked",
                (await presence.PromoteAsync(revision, observationId, person, "synthetic")).Status);
            await details.SetPersonAsync(revision, person, false, "manual-test");
            Assert.False(Assert.IsType<VideoFacePresenceLink>(
                Assert.Single((await review.GetCurrentAsync(revision))!.Observations).Presence)
                .VideoTagStillCurrent);
            VideoFacePresenceOutcome afterManual = await presence.UndoAsync(
                revision, observationId, "synthetic");
            Assert.Equal("unlinked", afterManual.Status);
            Assert.False(afterManual.VideoPersonTagChanged);
            Assert.Empty((await details.GetAsync(revision))!.People);

            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                presence.PromoteAsync(emptyRevision, observationId, person, "synthetic"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                presence.UndoAsync(AssetRevisionId.From(photoId),
                    observationId, "synthetic"));

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand trace = connection.CreateCommand())
            {
                trace.CommandText = """
                    SELECT (SELECT count(*) FROM video_face_presence_actions
                            WHERE asset_revision_id=@revision),
                           (SELECT count(*) FROM photo_person_actions),
                           (SELECT count(*) FROM video_person_actions
                            WHERE asset_revision_id=@revision);
                    """;
                trace.Parameters.AddWithValue("revision", videoId);
                await using NpgsqlDataReader reader = await trace.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(4L, reader.GetInt64(0)); // link, unlink, link, unlink
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(6L, reader.GetInt64(2)); // two linked cycles + manual add/remove
            }
        }
        finally
        {
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class SyntheticProducer(
        Func<VideoProcessingJobLease, CancellationToken,
            Task<VideoFaceEmbeddingPublication?>> callback)
        : IVideoFaceEmbeddingProducer
    {
        public Task<VideoFaceEmbeddingPublication?> ProduceAsync(
            VideoProcessingJobLease lease, CancellationToken token) =>
            callback(lease, token);
    }
}
