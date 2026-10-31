using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Disposable PostgreSQL integration. A synthetic producer returns in-memory
/// fixture vectors and captions; NO FFmpeg, Ollama, Azure, private file or
/// unattended media worker is involved.
/// </summary>
public sealed class VideoFrameEvidenceWorkerAdapterTests
{
    private static readonly VideoFrameSamplingPlan Plan = VideoFrameSamplingPlanner.Plan(
        new VideoFrameSamplingRequest(8m, MaximumFrames: 2));

    private static VideoFrameEvidenceProduction Good(Guid revision)
    {
        var provenance = new VideoFrameEvidenceProvenance(
            AssetRevisionId.From(revision), Plan.PolicyVersion,
            VideoFrameExtractionProfile.Version,
            "openai/clip-vit-base-patch32", new string('a', 64),
            "clip-image-preprocessing-v1")
        {
            CaptionModelId = "qwen2.5vl:3b",
            CaptionModelSha256 = new string('b', 64),
            CaptionPromptVersion = "ollama-caption-v1"
        };
        return new(Plan, new(provenance,
            Plan.Samples.Select((f, i) => new VideoFrameEvidenceItem(
                f.TimestampMilliseconds, f.SceneIndex,
                "Synthetic frame " + i + ".",
                new float[] { 0.1f, 0.4f, -0.6f })).ToArray()));
    }

