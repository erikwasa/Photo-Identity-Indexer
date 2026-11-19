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
            [new(0, 1000, 0, 1600, 900, .2, .1, .05, .1,
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
            Assert.Equal(43, PostgresCatalogueDatabase.CurrentSchemaVersion);
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
                       color_transfer,inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@video,'supported',8,'bt709',now(),repeat('b',64),'synthetic');
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
            Assert.Throws<ArgumentException>(() =>
                scopes.ArmAsync(frameJob, ToolSha,
                    VideoFaceEvidenceLeaseIdentity.PresetVersion,
                    TimeSpan.FromMinutes(5), derivativeKind: "FaceEvidence")
                .GetAwaiter().GetResult());
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
