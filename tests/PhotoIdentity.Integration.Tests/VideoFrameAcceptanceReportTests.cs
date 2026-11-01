using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoFrameAcceptanceReportTests
{
    [Fact]
    public void Parser_and_private_annotations_refuse_unknown_duplicates_and_unsafe_inputs()
    {
        Guid job = Guid.NewGuid();
        var good = new VideoFrameAcceptanceReportManifest(
            VideoFrameAcceptanceEvidence.Protocol,
            [new(job, ["short", "multi-scene"], [3000L],
                1500, null, null, null, 2, 1)]);
        VideoFrameAcceptanceReportCommandRunner.ValidateManifest(good);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceReportCommandRunner.ValidateManifest(good with
                { Cases = [good.Cases[0], good.Cases[0]] }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceReportCommandRunner.ValidateManifest(good with
                { Cases = [good.Cases[0] with { SceneCutsMs = [3000L, 3000L] }] }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceReportCommandRunner.ValidateManifest(good with
                { Cases = [good.Cases[0] with { Categories = ["short", "short"] }] }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceReportCommandRunner.ValidateManifest(good with
                { Cases = [good.Cases[0] with { CpuTimeMs = -1 }] }));
        string path = Path.Combine(Path.GetTempPath(), "private-acceptance.json");
        string[] args = [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST_CONN",
            "--database", "photoidentity_test",
            "--manifest", path
        ];
        Assert.Equal(path, VideoFrameAcceptanceReportOptions.Parse(args).ManifestPath);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceReportOptions.Parse([.. args, "--apply", "true"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceReportOptions.Parse([.. args, "--manifest", path]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceReportOptions.Parse(
                ["--postgres-connection-env", "INVALID-NAME",
                 "--database", "photoidentity_test", "--manifest", path]));
    }

    [Fact]
    public async Task Malformed_private_manifest_does_not_echo_media_or_paths()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "pi-acceptance-private-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "secret-fixture.json");
        try
        {
            await File.WriteAllTextAsync(file,
                "{ \"personalVideoName\": \"PERSONAL_MOVIE_NAME.mov\" }");
            using StringWriter output = new();
            var options = new VideoFrameAcceptanceReportOptions(
                "NOT_CONFIGURED", "photoidentity_test", file);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                VideoFrameAcceptanceReportCommandRunner.RunAsync(options, output));
            Assert.DoesNotContain("PERSONAL_MOVIE_NAME", error.Message);
            Assert.DoesNotContain(file, error.Message);
            Assert.Empty(output.ToString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Read_only_report_accepts_current_batch_and_rejects_stale_and_excluded()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string name = "photoidentity_acceptance_" + Guid.NewGuid().ToString("N");
        string env = "PHOTOIDENTITY_ACCEPTANCE_TEST_" + Guid.NewGuid().ToString("N");
        string? old = Environment.GetEnvironmentVariable(env);
        await using NpgsqlConnection administrator = new(
            new NpgsqlConnectionStringBuilder(admin)
                { Pooling = false }.ConnectionString);
        await administrator.OpenAsync();
        await using (NpgsqlCommand create = administrator.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync();
        }
        string dir = Path.Combine(Path.GetTempPath(),
            "pi-acceptance-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string manifest = Path.Combine(dir, "private-cases.json");
        try
        {
            var config = new NpgsqlConnectionStringBuilder(admin)
                { Database = name, Pooling = false };
            Environment.SetEnvironmentVariable(env, config.ConnectionString);
            await using PostgresCatalogueDatabase db = new(config.ConnectionString);
            await db.InitializeAsync();
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(),
                revision = Guid.NewGuid(), job = Guid.NewGuid();
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand seed = conn.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','sensitive-local-source',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'PERSONAL_NAME.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,
                        size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),4000,
                        now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,duration_seconds,color_transfer,
                        inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',8,'bt709',now(),repeat('a',64),'fixture');
                    INSERT INTO video_processing_jobs(id,asset_revision_id,
                        idempotency_key,derivative_kind,policy_version,
                        preset_version,tool_sha256,source_bytes,
                        reserved_temporary_bytes,requested_frames,status,
                        progress_percent,completed_at_utc)
                    VALUES (@job,@revision,repeat('b',64),'FrameEvidence',
                        'video-frame-sampling-v1','wi0190-sdr-frame-jpeg-v1',
                        repeat('c',64),4000,20000,2,'succeeded',100,now());
                    INSERT INTO video_frame_evidence_batches(job_id,asset_revision_id,
                        sampling_policy_version,extraction_profile_version,
                        clip_model_id,clip_model_sha256,clip_preprocessing_version,
                        caption_model_id,caption_model_sha256,
                        caption_prompt_version,frame_count,embedding_dimensions)
                    VALUES (@job,@revision,'video-frame-sampling-v1',
                        'wi0190-sdr-frame-jpeg-v1','synthetic-model',
                        repeat('d',64),'synthetic-preprocess',
                        'synthetic-caption',repeat('e',64),'synthetic-prompt',2,3);
                    INSERT INTO video_frame_evidence_frames(job_id,ordinal,
                        timestamp_ms,caption,embedding_le_f32)
                    VALUES (@job,0,2000,'PRIVATE HUMAN CAPTION ONE',
                        decode('0000803f0000004000004040','hex')),
                           (@job,1,6000,'PRIVATE HUMAN CAPTION TWO',
                        decode('0000803f0000004000004040','hex'));
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                seed.Parameters.AddWithValue("job", job);
                await seed.ExecuteNonQueryAsync();
            }
            var observed = new VideoFrameAcceptanceReportManifest(
                VideoFrameAcceptanceEvidence.Protocol,
                [new(job, ["short", "multi-scene"], [3000L, 5000L],
                    1234, 240, null, null, 2, 1)]);
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(observed,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var options = new VideoFrameAcceptanceReportOptions(env, name, manifest);
            async Task<(int Exit, string Json)> Report()
            {
                using StringWriter writer = new();
                int exit = await VideoFrameAcceptanceReportCommandRunner.RunAsync(options, writer);
                return (exit, writer.ToString());
            }
            var success = await Report();
            Assert.Equal(0, success.Exit);
            using (JsonDocument parsed = JsonDocument.Parse(success.Json))
            {
                var root = parsed.RootElement;
                Assert.True(root.GetProperty("readOnly").GetBoolean());
                Assert.Equal(1, root.GetProperty("evidence")
                    .GetProperty("clips").GetInt32());
                Assert.Equal(3, root.GetProperty("evidence")
                    .GetProperty("annotatedScenes").GetInt32());
                Assert.Equal(2, root.GetProperty("evidence")
                    .GetProperty("coveredScenes").GetInt32());
                Assert.Equal(1, root.GetProperty("evidence")
                    .GetProperty("cpuMeasuredClips").GetInt32());
                Assert.False(root.GetProperty("evidence")
                    .GetProperty("fullAcceptanceEstablished").GetBoolean());
                Assert.DoesNotContain("PRIVATE HUMAN", success.Json);
                Assert.DoesNotContain("PERSONAL_NAME", success.Json);
                Assert.DoesNotContain(job.ToString(), success.Json);
                Assert.DoesNotContain("sensitive-local-source", success.Json);
            }

            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand mutate = conn.CreateCommand())
            {
                mutate.CommandText = """
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@newer,@asset,repeat('f',64),5000,
                        now()+interval '1 minute','video/mp4','video');
                    """;
                mutate.Parameters.AddWithValue("newer", Guid.NewGuid());
                mutate.Parameters.AddWithValue("asset", asset);
                await mutate.ExecuteNonQueryAsync();
            }
            using (StringWriter failureOutput = new())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    VideoFrameAcceptanceReportCommandRunner.RunAsync(options, failureOutput));
                Assert.DoesNotContain("PERSONAL_NAME", error.Message);
                Assert.Empty(failureOutput.ToString());
            }
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = conn.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions(source_id,source_key,
                        excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'PERSONAL_NAME.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            using StringWriter excludedOutput = new();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                VideoFrameAcceptanceReportCommandRunner.RunAsync(options, excludedOutput));
            Assert.Empty(excludedOutput.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, old);
            Directory.Delete(dir, true);
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
