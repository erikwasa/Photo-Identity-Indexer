using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Testing.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoRec709RealSourcePilotTests
{
    private static string[] Options() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_LOCAL_POSTGRES",
        "--database", "photoidentity_main",
        "--revision-id", "12345678-1234-4234-8234-123456789abc",
        "--scratch-root", Path.Combine(Path.GetTempPath(), "wi0210-real"),
        "--ffmpeg", Path.Combine(Path.GetTempPath(), "ffmpeg.exe"),
        "--ffmpeg-sha256", new string('a', 64),
        "--ffprobe", Path.Combine(Path.GetTempPath(), "ffprobe.exe"),
        "--ffprobe-sha256", new string('b', 64)
    ];

    [Fact]
    public void No_real_media_read_without_all_three_explicit_permissions()
    {
        VideoRec709RealSourcePilotOptions preview =
            VideoRec709RealSourcePilotOptions.Parse(Options());
        Assert.False(preview.Apply);
        Assert.False(preview.ReadLocalBytes);
        Assert.False(preview.RetainReviewProxy);
        VideoRec709RealSourcePilotOptions explicitlyApproved =
            VideoRec709RealSourcePilotOptions.Parse(
                [.. Options(), "--apply", "--read-local-bytes",
                 "--retain-review-proxy"]);
        Assert.True(explicitlyApproved.Apply);
        Assert.True(explicitlyApproved.ReadLocalBytes);
        Assert.True(explicitlyApproved.RetainReviewProxy);

        foreach (string[] unexpected in new[]
        {
            new[] { "--apply" },
            new[] { "--read-local-bytes" },
            new[] { "--retain-review-proxy" },
            new[] { "--apply", "--read-local-bytes" },
            new[] { "--apply", "--retain-review-proxy" },
            new[] { "--read-local-bytes", "--retain-review-proxy" },
            new[] { "--apply", "--apply" },
            new[] { "--apply", "--read-local-bytes",
                "--retain-review-proxy", "--apply" },
            new[] { "--apply", "--read-local-bytes",
                "--retain-review-proxy", "--source-path", "private.mov" },
            new[] { "--apply", "--read-local-bytes",
                "--retain-review-proxy", "--publish" },
            new[] { "--profile", "standard-720p" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoRec709RealSourcePilotOptions.Parse(
                    [.. Options(), .. unexpected]));
    }

    [Fact]
    public void Real_media_scratch_requires_an_empty_marked_system_temp_child()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "wi0210-real-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                VideoRec709RealSourcePilotCommandRunner.ValidateRoot(root));
            File.WriteAllText(Path.Combine(root,
                VideoRec709RealSourcePilotCommandRunner.RootMarker),
                VideoRec709RealSourcePilotCommandRunner.MarkerText);
            Assert.Equal(Path.GetFullPath(root),
                VideoRec709RealSourcePilotCommandRunner.ValidateRoot(root));
            File.WriteAllText(Path.Combine(root, "private.mov"), "not allowed");
            Assert.Throws<InvalidOperationException>(() =>
                VideoRec709RealSourcePilotCommandRunner.ValidateRoot(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Preview_does_not_connect_to_Postgres_or_open_originals_or_tools()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "wi0210-real-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,
                VideoRec709RealSourcePilotCommandRunner.RootMarker),
                VideoRec709RealSourcePilotCommandRunner.MarkerText);
            string[] args = Options();
            args[Array.IndexOf(args, "--scratch-root") + 1] = root;
            VideoRec709RealSourcePilotOptions preview =
                VideoRec709RealSourcePilotOptions.Parse(args);
            using StringWriter writer = new();
            Assert.Equal(0,
                await VideoRec709RealSourcePilotCommandRunner.RunAsync(
                    preview, writer));
            using JsonDocument parsed = JsonDocument.Parse(writer.ToString());
            JsonElement result = parsed.RootElement;
            Assert.Equal("not-run", result.GetProperty("status").GetString());
            Assert.True(result.GetProperty("dryRun").GetBoolean());
            Assert.False(result.GetProperty("originalBytesOpened").GetBoolean());
            Assert.False(result.GetProperty("inputFullRangeProven").GetBoolean());
            Assert.False(result.GetProperty("outputLimitedRangeValidated").GetBoolean());
            Assert.False(result.GetProperty("sourceIntegrityRechecked").GetBoolean());
            Assert.False(result.GetProperty("reviewProxyRetained").GetBoolean());
            Assert.False(result.GetProperty("oneDriveHydrationRequested").GetBoolean());
            Assert.False(result.GetProperty("databaseWriteRequested").GetBoolean());
            Assert.False(result.GetProperty("jobsEnqueued").GetBoolean());
            Assert.False(result.GetProperty("realVideoProductionAuthorized").GetBoolean());
            Assert.False(File.Exists(Path.Combine(root,
                VideoRec709RealSourcePilotCommandRunner.ReviewFileName)));
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Staged_input_probe_is_file_only_and_private_output_is_fixed()
    {
        string input = Path.Combine(Path.GetTempPath(), "verified.mov");
        string scratch = Path.Combine(Path.GetTempPath(), "private-probe");
        IReadOnlyList<string> args =
            VideoRec709RealSourcePilotCommandRunner.BuildInputProbeArgs(input, scratch);
        Assert.Contains("-protocol_whitelist", args);
        Assert.Contains("file", args);
        Assert.Contains("-show_entries", args);
        Assert.Contains("-o", args);
        Assert.Contains(Path.Combine(scratch, "probe.json"), args);
        Assert.Equal(input, args.Last());
        Assert.DoesNotContain("http", args);
        Assert.DoesNotContain("https", args);
        Assert.DoesNotContain("pipe", args);
    }

    private static VideoRevisionInspection Inspection(int rotation, int width,
        int height, string? audio = "aac") => new(
            AssetRevisionId.From(Guid.NewGuid()), "supported", null, null,
            "h264", null, audio, "yuvj420p", "bt709", width, height,
            rotation, 3m, null, null, null, null, null, null, null,
            DateTimeOffset.UtcNow, new string('0', 64),
            VideoRec709FullRangeEvaluationProfile.Version);

    [Fact]
    public void Output_must_preserve_display_orientation_audio_and_duration()
    {
        Assert.True(VideoRec709RealSourcePilotCommandRunner.MatchesExpectedOutput(
            Inspection(0, 1920, 1080), new(720, 406, true, 3)));
        Assert.True(VideoRec709RealSourcePilotCommandRunner.MatchesExpectedOutput(
            Inspection(90, 1920, 1080), new(406, 720, true, 3)));
        Assert.True(VideoRec709RealSourcePilotCommandRunner.MatchesExpectedOutput(
            Inspection(0, 1080, 1920, audio: null), new(406, 720, false, 3)));
        Assert.False(VideoRec709RealSourcePilotCommandRunner.MatchesExpectedOutput(
            Inspection(90, 1920, 1080), new(720, 406, true, 3)));
        Assert.False(VideoRec709RealSourcePilotCommandRunner.MatchesExpectedOutput(
            Inspection(0, 1920, 1080), new(720, 406, false, 3)));
        Assert.False(VideoRec709RealSourcePilotCommandRunner.MatchesExpectedOutput(
            Inspection(0, 1920, 1080), new(720, 406, true, 5)));
    }

    [Fact]
    public async Task Disposable_postgres_exact_selection_rejects_wrong_and_stale_revisions()
    {
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING"))) return;

        await using PostgresTestDatabaseLease lease = PostgresTestDatabaseLease.Create();
        await using PostgresCatalogueDatabase db = new(lease.ConnectionString);
        await db.InitializeAsync();
        Guid source = Guid.NewGuid(), valid = Guid.NewGuid(),
            hdr = Guid.NewGuid(), excluded = Guid.NewGuid(), stale = Guid.NewGuid();
        Guid revision = Guid.NewGuid(), hdrRevision = Guid.NewGuid(),
            excludedRevision = Guid.NewGuid(), staleRevision = Guid.NewGuid(),
            newerRevision = Guid.NewGuid();
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync())
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO sources(id,kind,root_locator,created_at_utc)
                VALUES (@source,'local-folder',@root,now());
                INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                VALUES (@valid,@source,'a.mov',now(),'video'),
                       (@hdr,@source,'b.mov',now(),'video'),
                       (@excluded,@source,'c.mov',now(),'video'),
                       (@stale,@source,'d.mov',now(),'video');
                INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                    observed_at_utc,media_type,media_kind)
                VALUES (@revision,@valid,repeat('a',64),4096,now(),'video/quicktime','video'),
                       (@hdrRevision,@hdr,repeat('b',64),4096,now(),'video/quicktime','video'),
                       (@excludedRevision,@excluded,repeat('c',64),4096,now(),'video/quicktime','video'),
                       (@staleRevision,@stale,repeat('d',64),4096,now()-interval '1 minute','video/quicktime','video'),
                       (@newerRevision,@stale,repeat('e',64),4096,now(),'video/quicktime','video');
                INSERT INTO video_revision_metadata(asset_revision_id,inspection_status,
                    video_codec,pixel_format,color_transfer,audio_codec,coded_width,
                    coded_height,display_rotation_degrees,duration_seconds,inspected_at_utc,
                    tool_sha256,inspection_profile_id)
                VALUES (@revision,'supported','h264','yuvj420p','bt709','aac',
                           1920,1080,0,3,now(),repeat('f',64),'fixture'),
                       (@hdrRevision,'supported','h264','yuvj420p','arib-std-b67','aac',
                           1920,1080,0,3,now(),repeat('f',64),'fixture'),
                       (@excludedRevision,'supported','h264','yuvj420p','bt709','aac',
                           1920,1080,0,3,now(),repeat('f',64),'fixture'),
                       (@staleRevision,'supported','h264','yuvj420p','bt709','aac',
                           1920,1080,0,3,now(),repeat('f',64),'fixture'),
                       (@newerRevision,'supported','h264','yuvj420p','bt709','aac',
                           1920,1080,0,3,now(),repeat('f',64),'fixture');
                INSERT INTO source_copy_exclusions(source_id,source_key,excluded_at_utc,
                    purge_state,purge_updated_at_utc)
                VALUES (@source,'c.mov',now(),'completed',now());
                """;
            command.Parameters.AddWithValue("source", source);
            command.Parameters.AddWithValue("root", Path.GetTempPath());
            command.Parameters.AddWithValue("valid", valid);
            command.Parameters.AddWithValue("hdr", hdr);
            command.Parameters.AddWithValue("excluded", excluded);
            command.Parameters.AddWithValue("stale", stale);
            command.Parameters.AddWithValue("revision", revision);
            command.Parameters.AddWithValue("hdrRevision", hdrRevision);
            command.Parameters.AddWithValue("excludedRevision", excludedRevision);
            command.Parameters.AddWithValue("staleRevision", staleRevision);
            command.Parameters.AddWithValue("newerRevision", newerRevision);
            await command.ExecuteNonQueryAsync();
        }

        VideoRec709SelectedSource? selected =
            await VideoRec709RealSourcePilotCommandRunner.ReadSelectedAsync(
                db, revision, CancellationToken.None);
        Assert.NotNull(selected);
        Assert.Equal(revision, selected.Inspection.RevisionId.Value);
        Assert.Equal(4096L, selected.Bytes);
        foreach (Guid denied in new[] { hdrRevision, excludedRevision, staleRevision })
            Assert.Null(await VideoRec709RealSourcePilotCommandRunner.ReadSelectedAsync(
                db, denied, CancellationToken.None));

        await using NpgsqlConnection verify = await db.OpenConnectionAsync();
        await using NpgsqlCommand counts = verify.CreateCommand();
        counts.CommandText = """
            SELECT (SELECT count(*) FROM video_processing_jobs),
                   (SELECT count(*) FROM video_published_derivatives);
            """;
        await using NpgsqlDataReader data = await counts.ExecuteReaderAsync();
        Assert.True(await data.ReadAsync());
        Assert.Equal(0L, data.GetInt64(0));
        Assert.Equal(0L, data.GetInt64(1));
    }
}
