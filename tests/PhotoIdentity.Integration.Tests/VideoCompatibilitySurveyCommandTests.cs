using System.Text.Json;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Testing.Postgres;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoCompatibilitySurveyCommandTests
{
    private static string[] Args() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_LOCAL_POSTGRES",
        "--database", "photoidentity_catalogue"
    ];

    private static VideoCompatibilityObservation Row(
        bool excluded = false, string? transfer = "bt709",
        long bytes = 10 * 1024 * 1024, decimal? duration = 12,
        string? availability = "local", bool poster = false,
        bool proxy = false, bool frame = false) =>
        new(excluded, "local-folder", availability, "supported", "mov,mp4,m4a,3gp,3g2,mj2",
            "h264", "yuv420p", transfer, "aac", 1920, 1080, 0,
            duration, bytes, poster, proxy, frame);

    [Fact]
    public void Survey_rejects_any_write_media_or_hydration_switch()
    {
        var options = VideoCompatibilitySurveyOptions.Parse(Args());
        Assert.Equal("PHOTOIDENTITY_LOCAL_POSTGRES", options.ConnectionEnv);
        Assert.Equal("photoidentity_catalogue", options.Database);
        foreach (string[] invalid in new[]
        {
            new[] { "--apply" }, new[] { "--include-paths" },
            new[] { "--ffprobe", "ffprobe.exe" },
            new[] { "--source", @"C:\Archive" },
            new[] { "--revision-id", Guid.NewGuid().ToString() },
            new[] { "--database", "other" },
            new[] { "--postgres-connection-env", "OTHER" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoCompatibilitySurveyOptions.Parse([.. Args(), .. invalid]));
        Assert.Throws<ArgumentException>(() =>
            VideoCompatibilitySurveyOptions.Parse([]));
        Assert.Throws<ArgumentException>(() =>
            VideoCompatibilitySurveyOptions.Parse(
                ["--postgres-connection-env", "BAD;NAME", "--database", "valid"]));
    }

    [Fact]
    public void Worker_gate_identifies_exact_current_metadata_boundaries()
    {
        Assert.Equal("metadata-eligible", VideoCompatibilitySurvey.WorkerGate(Row()));
        Assert.Equal("transfer", VideoCompatibilitySurvey.WorkerGate(Row(transfer: "arib-std-b67")));
        Assert.Equal("transfer", VideoCompatibilitySurvey.WorkerGate(Row(transfer: null)));
        Assert.Equal("source-size", VideoCompatibilitySurvey.WorkerGate(
            Row(bytes: 256L * 1024 * 1024 + 1)));
        Assert.Equal("duration", VideoCompatibilitySurvey.WorkerGate(Row(duration: 601)));
        Assert.Equal("metadata-eligible", VideoCompatibilitySurvey.WorkerGate(
            Row(availability: "online-only"))); // eligibility != live file presence
    }

    [Fact]
    public void Output_is_aggregated_stage_specific_and_excludes_source_identifiers()
    {
        VideoCompatibilitySurvey survey = new();
        survey.Add(Row(poster: true, proxy: true, frame: true));
        survey.Add(Row(transfer: "arib-std-b67", bytes: 400L * 1024 * 1024,
            availability: "online-only"));
        survey.Add(Row(excluded: true, transfer: "smpte2084"));
        using StringWriter output = new();
        survey.Write(output);
        string json = output.ToString();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("wi0210-compatibility-inventory-v1", root.GetProperty("protocol").GetString());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.False(root.GetProperty("originalBytesOpened").GetBoolean());
        Assert.False(root.GetProperty("filesystemAvailabilityChecked").GetBoolean());
        Assert.False(root.GetProperty("oneDriveHydrationRequested").GetBoolean());
        Assert.False(root.GetProperty("jobsEnqueued").GetBoolean());
        Assert.Equal(2, root.GetProperty("currentNonExcludedVideoCount").GetInt64());
        Assert.Equal(1, root.GetProperty("excludedCurrentVideoCount").GetInt64());
        Assert.Contains(root.GetProperty("dimensions").EnumerateArray(),
            item => item.GetProperty("dimension").GetString() == "last-recorded-availability" &&
                item.GetProperty("bucket").GetString() == "online-only" &&
                item.GetProperty("count").GetInt64() == 1);
        Assert.Contains(root.GetProperty("stages").EnumerateArray(),
            item => item.GetProperty("stage").GetString() == "playable-proxy-receipt" &&
                item.GetProperty("state").GetString() == "registered" &&
                item.GetProperty("count").GetInt64() == 1);
        Assert.Contains(root.GetProperty("overlappingCandidateGaps").EnumerateArray(),
            item => item.GetProperty("reason").GetString() == "source-over-256mib" &&
                item.GetProperty("affected").GetInt64() == 1);
        Assert.Contains(root.GetProperty("overlappingCandidateGaps").EnumerateArray(),
            item => item.GetProperty("reason").GetString() == "non-bt709-or-unknown-transfer" &&
                item.GetProperty("affected").GetInt64() == 1);
        foreach (string forbidden in new[]
        {
            "sourceKey", "rootLocator", "fileName", "revisionId", "assetId",
            "captureAtUtc", "gpsLatitude", "gpsLongitude", "cameraMake",
            "cameraModel", "caption", "embedding", "person", "connectionString"
        })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_archive_produces_zero_counts_without_division_or_probe()
    {
        using StringWriter output = new();
        new VideoCompatibilitySurvey().Write(output);
        using JsonDocument parsed = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, parsed.RootElement.GetProperty("currentNonExcludedVideoCount").GetInt64());
        Assert.Empty(parsed.RootElement.GetProperty("overlappingCandidateGaps").EnumerateArray());
    }

    [Fact]
    public async Task Disposable_postgres_census_uses_latest_nonexcluded_video_and_never_writes()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING"))) return;

        await using PostgresTestDatabaseLease lease = PostgresTestDatabaseLease.Create();
        await using PostgresCatalogueDatabase database = new(lease.ConnectionString);
        await database.InitializeAsync();

        Guid source = Guid.NewGuid(), video = Guid.NewGuid(),
            excluded = Guid.NewGuid(), photo = Guid.NewGuid();
        Guid stale = Guid.NewGuid(), current = Guid.NewGuid(),
            excludedRevision = Guid.NewGuid(), photoRevision = Guid.NewGuid();
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO sources(id,kind,root_locator,created_at_utc)
                VALUES (@source,'local-folder','wi0210-fixture',now());
                INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                VALUES (@video,@source,'video.mov',now(),'video'),
                       (@excluded,@source,'excluded.mov',now(),'video'),
                       (@photo,@source,'photo.jpg',now(),'photo');
                INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                    observed_at_utc,media_type,media_kind)
                VALUES (@stale,@video,repeat('a',64),10000,now()-interval '1 day','video/mp4','video'),
                       (@current,@video,repeat('b',64),314572800,now(),'video/mp4','video'),
                       (@excluded_revision,@excluded,repeat('c',64),10000,now(),'video/mp4','video'),
                       (@photo_revision,@photo,repeat('d',64),10000,now(),'image/jpeg','photo');
                INSERT INTO video_revision_metadata(asset_revision_id,inspection_status,
                    video_codec,pixel_format,color_transfer,audio_codec,coded_width,
                    coded_height,display_rotation_degrees,duration_seconds,inspected_at_utc,
                    tool_sha256,inspection_profile_id)
                VALUES (@stale,'supported','h264','yuv420p','bt709','aac',1920,1080,0,10,now(),repeat('e',64),'fixture'),
                       (@current,'supported','h264','yuv420p','arib-std-b67','aac',1920,1080,0,10,now(),repeat('e',64),'fixture'),
                       (@excluded_revision,'supported','h264','yuv420p','bt709','aac',1920,1080,0,10,now(),repeat('e',64),'fixture');
                INSERT INTO archive_asset_availability(asset_id,availability,checked_at_utc)
                VALUES (@video,'online-only',now());
                INSERT INTO source_copy_exclusions(source_id,source_key,excluded_at_utc,
                    purge_state,purge_updated_at_utc)
                VALUES (@source,'excluded.mov',now(),'completed',now());
                """;
            command.Parameters.AddWithValue("source", source);
            command.Parameters.AddWithValue("video", video);
            command.Parameters.AddWithValue("excluded", excluded);
            command.Parameters.AddWithValue("photo", photo);
            command.Parameters.AddWithValue("stale", stale);
            command.Parameters.AddWithValue("current", current);
            command.Parameters.AddWithValue("excluded_revision", excludedRevision);
            command.Parameters.AddWithValue("photo_revision", photoRevision);
            await command.ExecuteNonQueryAsync();
        }

        string variable = "PHOTOIDENTITY_WI0210_TEST_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        try
        {
            Environment.SetEnvironmentVariable(variable, lease.ConnectionString);
            var options = new VideoCompatibilitySurveyOptions(variable, lease.DatabaseName);
            using StringWriter output = new();
            Assert.Equal(0, await VideoCompatibilitySurveyCommandRunner.RunAsync(options, output));

            using JsonDocument parsed = JsonDocument.Parse(output.ToString());
            JsonElement root = parsed.RootElement;
            Assert.Equal(1L, root.GetProperty("currentNonExcludedVideoCount").GetInt64());
            Assert.Equal(1L, root.GetProperty("excludedCurrentVideoCount").GetInt64());
            Assert.Contains(root.GetProperty("overlappingCandidateGaps").EnumerateArray(),
                x => x.GetProperty("reason").GetString() == "source-over-256mib" &&
                     x.GetProperty("affected").GetInt64() == 1);
            Assert.Contains(root.GetProperty("overlappingCandidateGaps").EnumerateArray(),
                x => x.GetProperty("reason").GetString() == "non-bt709-or-unknown-transfer" &&
                     x.GetProperty("affected").GetInt64() == 1);
            Assert.Contains(root.GetProperty("dimensions").EnumerateArray(),
                x => x.GetProperty("dimension").GetString() == "last-recorded-availability" &&
                     x.GetProperty("bucket").GetString() == "online-only" &&
                     x.GetProperty("count").GetInt64() == 1);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }

        await using NpgsqlConnection check = await database.OpenConnectionAsync();
        await using NpgsqlCommand counts = check.CreateCommand();
        counts.CommandText = """
            SELECT (SELECT count(*) FROM assets),
                   (SELECT count(*) FROM video_processing_jobs),
                   (SELECT count(*) FROM video_still_posters),
                   (SELECT count(*) FROM video_frame_evidence_batches);
            """;
        await using NpgsqlDataReader read = await counts.ExecuteReaderAsync();
        Assert.True(await read.ReadAsync());
        Assert.Equal(3L, read.GetInt64(0));
        Assert.Equal(0L, read.GetInt64(1));
        Assert.Equal(0L, read.GetInt64(2));
        Assert.Equal(0L, read.GetInt64(3));
    }

}
