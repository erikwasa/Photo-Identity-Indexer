using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoFrameEvidenceAuditTests
{
    private static readonly VideoFrameEvidenceAuditSnapshot Complete = new(
        "succeeded", "FrameEvidence", true, 2, 3,
        true, true, true, true, false, 8m, 2,
        24, 34, 2000, 6000, 0, 1, true, true);

    [Fact]
    public void Audit_options_accept_only_exact_opaque_job_and_local_database()
    {
        string[] args = [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST_CONN",
            "--database", "photoidentity_test",
            "--job-id", Guid.NewGuid().ToString()
        ];
        Assert.NotEqual(Guid.Empty, VideoFrameEvidenceAuditOptions.Parse(args).JobId);
        foreach (string[] invalid in new[]
        {
            Array.Empty<string>(),
            new[] { "--apply" },
            [.. args, "--apply"],
            [.. args, "--job-id", Guid.NewGuid().ToString()],
            [.. args.Take(4), "--job-id", "not-a-uuid"],
            [.. args.Take(2), "--database", "unsafe-db",
                .. args.Skip(4)],
            [.. args.Take(2), "--database", "postgres",
                .. args.Skip(4), "--source", "private.mov"]
        })
            Assert.Throws<ArgumentException>(() =>
                VideoFrameEvidenceAuditOptions.Parse(invalid));
    }

    [Fact]
    public void Audit_is_fail_closed_for_stale_excluded_partial_or_unsupported_data()
    {
        Assert.Equal("current-complete",
            VideoFrameEvidenceAuditPolicy.Classify(Complete));
        Assert.Equal("job-not-found",
            VideoFrameEvidenceAuditPolicy.Classify(null));
        Assert.Equal("source-excluded",
            VideoFrameEvidenceAuditPolicy.Classify(Complete with { Excluded = true }));
        Assert.Equal("stale-revision",
            VideoFrameEvidenceAuditPolicy.Classify(Complete with { LatestRevision = false }));
        Assert.Equal("unsupported-media",
            VideoFrameEvidenceAuditPolicy.Classify(Complete with { SupportedSdr = false }));
        Assert.Equal("batch-not-published",
            VideoFrameEvidenceAuditPolicy.Classify(Complete with { BatchExists = false }));
        Assert.Equal("job-not-succeeded",
            VideoFrameEvidenceAuditPolicy.Classify(Complete with { JobStatus = "running" }));
        Assert.Equal("wrong-job-kind",
            VideoFrameEvidenceAuditPolicy.Classify(Complete with { DerivativeKind = "PlayableProxy" }));
        foreach (VideoFrameEvidenceAuditSnapshot bad in new[]
        {
            Complete with { FrameRows = 1 },
            Complete with { Dimensions = 4 },
            Complete with { VectorBytes = 20 },
            Complete with { FirstOrdinal = 1 },
            Complete with { LastOrdinal = 2 },
            Complete with { IncreasingTimestamps = false },
            Complete with { ValidVectorLengths = false },
            Complete with { LastTimestampMs = 8000 },
            Complete with { CaptionBytes = 0 },
            Complete with { MatchesJobRevision = false }
        })
            Assert.Equal("incomplete-or-invalid",
                VideoFrameEvidenceAuditPolicy.Classify(bad));
    }

    [Fact]
    public async Task Read_only_audit_tracks_current_revision_and_exclusion_purge()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string name = "photoidentity_frame_audit_" + Guid.NewGuid().ToString("N");
        string env = "PHOTOIDENTITY_WI0190_FRAME_AUDIT_TEST";
        string? previous = Environment.GetEnvironmentVariable(env);
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
            Environment.SetEnvironmentVariable(env, config.ConnectionString);
            await using PostgresCatalogueDatabase database = new(config.ConnectionString);
            await database.InitializeAsync();
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(),
                revision = Guid.NewGuid(), job = Guid.NewGuid();

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-audit-root',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'audit-fixture.mp4',now(),'video');
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
                    VALUES (@job,0,2000,'Synthetic frame one.',
                        decode('0000803f0000004000004040','hex')),
                           (@job,1,6000,'Synthetic frame two.',
                        decode('0000803f0000004000004040','hex'));
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                seed.Parameters.AddWithValue("job", job);
                await seed.ExecuteNonQueryAsync();
            }

            VideoFrameEvidenceAuditOptions options = new(env, name, job);
            async Task<(int ExitCode, JsonDocument Report)> Audit()
            {
                using StringWriter writer = new();
                int exit = await VideoFrameEvidenceAuditCommandRunner.RunAsync(
                    options, writer);
                return (exit, JsonDocument.Parse(writer.ToString()));
            }

            async Task<(int ExitCode, JsonDocument Report)> Coverage(string cuts)
            {
                using StringWriter writer = new();
                int exit = await VideoFrameSceneCoverageCommandRunner.RunAsync(
                    new VideoFrameSceneCoverageOptions(options,
                        VideoFrameSceneCoverageOptions.Parse([
                            "--postgres-connection-env", env,
                            "--database", name, "--job-id", job.ToString(),
                            "--scene-boundaries-ms", cuts
                        ]).SceneCutsMs),
                    writer);
                return (exit, JsonDocument.Parse(writer.ToString()));
            }

            var first = await Audit();
            using (first.Report)
            {
                Assert.Equal(0, first.ExitCode);
                Assert.True(first.Report.RootElement.GetProperty("current").GetBoolean());
                Assert.Equal("current-complete",
                    first.Report.RootElement.GetProperty("reasonCode").GetString());
                Assert.Equal(2,
                    first.Report.RootElement.GetProperty("durableFrames").GetInt32());
                Assert.Equal(24,
                    first.Report.RootElement.GetProperty("embeddingBytes").GetInt64());
                Assert.DoesNotContain("Synthetic frame", first.Report.RootElement.ToString());
                Assert.DoesNotContain("audit-fixture", first.Report.RootElement.ToString());
            }

            var currentCoverage = await Coverage("3000,5000,7000");
            using (currentCoverage.Report)
            {
                Assert.Equal(0, currentCoverage.ExitCode);
                var report = currentCoverage.Report.RootElement;
                Assert.True(report.GetProperty("readOnly").GetBoolean());
                Assert.True(report.GetProperty("current").GetBoolean());
                Assert.Equal(4, report.GetProperty("actual")
                    .GetProperty("totalScenes").GetInt32());
                Assert.Equal(2, report.GetProperty("actual")
                    .GetProperty("missedScenes").GetInt32());
                Assert.Equal(50, report.GetProperty("actual")
                    .GetProperty("coveragePercent").GetDouble());
                Assert.True(report.GetProperty(
                    "observedPositionsMatchIntervalPlan").GetBoolean());
                Assert.False(report.GetProperty("sceneDetectionPerformed").GetBoolean());
                Assert.DoesNotContain("Synthetic frame", report.ToString());
                Assert.DoesNotContain("audit-fixture", report.ToString());
            }

            await using (NpgsqlConnection conn = await database.OpenConnectionAsync())
            await using (NpgsqlCommand revise = conn.CreateCommand())
            {
                revise.CommandText = """
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,
                        size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('f',64),5000,
                        now() + interval '1 minute','video/mp4','video');
                    """;
                revise.Parameters.AddWithValue("revision", Guid.NewGuid());
                revise.Parameters.AddWithValue("asset", asset);
                await revise.ExecuteNonQueryAsync();
            }
            var stale = await Audit();
            using (stale.Report)
            {
                Assert.Equal(1, stale.ExitCode);
                Assert.Equal("stale-revision",
                    stale.Report.RootElement.GetProperty("reasonCode").GetString());
                Assert.False(stale.Report.RootElement.GetProperty("current").GetBoolean());
            }

            var staleCoverage = await Coverage("3000");
            using (staleCoverage.Report)
            {
                Assert.Equal(1, staleCoverage.ExitCode);
                Assert.Equal("stale-revision",
                    staleCoverage.Report.RootElement.GetProperty("reasonCode").GetString());
                Assert.False(staleCoverage.Report.RootElement.GetProperty("current").GetBoolean());
                Assert.False(staleCoverage.Report.RootElement.TryGetProperty("actual", out _));
            }

            await using (NpgsqlConnection conn = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = conn.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions(source_id,source_key,
                        excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'audit-fixture.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            var excluded = await Audit();
            using (excluded.Report)
            {
                Assert.Equal(1, excluded.ExitCode);
                Assert.Equal("source-excluded",
                    excluded.Report.RootElement.GetProperty("reasonCode").GetString());
                Assert.Equal(0, excluded.Report.RootElement.GetProperty("durableFrames").GetInt32());
            }
            var excludedCoverage = await Coverage("3000");
            using (excludedCoverage.Report)
            {
                Assert.Equal(1, excludedCoverage.ExitCode);
                Assert.Equal("source-excluded",
                    excludedCoverage.Report.RootElement.GetProperty("reasonCode").GetString());
                Assert.False(excludedCoverage.Report.RootElement.TryGetProperty("actual", out _));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, previous);
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
