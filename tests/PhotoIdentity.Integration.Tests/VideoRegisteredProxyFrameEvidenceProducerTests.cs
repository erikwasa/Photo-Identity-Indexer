using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoRegisteredProxyFrameEvidenceProducerTests
{
    [Fact]
    public void Frame_scratch_requires_dedicated_owned_empty_OS_temp_root()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                VideoFrameEvidenceScratchGuard.Validate(root));
            string marker = Path.Combine(root, VideoFrameEvidenceScratchGuard.MarkerName);
            File.WriteAllText(marker, "not-owned");
            Assert.Throws<InvalidOperationException>(() =>
                VideoFrameEvidenceScratchGuard.Validate(root));
            File.WriteAllText(marker, VideoFrameEvidenceScratchGuard.MarkerContents);
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                VideoFrameEvidenceScratchGuard.Validate(root));

            string foreign = Path.Combine(root, "private-existing-image.jpg");
            File.WriteAllText(foreign, "never-delete-me");
            Assert.Throws<InvalidOperationException>(() =>
                VideoFrameEvidenceScratchGuard.Validate(root));
            Assert.Equal("never-delete-me", File.ReadAllText(foreign));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Forged_or_unapproved_frame_lease_refuses_before_media_and_postgres_access()
    {
        string local = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fixture"));
        VideoRegisteredProxyFrameProducerOptions options = new(
            local, local, Path.Combine(local, "ffmpeg.exe"), new string('a', 64),
            Path.Combine(local, "model.onnx"), Path.Combine(local, "vocab.json"),
            Path.Combine(local, "merges.txt"),
            ExpectedClipSha256: new string('c', 64),
            ExpectedOllamaSha256: new string('d', 64));
        options.Validate();
        Assert.Throws<ArgumentException>(() =>
            (options with { FfmpegSha256 = "not-approved" }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (options with { CacheRoot = "relative" }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (options with { ExpectedOllamaSha256 = "bad" }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (options with { ExpectedClipSha256 = "" }).Validate());

        await using PostgresCatalogueDatabase db = new(
            "Host=127.0.0.1;Port=1;Database=never_connect;Username=fixture;Pooling=false");
        var producer = new VideoRegisteredProxyFrameEvidenceProducer(db, options);
        VideoProcessingJobLease lease = new(
            Guid.NewGuid(), AssetRevisionId.From(Guid.NewGuid()), Guid.NewGuid(),
            "FrameEvidence", VideoFrameSamplingPlanner.PolicyVersion,
            VideoFrameExtractionProfile.Version, new string('b', 64),
            1000, 16000, 2, 1);
        // Even with a bogus DB, no model or file may be consulted when
        // a lease is for the wrong pinned FFmpeg digest.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            producer.ProduceAsync(lease, CancellationToken.None));
    }

    [Fact]
    public async Task Only_current_local_published_proxy_is_selected_for_exact_revision()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string name = "photoidentity_frame_proxy_selection_" + Guid.NewGuid().ToString("N");
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
            await using PostgresCatalogueDatabase db = new(
                new NpgsqlConnectionStringBuilder(admin)
                { Database = name, Pooling = false }.ConnectionString);
            await db.InitializeAsync();
            var proxy = new PostgresVideoFrameProxyPilotSource(db);
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(), revision = Guid.NewGuid();
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand seed = conn.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-source',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'fixture.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,
                        size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),4000,
                        now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,duration_seconds,color_transfer,
                        inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',10,'bt709',now(),
                        repeat('a',64),'fixture');
                    UPDATE video_processing_control
                    SET enabled=true,paused=false WHERE singleton_id=1;
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                await seed.ExecuteNonQueryAsync();
            }
            Assert.Null(await proxy.FindCurrentProxyJobAsync(revision));

            var budget = new VideoProcessingBudget(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 5000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 4);
            var candidate = new VideoProcessingCandidate(
                true, false, true, true, 4000, 16000, 1);
            var queue = new PostgresVideoProcessingJobRepository(db);
            Guid proxyId = await queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(AssetRevisionId.From(revision),
                    VideoDerivativeKind.PlayableProxy, "proxy-policy", "proxy-preset",
                    new string('b', 64)), budget, candidate);
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand publish = conn.CreateCommand())
            {
                publish.CommandText = """
                    UPDATE video_processing_jobs
                    SET status='succeeded',progress_percent=100,
                        completed_at_utc=now(),updated_at_utc=now()
                    WHERE id=@job;
                    INSERT INTO video_published_derivatives(job_id,asset_revision_id,
                        artifact_relative_path,content_sha256,encoded_bytes)
                    VALUES (@job,@revision,@path,repeat('c',64),12345);
                    """;
                publish.Parameters.AddWithValue("job", proxyId);
                publish.Parameters.AddWithValue("revision", revision);
                publish.Parameters.AddWithValue("path",
                    "video-derivatives/" + proxyId.ToString("N") + "/" +
                    Guid.NewGuid().ToString("N") + ".mp4");
                await publish.ExecuteNonQueryAsync();
            }
            Assert.Equal(proxyId, await proxy.FindCurrentProxyJobAsync(revision));
            Assert.Null(await proxy.FindCurrentProxyJobAsync(Guid.NewGuid()));

            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand exclusion = conn.CreateCommand())
            {
                exclusion.CommandText = """
                    INSERT INTO source_copy_exclusions(source_id,source_key,
                        excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'fixture.mp4',now(),'pending',now());
                    """;
                exclusion.Parameters.AddWithValue("source", source);
                await exclusion.ExecuteNonQueryAsync();
            }
            Assert.Null(await proxy.FindCurrentProxyJobAsync(revision));
        }
        finally
        {
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
