using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoFrameSelectedOneShotTests
{
    private static string[] Valid(string root) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0190_CONN",
        "--database", "photoidentity_main",
        "--proxy-job-id", "9d20dafa-282e-4240-914c-4cc5328185a4",
        "--cache-root", root,
        "--scratch-root", root,
        "--ffmpeg", Path.Combine(root, "ffmpeg.exe"),
        "--ffmpeg-sha256", new string('a', 64),
        "--clip-model", Path.Combine(root, "model.onnx"),
        "--clip-model-sha256", new string('b', 64),
        "--clip-vocab", Path.Combine(root, "vocab.json"),
        "--clip-merges", Path.Combine(root, "merges.txt"),
        "--ollama-sha256", new string('c', 64),
        "--max-frames", "2"
    ];

    [Fact]
    public void Default_is_preview_only_and_publication_requires_both_specific_acknowledgements()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "wi0190-test"));
        var options = VideoFrameSelectedOneShotOptions.Parse(Valid(root));
        Assert.False(options.Apply);
        Assert.False(options.AcknowledgeV38);
        Assert.False(options.AcknowledgeDisclosure);
        Assert.Equal(2, options.Frames);
        Assert.Equal(Guid.Parse("9d20dafa-282e-4240-914c-4cc5328185a4"),
            options.Producer.SelectedProxyJobId);
        Assert.Equal(new string('b', 64), options.Producer.ExpectedClipSha256);
        Assert.Equal(new string('c', 64), options.Producer.ExpectedOllamaSha256);
        Assert.True(VideoFrameSelectedOneShotOptions.Parse(
            [.. Valid(root), "--apply",
            "--acknowledge-isolated-v38-rehearsal",
            "--acknowledge-frame-disclosure"]).Apply);

        foreach (string[] forbidden in new[]
        {
            new[] { "--apply" },
            new[] { "--apply", "--acknowledge-isolated-v38-rehearsal" },
            new[] { "--apply", "--acknowledge-frame-disclosure" },
            new[] { "--acknowledge-frame-disclosure" },
            new[] { "--source", @"C:\private\family.mov" },
            new[] { "--original-path", @"C:\private\family.mov" },
            new[] { "--online-only", "yes" },
            new[] { "--max-frames", "3" },
            new[] { "--max-frames", "0" },
            new[] { "--max-frames", "2" },
            new[] { "--ollama-sha256", new string('d', 64) },
            new[] { "--clip-model-sha256", "not-a-hash" },
            new[] { "--apply", "--apply" },
            new[] { "--acknowledge-isolated-v38-rehearsal",
                    "--acknowledge-frame-disclosure" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoFrameSelectedOneShotOptions.Parse(
                    [.. Valid(root), .. forbidden]));
    }

    [Fact]
    public void Selected_frame_scratch_does_not_accept_previous_synthetic_pilot_marker()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,
                VideoSyntheticProxyPilotCommandRunner.RootMarker),
                VideoSyntheticProxyPilotCommandRunner.MarkerText);
            Assert.Throws<InvalidOperationException>(() =>
                VideoFrameEvidenceScratchGuard.Validate(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task Current_proxy_dry_run_does_not_enqueue_or_arm_control()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin) || !OperatingSystem.IsWindows()) return;
        string dbName = "photoidentity_frame_preflight_" + Guid.NewGuid().ToString("N");
        string temp = Path.GetTempPath();
        string cache = Path.Combine(temp, "photoidentity-frame-cache-" +
            Guid.NewGuid().ToString("N"));
        string scratch = Path.Combine(temp, "photoidentity-frame-evidence-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(cache, VideoDerivativeFilePublisher.CacheMarker),
            VideoDerivativeFilePublisher.CacheMarkerContents);
        File.WriteAllText(Path.Combine(scratch, VideoFrameEvidenceScratchGuard.MarkerName),
            VideoFrameEvidenceScratchGuard.MarkerContents);

        string envName = "PHOTOIDENTITY_WI0190_ONE_SHOT_FIXTURE";
        string? previous = Environment.GetEnvironmentVariable(envName);
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
            var config = new NpgsqlConnectionStringBuilder(admin)
            { Database = dbName, Pooling = false };
            Environment.SetEnvironmentVariable(envName, config.ConnectionString);
            await using PostgresCatalogueDatabase db = new(config.ConnectionString);
            await db.InitializeAsync();
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid();
            Guid revision = Guid.NewGuid(), proxyJob = Guid.NewGuid();
            await using (NpgsqlConnection connection = await db.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-root',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'fixture.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,
                        size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),4000,
                        now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,duration_seconds,color_transfer,
                        inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',10,'bt709',now(),repeat('a',64),'fixture');
                    INSERT INTO video_processing_jobs(id,asset_revision_id,
                        idempotency_key,derivative_kind,policy_version,
                        preset_version,tool_sha256,source_bytes,
                        reserved_temporary_bytes,requested_frames,
                        status,progress_percent,completed_at_utc)
                    VALUES (@proxyJob,@revision,repeat('b',64),
                        'PlayableProxy','proxy-policy','proxy-preset',
                        repeat('c',64),4000,20000,1,
                        'succeeded',100,now());
                    INSERT INTO video_published_derivatives(job_id,asset_revision_id,
                        artifact_relative_path,content_sha256,encoded_bytes)
                    VALUES (@proxyJob,@revision,@path,repeat('d',64),12345);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                seed.Parameters.AddWithValue("proxyJob", proxyJob);
                seed.Parameters.AddWithValue("path",
                    "video-derivatives/" + proxyJob.ToString("N") + "/" +
                    Guid.NewGuid().ToString("N") + ".mp4");
                await seed.ExecuteNonQueryAsync();
            }
            string exe = typeof(VideoFrameSelectedOneShotTests).Assembly.Location;
            await using FileStream binary = File.OpenRead(exe);
            string hash = Convert.ToHexString(
                await SHA256.HashDataAsync(binary)).ToLowerInvariant();
            var requested = VideoFrameSelectedOneShotOptions.Parse(
                [
                "--postgres-connection-env", envName,
                "--database", dbName,
                "--proxy-job-id", proxyJob.ToString(),
                "--cache-root", cache, "--scratch-root", scratch,
                "--ffmpeg", exe, "--ffmpeg-sha256", hash,
                "--clip-model", Path.Combine(temp, "unused.onnx"),
                "--clip-model-sha256", new string('b', 64),
                "--clip-vocab", Path.Combine(temp, "unused-vocab.json"),
                "--clip-merges", Path.Combine(temp, "unused-merges.txt"),
                "--ollama-sha256", new string('c', 64),
                "--max-frames", "2"]);
            using StringWriter report = new();
            Assert.Equal(0, await VideoFrameSelectedOneShotCommandRunner
                .RunAsync(requested, report));
            Assert.Contains("\"reasonCode\":\"preflight-only\"", report.ToString());
            Assert.Contains("\"dryRun\":true", report.ToString());
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand verify = conn.CreateCommand())
            {
                verify.CommandText = """
                    SELECT (SELECT COUNT(*) FROM video_processing_jobs
                            WHERE derivative_kind='FrameEvidence'),
                           (SELECT enabled FROM video_processing_control
                            WHERE singleton_id=1),
                           (SELECT selected_job_id IS NULL
                            FROM video_processing_control WHERE singleton_id=1);
                    """;
                await using NpgsqlDataReader reader = await verify.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0L, reader.GetInt64(0));
                Assert.False(reader.GetBoolean(1));
                Assert.True(reader.GetBoolean(2));
            }

            // No FFmpeg/media/model request is permitted for unsupported
            // inspection: the preflight must reject before a job is created.
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand unsupported = conn.CreateCommand())
            {
                unsupported.CommandText = """
                    UPDATE video_revision_metadata
                    SET inspection_status='unsupported'
                    WHERE asset_revision_id=@revision;
                    """;
                unsupported.Parameters.AddWithValue("revision", revision);
                await unsupported.ExecuteNonQueryAsync();
            }
            using (StringWriter rejected = new())
            {
                Assert.Equal(1, await VideoFrameSelectedOneShotCommandRunner
                    .RunAsync(requested, rejected));
                Assert.Contains("\"reasonCode\":\"unsupported-video\"", rejected.ToString());
            }

            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand oversize = conn.CreateCommand())
            {
                oversize.CommandText = """
                    UPDATE video_revision_metadata
                    SET inspection_status='supported'
                    WHERE asset_revision_id=@revision;
                    UPDATE video_published_derivatives
                    SET encoded_bytes=300000000 WHERE job_id=@job;
                    """;
                oversize.Parameters.AddWithValue("revision", revision);
                oversize.Parameters.AddWithValue("job", proxyJob);
                await oversize.ExecuteNonQueryAsync();
            }
            using (StringWriter rejected = new())
            {
                Assert.Equal(1, await VideoFrameSelectedOneShotCommandRunner
                    .RunAsync(requested, rejected));
                Assert.Contains("\"published\":false", rejected.ToString());
            }

            // Exclusions make a registered proxy unavailable before staged
            // copies or disclosure; v38 also purges earlier private evidence.
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = conn.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions(
                        source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'fixture.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            using (StringWriter rejected = new())
            {
                Assert.Equal(1, await VideoFrameSelectedOneShotCommandRunner
                    .RunAsync(requested, rejected));
                Assert.Contains("\"published\":false", rejected.ToString());
            }
            Assert.Equal(new[] { VideoFrameEvidenceScratchGuard.MarkerName },
                Directory.GetFileSystemEntries(scratch).Select(Path.GetFileName).ToArray());
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand verify = conn.CreateCommand())
            {
                verify.CommandText = """
                    SELECT COUNT(*) FROM video_processing_jobs
                    WHERE derivative_kind='FrameEvidence';
                    """;
                Assert.Equal(0L, (long)(await verify.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
            Directory.Delete(cache, recursive: true);
            Directory.Delete(scratch, recursive: true);
        }
    }

}