    [Fact]
    public async Task Frame_worker_claims_only_frame_jobs_and_publishes_fenced_full_batch()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string dbName = "photoidentity_frame_cycle_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection administrator = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await administrator.OpenAsync();
        await using (NpgsqlCommand create = administrator.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{dbName}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using PostgresCatalogueDatabase database = new(
                new NpgsqlConnectionStringBuilder(admin)
                { Database = dbName, Pooling = false }.ConnectionString);
            await database.InitializeAsync();
            var queue = new PostgresVideoProcessingJobRepository(database);
            var coordinator = new VideoWorkerAttemptCoordinator(database);
            var reader = new PostgresVideoFrameEvidenceRepository(database);
            var controls = new PostgresVideoProcessingControlRepository(database);

            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(), revision = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand insert = connection.CreateCommand())
            {
                insert.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','frame-cycle-test',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'synthetic.mp4',now(),'video');
                    INSERT INTO asset_revisions(
                        id,asset_id,content_sha256,size_bytes,observed_at_utc,
                        media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),4000,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(
                        asset_revision_id,inspection_status,duration_seconds,
                        color_transfer,inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',8,'bt709',now(),repeat('c',64),'synthetic');
                    UPDATE video_processing_control
                    SET enabled=true,paused=false WHERE singleton_id=1;
                    """;
                insert.Parameters.AddWithValue("source", source);
                insert.Parameters.AddWithValue("asset", asset);
                insert.Parameters.AddWithValue("revision", revision);
                await insert.ExecuteNonQueryAsync();
            }

            var budget = new VideoProcessingBudget(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 5000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 4);
            var candidate = new VideoProcessingCandidate(
                true, false, true, true, 4000, 16000, 2);
            Task<Guid> Enqueue(VideoDerivativeKind kind, char hash) =>
                queue.EnqueueAsync(new VideoDerivativeJobIdentity(
                    AssetRevisionId.From(revision), kind,
                    kind == VideoDerivativeKind.FrameEvidence
                        ? VideoFrameSamplingPlanner.PolicyVersion : "proxy-policy",
                    kind == VideoDerivativeKind.FrameEvidence
                        ? VideoFrameExtractionProfile.Version : "proxy-preset",
                    new string(hash, 64)), budget, candidate);

            // The older queued proxy must NEVER be claimed by a frame adapter.
            Guid proxyJob = await Enqueue(VideoDerivativeKind.PlayableProxy, 'd');
            Guid frameJob = await Enqueue(VideoDerivativeKind.FrameEvidence, 'e');
            int invocations = 0;
            var adapter = new VideoFrameEvidenceWorkerAdapter(database,
                new SyntheticProducer((lease, _) =>
                {
                    invocations++;
                    Assert.Equal(frameJob, lease.Id);
                    Assert.Equal("FrameEvidence", lease.DerivativeKind);
                    return Task.FromResult(Good(revision));
                }));
            VideoWorkerCycleResult success = await coordinator.RunOneAsync(budget, adapter);
            Assert.True(success.Claimed);
            Assert.True(success.Published);
            Assert.Equal("published", success.ReasonCode);
            Assert.Equal(1, invocations);
            Assert.Equal("queued", (await queue.GetAsync(proxyJob))?.Status);
            Assert.Equal("succeeded", (await queue.GetAsync(frameJob))?.Status);
            VideoFrameEvidenceBatch published = Assert.IsType<VideoFrameEvidenceBatch>(
                await reader.GetCurrentAsync(frameJob));
            Assert.Equal(2, published.Items.Count);
            Assert.Equal("Synthetic frame 0.", published.Items[0].Caption);

            // No frame lease exists, so the worker will NOT take the proxy.
            VideoWorkerCycleResult none = await coordinator.RunOneAsync(budget, adapter);
            Assert.False(none.Claimed);
            Assert.Equal("no-admissible-job", none.ReasonCode);

            // A partial returned batch must not reach the PG publication path.
            Guid invalidJob = await Enqueue(VideoDerivativeKind.FrameEvidence, 'f');
            var partialAdapter = new VideoFrameEvidenceWorkerAdapter(database,
                new SyntheticProducer((_, _) =>
                {
                    VideoFrameEvidenceProduction valid = Good(revision);
                    return Task.FromResult(valid with
                    {
                        Batch = valid.Batch with { Items = valid.Batch.Items.Take(1).ToArray() }
                    });
                }));
            VideoWorkerCycleResult invalid = await coordinator.RunOneAsync(
                budget, partialAdapter);
            Assert.True(invalid.Claimed);
            Assert.False(invalid.Published);
            Assert.Equal("failed", (await queue.GetAsync(invalidJob))?.Status);
            Assert.Null(await reader.GetCurrentAsync(invalidJob));

            // A synthetically claimed success with NO atomic receipt is
            // rejected by the coordinator, not trusted as published.
            Guid fakeJob = await Enqueue(VideoDerivativeKind.FrameEvidence, '1');
            var lyingAdapter = new SyntheticClaimAdapter();
            VideoWorkerCycleResult forged = await coordinator.RunOneAsync(
                budget, lyingAdapter);
            Assert.True(forged.Claimed);
            Assert.False(forged.Published);
            Assert.Equal("publication-required", forged.ReasonCode);
            Assert.Equal("failed", (await queue.GetAsync(fakeJob))?.Status);
            Assert.Null(await reader.GetCurrentAsync(fakeJob));

            // Revocation during production prohibits publication and must
            // not manufacture completed evidence or job success.
            Guid revokedJob = await Enqueue(VideoDerivativeKind.FrameEvidence, '2');
            var revokedAdapter = new VideoFrameEvidenceWorkerAdapter(database,
                new SyntheticProducer(async (_, _) =>
                {
                    await controls.PauseAsync();
                    return Good(revision);
                }));
            VideoWorkerCycleResult revoked = await coordinator.RunOneAsync(
                budget, revokedAdapter);
            Assert.True(revoked.Claimed);
            Assert.False(revoked.Published);
            Assert.NotEqual("succeeded", (await queue.GetAsync(revokedJob))?.Status);
            Assert.Null(await reader.GetCurrentAsync(revokedJob));

            // A cancellation requested while the producer is running must
            // suppress the complete synthetic output and never publish a
            // completed FrameEvidence receipt.
            await controls.ResumeAsync();
            Guid cancelledJob = await Enqueue(VideoDerivativeKind.FrameEvidence, '3');
            var cancelledAdapter = new VideoFrameEvidenceWorkerAdapter(database,
                new SyntheticProducer(async (lease, _) =>
                {
                    Assert.Equal(cancelledJob, lease.Id);
                    Assert.True(await queue.RequestCancellationAsync(lease.Id));
                    return Good(revision);
                }));
            VideoWorkerCycleResult cancelled = await coordinator.RunOneAsync(
                budget, cancelledAdapter);
            Assert.True(cancelled.Claimed);
            Assert.False(cancelled.Published);
            Assert.NotEqual("succeeded", (await queue.GetAsync(cancelledJob))?.Status);
            Assert.Null(await reader.GetCurrentAsync(cancelledJob));

            // A new photo sync request arising while synthetic frame
            // production is in flight must preempt publication immediately,
            // independent of the 10-second heartbeat interval.
            Guid photoPreemptedJob = await Enqueue(VideoDerivativeKind.FrameEvidence, '4');
            var preemptedAdapter = new VideoFrameEvidenceWorkerAdapter(database,
                new SyntheticProducer(async (lease, _) =>
                {
                    Assert.Equal(photoPreemptedJob, lease.Id);
                    await using NpgsqlConnection connection =
                        await database.OpenConnectionAsync();
                    await using NpgsqlCommand activate = connection.CreateCommand();
                    activate.CommandText = """
                        INSERT INTO archive_advancement_control(
                            source_id,desired_state,runtime_state,
                            sync_required,updated_at_utc)
                        VALUES (@source,'running','video-processing',true,now());
                        """;
                    activate.Parameters.AddWithValue("source", source);
                    Assert.Equal(1, await activate.ExecuteNonQueryAsync());
                    return Good(revision);
                }));
            VideoWorkerCycleResult preempted = await coordinator.RunOneAsync(
                budget, preemptedAdapter);
            Assert.True(preempted.Claimed);
            Assert.False(preempted.Published);
            Assert.NotEqual("succeeded", (await queue.GetAsync(photoPreemptedJob))?.Status);
            Assert.Null(await reader.GetCurrentAsync(photoPreemptedJob));
            Assert.True((await new PostgresVideoPhotoPriorityProbe(database)
                .GetAsync()).PhotoWorkActive);

            // A committed caption/vector is physically purged by an
            // exclusion, rather than merely excluded from current reads.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = connection.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions (
                        source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'synthetic.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            Assert.Null(await reader.GetCurrentAsync(frameJob));
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand count = connection.CreateCommand())
            {
                count.CommandText = """
                    SELECT COUNT(*) FROM video_frame_evidence_batches;
                    """;
                Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class SyntheticProducer(
        Func<VideoProcessingJobLease, CancellationToken,
            Task<VideoFrameEvidenceProduction>> execute) : IVideoFrameEvidenceProducer
    {
        public Task<VideoFrameEvidenceProduction> ProduceAsync(
            VideoProcessingJobLease lease, CancellationToken token) => execute(lease, token);
    }

    private sealed class SyntheticClaimAdapter : IVideoPublishedDerivativeAdapter
    {
        public string? RequiredDerivativeKind => "FrameEvidence";
        public Task<VideoDerivativeAttemptReport> ProcessAsync(
            VideoProcessingJobLease lease, CancellationToken token) =>
            Task.FromResult(new VideoDerivativeAttemptReport(true, false));
    }
}
