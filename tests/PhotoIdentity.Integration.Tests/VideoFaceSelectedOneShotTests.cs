using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Strict opt-in CLI acceptance. No private sources, real model, FFmpeg
/// execution, OneDrive hydration or production identity change is allowed.
/// </summary>
public sealed class VideoFaceSelectedOneShotTests
{
    private static string[] Valid(string root, string exe) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0155_SELECTED_TEST",
        "--database", "photoidentity_synthetic",
        "--revision-id", "d1289a4a-fb10-4b38-9699-5b9c4c7f5598",
        "--scratch-root", root,
        "--ffmpeg", exe,
        "--ffmpeg-sha256", new string('a', 64),
        "--manifest-directory", root,
        "--model-directory", root,
        "--max-frames", "2"
    ];

    [Fact]
    public void Parser_is_preview_first_and_refuses_silent_real_media_authorization()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        string exe = Path.Combine(root, "ffmpeg.exe");
        var options = VideoFaceSelectedOneShotOptions.Parse(Valid(root, exe));
        Assert.False(options.Apply);
        Assert.False(options.AcknowledgePrivateProcessing);
        Assert.False(options.AcknowledgeModelRights);
        Assert.Equal(2, options.Frames);
        Assert.Equal("photoidentity_synthetic", options.DatabaseName);
        Assert.Equal("wi0155-selected-native-face-one-shot-v1",
            VideoFaceSelectedOneShotCommandRunner.Protocol);
        Assert.Equal(4,
            VideoFaceNativeFrameAnalyzer.MaximumFramesPerAttempt);

        string[] four = Valid(root, exe);
        four[^1] = "4";
        Assert.Equal(4, VideoFaceSelectedOneShotOptions.Parse(four).Frames);
        string[] one = Valid(root, exe);
        one[^1] = "1";
        Assert.Equal(1, VideoFaceSelectedOneShotOptions.Parse(one).Frames);

        Assert.True(VideoFaceSelectedOneShotOptions.Parse(
            [.. Valid(root, exe), "--apply",
             "--acknowledge-private-face-processing", "--acknowledge-model-rights"]).Apply);
        foreach (string[] denied in new[]
        {
            new[] { "--apply" },
            new[] { "--apply", "--acknowledge-model-rights" },
            new[] { "--apply", "--acknowledge-private-face-processing" },
            new[] { "--acknowledge-private-face-processing" },
            new[] { "--acknowledge-model-rights" },
            new[] { "--source", @"C:\private\family.mp4" },
            new[] { "--original-path", @"C:\private\family.mp4" },
            new[] { "--proxy-job-id", Guid.NewGuid().ToString() },
            new[] { "--mode", "automatic" },
            new[] { "--ollama-base-url", "https://example.com" },
            new[] { "--max-frames", "0" },
            new[] { "--max-frames", "5" },
            new[] { "--max-frames", "2" },
            new[] { "--ffmpeg-sha256", "invalid" },
            new[] { "--apply", "--apply" },
            new[] { "--apply", "--acknowledge-private-face-processing",
                    "--acknowledge-model-rights", "--acknowledge-model-rights" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoFaceSelectedOneShotOptions.Parse(
                    [.. Valid(root, exe), .. denied]));
    }

    [Fact]
    public async Task Metadata_preview_never_enqueues_or_arms_and_unavailable_source_apply_fails_closed()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin) || !OperatingSystem.IsWindows())
            return;

        string dbName = "photoidentity_face_selected_" + Guid.NewGuid().ToString("N");
        string scratch = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        File.WriteAllText(
            Path.Combine(scratch, VideoFrameEvidenceScratchGuard.MarkerName),
            VideoFrameEvidenceScratchGuard.MarkerContents);

        string variable = "PHOTOIDENTITY_WI0155_SELECTED_TEST";
        string? old = Environment.GetEnvironmentVariable(variable);
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
            var dbConn = new NpgsqlConnectionStringBuilder(admin)
            { Database = dbName, Pooling = false };
            Environment.SetEnvironmentVariable(variable, dbConn.ConnectionString);
            await using PostgresCatalogueDatabase database = new(dbConn.ConnectionString);
            await database.InitializeAsync();

            Guid sourceId = Guid.NewGuid();
            Guid assetId = Guid.NewGuid();
            Guid revisionId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','C:\\no-such-private-video-source',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'absent.mp4',now(),'video');
                    INSERT INTO asset_revisions(
                        id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),
                            4000,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(
                        asset_revision_id,inspection_status,duration_seconds,
                        color_transfer,coded_width,coded_height,
                        inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',8,'bt709',1920,1080,
                            now(),repeat('b',64),'synthetic');
                    """;
                seed.Parameters.AddWithValue("source", sourceId);
                seed.Parameters.AddWithValue("asset", assetId);
                seed.Parameters.AddWithValue("revision", revisionId);
                await seed.ExecuteNonQueryAsync();
            }

            string executable = typeof(VideoFaceSelectedOneShotTests).Assembly.Location;
            await using FileStream binary = File.OpenRead(executable);
            string digest = Convert.ToHexString(
                await SHA256.HashDataAsync(binary)).ToLowerInvariant();
            string[] args = [
                "--postgres-connection-env", variable, "--database", dbName,
                "--revision-id", revisionId.ToString("D"),
                "--scratch-root", scratch, "--ffmpeg", executable,
                "--ffmpeg-sha256", digest,
                "--manifest-directory", scratch,
                "--model-directory", scratch, "--max-frames", "2"
            ];

            VideoFaceSelectedOneShotOptions preview =
                VideoFaceSelectedOneShotOptions.Parse(args);
            using (StringWriter result = new())
            {
                Assert.Equal(0, await VideoFaceSelectedOneShotCommandRunner
                    .RunAsync(preview, result));
                using JsonDocument json = JsonDocument.Parse(result.ToString());
                JsonElement row = json.RootElement;
                Assert.True(row.GetProperty("dryRun").GetBoolean());
                Assert.True(row.GetProperty("admitted").GetBoolean());
                Assert.False(row.GetProperty("published").GetBoolean());
                Assert.Equal("preflight-only",
                    row.GetProperty("reasonCode").GetString());
                Assert.False(row.GetProperty("originalsMayHaveBeenOpened").GetBoolean());
                Assert.False(row.GetProperty("oneDriveHydrationRequested").GetBoolean());
                Assert.False(row.GetProperty("cloudInferenceRequested").GetBoolean());
                Assert.Equal(0, row.GetProperty("identityAssignmentsMade").GetInt32());
                // No source names, paths or UUIDs appear in the response.
                Assert.DoesNotContain("absent.mp4", result.ToString());
                Assert.DoesNotContain(revisionId.ToString("D"), result.ToString());
                Assert.DoesNotContain(scratch, result.ToString());
            }
            await AssertUnmodifiedAsync(database);

            // Opted-in apply cannot open a missing original or enqueue it.
            var apply = VideoFaceSelectedOneShotOptions.Parse(
                [.. args, "--apply", "--acknowledge-private-face-processing",
                 "--acknowledge-model-rights"]);
            using (StringWriter denied = new())
            {
                Assert.Equal(1, await VideoFaceSelectedOneShotCommandRunner
                    .RunAsync(apply, denied));
                Assert.Contains("\"reasonCode\":\"source-not-local\"",
                    denied.ToString());
            }
            await AssertUnmodifiedAsync(database);

            // Source-exclusion immediately makes even the preview ineligible.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = connection.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions(
                        source_id,source_key,excluded_at_utc,
                        purge_state,purge_updated_at_utc)
                    VALUES (@source,'absent.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", sourceId);
                await exclude.ExecuteNonQueryAsync();
            }
            using (StringWriter excluded = new())
            {
                Assert.Equal(1, await VideoFaceSelectedOneShotCommandRunner
                    .RunAsync(preview, excluded));
                Assert.Contains("\"reasonCode\":\"revision-not-eligible\"",
                    excluded.ToString());
            }
            await AssertUnmodifiedAsync(database);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, old);
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task AssertUnmodifiedAsync(PostgresCatalogueDatabase database)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand check = connection.CreateCommand();
        check.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM video_processing_jobs
                 WHERE derivative_kind='FaceEvidence'),
                (SELECT enabled FROM video_processing_control WHERE singleton_id=1),
                (SELECT selected_job_id IS NULL FROM video_processing_control
                 WHERE singleton_id=1),
                (SELECT COUNT(*) FROM video_face_evidence_batches),
                (SELECT COUNT(*) FROM video_person_actions);
            """;
        await using NpgsqlDataReader reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.False(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal(0L, reader.GetInt64(3));
        Assert.Equal(0L, reader.GetInt64(4));
    }
}
