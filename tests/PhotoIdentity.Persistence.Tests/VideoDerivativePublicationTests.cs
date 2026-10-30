using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class VideoDerivativePublicationTests
{
    [Fact]
    public void Artifact_keys_are_exactly_lease_bound_without_paths_or_traversal()
    {
        VideoProcessingJobLease lease = LeaseFixture();
        string expected = VideoDerivativeArtifactPath.ForLease(lease);
        Assert.True(VideoDerivativeArtifactPath.IsCanonical(
            expected, lease.Id, lease.LeaseToken));
        Assert.False(VideoDerivativeArtifactPath.IsCanonical(
            "../" + expected, lease.Id, lease.LeaseToken));
        Assert.False(VideoDerivativeArtifactPath.IsCanonical(
            expected.Replace("/", "\\"), lease.Id, lease.LeaseToken));
        Assert.False(VideoDerivativeArtifactPath.IsCanonical(
            expected, lease.Id, Guid.NewGuid()));
        Assert.True(VideoDerivativeArtifactPath.IsHash(new string('a', 64)));
        Assert.False(VideoDerivativeArtifactPath.IsHash(new string('A', 64)));
        Assert.False(VideoDerivativeArtifactPath.IsHash(new string('a', 63)));
    }

    [Fact]
    public async Task Private_publisher_refuses_unmarked_input_and_wrong_derivative_kind()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-publication-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var publisher = new VideoDerivativeFilePublisher(
                new PostgresCatalogueDatabase(
                    "Host=127.0.0.1;Port=1;Database=do_not_connect;Username=fixture;Pooling=false"));
            VideoProcessingJobLease lease = LeaseFixture();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                publisher.PublishAsync(lease, directory, Path.Combine(directory, "not-owned.mp4")));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                publisher.PublishAsync(lease with { DerivativeKind = "FaceEvidence" },
                    directory, Path.Combine(directory, "output.mp4")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Synthetic_publication_is_atomic_with_job_success_and_denies_revoked_lease()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string dbName = "photoidentity_video_pub_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, VideoDerivativeFilePublisher.CacheMarker),
            VideoDerivativeFilePublisher.CacheMarkerContents);

        await using NpgsqlConnection adminConnection = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{dbName}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var config = new NpgsqlConnectionStringBuilder(admin)
                { Database = dbName, Pooling = false };
            await using PostgresCatalogueDatabase database = new(config.ConnectionString);
            await database.InitializeAsync();
            await database.InitializeAsync();
            var queue = new PostgresVideoProcessingJobRepository(database);
            var control = new PostgresVideoProcessingControlRepository(database);
            var published = new PostgresVideoPublishedDerivativeRepository(database);
            var filePublisher = new VideoDerivativeFilePublisher(database);

            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(), revision = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-video-source',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'test.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),5000,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,inspection_status,
                        duration_seconds,inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',6,now(),repeat('b',64),'synthetic');
                    UPDATE video_processing_control SET enabled=true,paused=false
                    WHERE singleton_id=1;
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                await seed.ExecuteNonQueryAsync();
            }

            VideoProcessingBudget budget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 6000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 4);
            VideoProcessingCandidate candidate = new(true, false, true, true,
                5000, 12000, 2);
            Task<Guid> Enqueue(string preset) => queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(AssetRevisionId.From(revision),
                    VideoDerivativeKind.PlayableProxy, "wi0188-v1", preset,
                    new string('c', 64)), budget, candidate);

            Guid jobId = await Enqueue("synthetic-a");
            VideoProcessingJobLease lease = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));

            // A forged in-memory reservation cannot bypass the immutable
            // source/scratch estimates saved when the job was admitted.
            string forgedStage = VideoScratchOwnership.Create(root, "video-tool");
            string forgedOutput = Path.Combine(forgedStage, "output.mp4");
            await File.WriteAllBytesAsync(forgedOutput, new byte[] { 1, 2, 3, 4 });
            Assert.False(await filePublisher.PublishAsync(
                lease with { TemporaryBytes = lease.TemporaryBytes + 1000 },
                root, forgedOutput));
            Assert.Equal("running", (await queue.GetAsync(jobId))?.Status);
            Assert.Null(await published.GetCurrentAsync(jobId));
            Assert.False(File.Exists(Path.Combine(root,
                VideoDerivativeArtifactPath.ForLease(lease)
                    .Replace('/', Path.DirectorySeparatorChar))));

            string stage = VideoScratchOwnership.Create(root, "video-tool");
            string output = Path.Combine(stage, "output.mp4");
            await File.WriteAllBytesAsync(output, new byte[] { 0, 0, 0, 12, 102, 116, 121, 112, 105, 115, 111, 109 });
            Assert.True(await filePublisher.PublishAsync(lease, root, output));
            Assert.False(File.Exists(output));
            Assert.True(File.Exists(Path.Combine(root,
                VideoDerivativeArtifactPath.ForLease(lease).Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal("succeeded", (await queue.GetAsync(jobId))?.Status);
            Assert.Equal(100, (await queue.GetAsync(jobId))?.ProgressPercent);
            VideoPublishedDerivativeReceipt receipt = Assert.IsType<VideoPublishedDerivativeReceipt>(
                await published.GetCurrentAsync(jobId));
            Assert.Equal(12, receipt.EncodedBytes);
            Assert.Equal(revision, receipt.RevisionId);
            Assert.False(await queue.TryFinishAttemptAsync(
                lease.Id, lease.LeaseToken, succeeded: true, retryable: false));
            Assert.Null(await queue.TryClaimNextAsync(budget)); // no remaining work

            // WI-0189: an opt-in byte cap must reject without committing
            // any receipt, changing job success, or deleting older proxies.
            // Size cap denial happens before moving tool bytes; aggregate
            // cap denial happens under the publication transaction lock.
            Guid quotaId = await Enqueue("synthetic-quota");
            VideoProcessingJobLease quotaLease = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));
            string quotaStage = VideoScratchOwnership.Create(root, "video-tool");
            string quotaOutput = Path.Combine(quotaStage, "output.mp4");
            await File.WriteAllBytesAsync(quotaOutput, new byte[12]);
            await Assert.ThrowsAsync<VideoProxyPublicationQuotaExceededException>(() =>
                filePublisher.PublishAsync(quotaLease, root, quotaOutput,
                    publicationQuota: new VideoProxyRetentionLimits(32, 8)));
            Assert.True(File.Exists(quotaOutput)); // per-file check did not move bytes
            await Assert.ThrowsAsync<VideoProxyPublicationQuotaExceededException>(() =>
                filePublisher.PublishAsync(quotaLease, root, quotaOutput,
                    publicationQuota: new VideoProxyRetentionLimits(16, 16)));
            Assert.False(File.Exists(quotaOutput)); // known-uncommitted bytes reclaimed
            Assert.False(File.Exists(Path.Combine(root,
                VideoDerivativeArtifactPath.ForLease(quotaLease)
                    .Replace('/', Path.DirectorySeparatorChar))));
            Assert.Null(await published.GetCurrentAsync(quotaId));
            Assert.Equal("running", (await queue.GetAsync(quotaId))?.Status);
            Assert.True(File.Exists(Path.Combine(root,
                VideoDerivativeArtifactPath.ForLease(lease)
                    .Replace('/', Path.DirectorySeparatorChar))));

            // A second attempt with sufficient configured budget may commit
            // under the very same current lease. No old copy was evicted.
            string allowedStage = VideoScratchOwnership.Create(root, "video-tool");
            string allowedOutput = Path.Combine(allowedStage, "output.mp4");
            await File.WriteAllBytesAsync(allowedOutput, new byte[12]);
            Assert.True(await filePublisher.PublishAsync(quotaLease, root,
                allowedOutput,
                publicationQuota: new VideoProxyRetentionLimits(32, 16)));
            Assert.NotNull(await published.GetCurrentAsync(quotaId));
            Assert.Equal("succeeded", (await queue.GetAsync(quotaId))?.Status);
            Assert.NotNull(await published.GetCurrentAsync(jobId));

            // An expired/revoked generation may not record success or leave
            // a published file visible, even if fake output bytes exist.
            Guid deniedId = await Enqueue("synthetic-b");
            VideoProcessingJobLease denied = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));
            await control.PauseAsync();
            string deniedStage = VideoScratchOwnership.Create(root, "video-tool");
            string deniedFile = Path.Combine(deniedStage, "output.mp4");
            await File.WriteAllBytesAsync(deniedFile, new byte[] { 1, 2, 3, 4 });
            Assert.False(await filePublisher.PublishAsync(denied, root, deniedFile));
            Assert.Null(await published.GetCurrentAsync(deniedId));
            Assert.False(File.Exists(Path.Combine(root,
                VideoDerivativeArtifactPath.ForLease(denied).Replace('/', Path.DirectorySeparatorChar))));

            // Existing committed evidence is hidden immediately on exclusion;
            // physical retention/eviction still needs its own approved janitor.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = connection.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions
                        (source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'test.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            Assert.Null(await published.GetCurrentAsync(jobId));
        }
        finally
        {
            await using (NpgsqlCommand drop = adminConnection.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
                await drop.ExecuteNonQueryAsync();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static VideoProcessingJobLease LeaseFixture() =>
        new(Guid.NewGuid(), AssetRevisionId.From(Guid.NewGuid()), Guid.NewGuid(),
            "PlayableProxy", "wi0188-v1", "synthetic", new string('a', 64),
            1000, 10000, 2, 0);
}
