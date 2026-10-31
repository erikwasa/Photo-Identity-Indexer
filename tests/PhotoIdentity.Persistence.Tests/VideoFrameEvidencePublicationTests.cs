using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

/// <summary>
/// No personal files or remote models. CI creates and drops an isolated
/// PostgreSQL catalogue when the test-admin connection is supplied.
/// </summary>
public sealed class VideoFrameEvidencePublicationTests
{
    private static readonly VideoFrameSamplingPlan Plan = VideoFrameSamplingPlanner.Plan(
        new VideoFrameSamplingRequest(8m, MaximumFrames: 2));

    private static VideoFrameEvidenceBatch Sample(Guid revision)
    {
        VideoFrameEvidenceProvenance p = new(
            AssetRevisionId.From(revision), Plan.PolicyVersion,
            "wi0190-sdr-frame-jpeg-v1", "openai/clip-vit-base-patch32",
            new string('a', 64), "clip-image-preprocessing-v1")
        {
            CaptionModelId = "qwen2.5vl:3b",
            CaptionModelSha256 = new string('b', 64),
            CaptionPromptVersion = "ollama-caption-v1"
        };
        return new VideoFrameEvidenceBatch(p,
            Plan.Samples.Select((f, i) =>
                new VideoFrameEvidenceItem(f.TimestampMilliseconds, f.SceneIndex,
                    "Synthetic fixture " + i + ".",
                    new float[] { 0.2f, -0.5f, 0.7f })).ToArray());
    }

    [Fact]
    public void Float_vectors_have_exact_portable_little_endian_bytes()
    {
        byte[] bytes = PostgresVideoFrameEvidenceRepository.EncodeVector(
            new float[] { 1.0f, -2.0f });
        Assert.Equal(new byte[] { 0, 0, 128, 63, 0, 0, 0, 192 }, bytes);
        Assert.Equal(new float[] { 1.0f, -2.0f },
            PostgresVideoFrameEvidenceRepository.DecodeVector(bytes, 2));
        Assert.Throws<ArgumentException>(() =>
            PostgresVideoFrameEvidenceRepository.EncodeVector([float.NaN]));
        Assert.Throws<ArgumentException>(() =>
            PostgresVideoFrameEvidenceRepository.DecodeVector(bytes, 1));
    }

    [Fact]
    public async Task Complete_publication_is_atomic_and_currentness_gated()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_frame_evidence_" + Guid.NewGuid().ToString("N");
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
            var config = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = name, Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(config.ConnectionString);
            await database.InitializeAsync();
            await database.InitializeAsync();
            var queue = new PostgresVideoProcessingJobRepository(database);
            var repository = new PostgresVideoFrameEvidenceRepository(database);
            var budget = new VideoProcessingBudget(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 5000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 4);
            var candidate = new VideoProcessingCandidate(
                true, false, true, true, 4000, 16000, 2);

            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(), revision = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-frame-asset',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'synthetic.mp4',now(),'video');
                    INSERT INTO asset_revisions(
                        id,asset_id,content_sha256,size_bytes,observed_at_utc,
                        media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),4000,
                        now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(
                        asset_revision_id,inspection_status,duration_seconds,
                        color_transfer,inspected_at_utc,tool_sha256,
                        inspection_profile_id)
                    VALUES (@revision,'supported',8,'bt709',now(),repeat('d',64),'synthetic');
                    UPDATE video_processing_control
                    SET enabled=true,paused=false WHERE singleton_id=1;
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                await seed.ExecuteNonQueryAsync();
            }

            Task<Guid> Enqueue(string tool) => queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(AssetRevisionId.From(revision),
                    VideoDerivativeKind.FrameEvidence, Plan.PolicyVersion,
                    "wi0190-sdr-frame-jpeg-v1", tool),
                budget, candidate);
            Guid jobId = await Enqueue(new string('c', 64));
            VideoProcessingJobLease lease = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));

            VideoFrameEvidenceBatch batch = Sample(revision);
            // Forged lease budgets cannot turn a failed execution into success.
            Assert.False(await repository.TryPublishAsync(
                lease with { TemporaryBytes = lease.TemporaryBytes + 1 }, Plan, batch));
            Assert.Equal("running", (await queue.GetAsync(jobId))?.Status);
            Assert.Null(await repository.GetCurrentAsync(jobId));

            // A partial batch cannot publish even with a valid live lease.
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.TryPublishAsync(lease, Plan,
                    batch with { Items = batch.Items.Take(1).ToArray() }));
            Assert.Null(await repository.GetCurrentAsync(jobId));

            Assert.True(await repository.TryPublishAsync(lease, Plan, batch));
            Assert.Equal("succeeded", (await queue.GetAsync(jobId))?.Status);
            VideoFrameEvidenceBatch stored = Assert.IsType<VideoFrameEvidenceBatch>(
                await repository.GetCurrentAsync(jobId));
            Assert.Equal(batch.Provenance, stored.Provenance);
            Assert.Equal(batch.Items.Count, stored.Items.Count);
            Assert.Equal(batch.Items[0].TimestampMilliseconds,
                stored.Items[0].TimestampMilliseconds);
            Assert.Equal(batch.Items[0].Caption, stored.Items[0].Caption);
            Assert.Equal(batch.Items[0].Embedding, stored.Items[0].Embedding);

            // No second commit may reuse an acknowledged, consumed lease.
            Assert.False(await repository.TryPublishAsync(lease, Plan, batch));

            // A control pause/generation update defeats another live job.
            Guid pausedId = await Enqueue(new string('e', 64));
            VideoProcessingJobLease paused = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));
            await new PostgresVideoProcessingControlRepository(database).PauseAsync();
            Assert.False(await repository.TryPublishAsync(paused, Plan, batch));
            Assert.Null(await repository.GetCurrentAsync(pausedId));

            // A source-copy exclusion immediately hides already published rows,
            // without a destructive mutation to the source or frame evidence.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = connection.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions(
                        source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'synthetic.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            Assert.Null(await repository.GetCurrentAsync(jobId));
            // Source-copy exclusion must physically purge private captions
            // and embeddings through v38 FK cascade, not only hide them.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand count = connection.CreateCommand())
            {
                count.CommandText = """
                    SELECT
                      (SELECT COUNT(*) FROM video_frame_evidence_batches),
                      (SELECT COUNT(*) FROM video_frame_evidence_frames);
                    """;
                await using NpgsqlDataReader reader = await count.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
            }
        }
        finally
        {
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
