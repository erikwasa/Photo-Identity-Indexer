using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Testing.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoCompatibilityLocalCheckCommandTests
{
    private static VideoCompatibilityObservation Row(string? pixel = "yuvj420p",
        string? transfer = "bt709", bool excluded = false) =>
        new(excluded, "local-folder", null, "supported", "mov,mp4", "h264",
            pixel, transfer, "aac", 1920, 1080, 90, 3m, 4096,
            false, false, false);

    [Fact]
    public void Exact_pixel_only_gate_reuses_current_worker_guards()
    {
        Assert.True(VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(Row()));
        Assert.False(VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(
            Row(pixel: "yuv420p")));
        Assert.False(VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(
            Row(transfer: "arib-std-b67")));
        Assert.False(VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(
            Row(excluded: true)));
        Assert.False(VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(
            Row() with { Rotation = null }));
        Assert.False(VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(
            Row() with { SourceBytes = 256L * 1024 * 1024 + 1 }));
        Assert.False(VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(
            Row() with { SourceKind = "remote" }));
    }

    [Fact]
    public async Task Metadata_only_local_status_never_opens_media_or_recalls_missing_files()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "wi0210-local-attribute-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string video = Path.Combine(root, "test.mov");
            File.WriteAllBytes(video, new byte[4096]);
            Guid source = Guid.NewGuid();
            Assert.Equal("local-metadata-match",
                await VideoCompatibilityLocalCheckCommandRunner.ClassifyAsync(
                    source, root, "test.mov", 4096));
            Assert.Equal("size-mismatch",
                await VideoCompatibilityLocalCheckCommandRunner.ClassifyAsync(
                    source, root, "test.mov", 4097));
            Assert.Equal("unavailable",
                await VideoCompatibilityLocalCheckCommandRunner.ClassifyAsync(
                    source, root, "absent.mov", 4096));
            Assert.Equal("unsafe-source",
                await VideoCompatibilityLocalCheckCommandRunner.ClassifyAsync(
                    source, root, "../escaped.mov", 4096));
            Assert.Equal("unsafe-source",
                await VideoCompatibilityLocalCheckCommandRunner.ClassifyAsync(
                    source, root, "test.jpg", 4096));
            Assert.Equal("unsafe-source",
                await VideoCompatibilityLocalCheckCommandRunner.ClassifyAsync(
                    source, root, "test.mov", 0));
            Assert.Equal("unsafe-source",
                await VideoCompatibilityLocalCheckCommandRunner.ClassifyAsync(
                    Guid.Empty, root, "test.mov", 4096));
            Assert.Equal(4096, new FileInfo(video).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Disposable_postgres_audit_filters_current_excluded_and_nonmatching_rows()
    {
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING"))) return;

        string root = Path.Combine(Path.GetTempPath(),
            "wi0210-local-audit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "fullrange.mov"), new byte[4096]);
            await using PostgresTestDatabaseLease lease = PostgresTestDatabaseLease.Create();
            await using PostgresCatalogueDatabase database = new(lease.ConnectionString);
            await database.InitializeAsync();

            Guid source = Guid.NewGuid();
            Guid full = Guid.NewGuid(), hdr = Guid.NewGuid(), excluded = Guid.NewGuid();
            Guid revision = Guid.NewGuid(), hdrRevision = Guid.NewGuid(),
                excludedRevision = Guid.NewGuid();
            await using (NpgsqlConnection conn = await database.OpenConnectionAsync())
            await using (NpgsqlCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder',@root,now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@full,@source,'fullrange.mov',now(),'video'),
                           (@hdr,@source,'hdr.mov',now(),'video'),
                           (@excluded,@source,'excluded.mov',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@full,repeat('a',64),4096,now(),'video/quicktime','video'),
                           (@hdrRevision,@hdr,repeat('b',64),4096,now(),'video/quicktime','video'),
                           (@excludedRevision,@excluded,repeat('c',64),4096,now(),'video/quicktime','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,inspection_status,
                        video_codec,pixel_format,color_transfer,audio_codec,coded_width,
                        coded_height,display_rotation_degrees,duration_seconds,inspected_at_utc,
                        tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported','h264','yuvj420p','bt709','aac',
                            1920,1080,90,3,now(),repeat('d',64),'fixture'),
                           (@hdrRevision,'supported','h264','yuvj420p','arib-std-b67','aac',
                            1920,1080,90,3,now(),repeat('d',64),'fixture'),
                           (@excludedRevision,'supported','h264','yuvj420p','bt709','aac',
                            1920,1080,90,3,now(),repeat('d',64),'fixture');
                    INSERT INTO source_copy_exclusions(source_id,source_key,excluded_at_utc,
                        purge_state,purge_updated_at_utc)
                    VALUES (@source,'excluded.mov',now(),'completed',now());
                    """;
                cmd.Parameters.AddWithValue("source", source);
                cmd.Parameters.AddWithValue("root", root);
                cmd.Parameters.AddWithValue("full", full);
                cmd.Parameters.AddWithValue("hdr", hdr);
                cmd.Parameters.AddWithValue("excluded", excluded);
                cmd.Parameters.AddWithValue("revision", revision);
                cmd.Parameters.AddWithValue("hdrRevision", hdrRevision);
                cmd.Parameters.AddWithValue("excludedRevision", excludedRevision);
                await cmd.ExecuteNonQueryAsync();
            }
            string envName = "PHOTOIDENTITY_WI0210_LOCAL_" +
                Guid.NewGuid().ToString("N").ToUpperInvariant();
            try
            {
                Environment.SetEnvironmentVariable(envName, lease.ConnectionString);
                using StringWriter output = new();
                Assert.Equal(0, await VideoCompatibilityLocalCheckCommandRunner.RunAsync(
                    new VideoCompatibilitySurveyOptions(envName, lease.DatabaseName), output));
                Assert.DoesNotContain(root, output.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("fullrange.mov", output.ToString(),
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(revision.ToString(), output.ToString(),
                    StringComparison.OrdinalIgnoreCase);
                using JsonDocument doc = JsonDocument.Parse(output.ToString());
                JsonElement json = doc.RootElement;
                Assert.True(json.GetProperty("readOnly").GetBoolean());
                Assert.True(json.GetProperty("filesystemMetadataChecked").GetBoolean());
                Assert.False(json.GetProperty("sourceContentOpened").GetBoolean());
                Assert.False(json.GetProperty("oneDriveHydrationRequested").GetBoolean());
                Assert.False(json.GetProperty("videoJobsEnqueued").GetBoolean());
                Assert.Equal(1, json.GetProperty("candidateCount").GetInt32());
                Assert.Single(json.GetProperty("availability").EnumerateArray());
                Assert.Equal("local-metadata-match", json.GetProperty("availability")[0]
                    .GetProperty("status").GetString());
                Assert.Equal(1, json.GetProperty("availability")[0]
                    .GetProperty("count").GetInt32());
            }
            finally { Environment.SetEnvironmentVariable(envName, null); }

            // Neither the source nor the database was changed by this audit.
            Assert.Equal(4096, new FileInfo(Path.Combine(root, "fullrange.mov")).Length);
            await using NpgsqlConnection check = await database.OpenConnectionAsync();
            await using NpgsqlCommand counts = check.CreateCommand();
            counts.CommandText = """
                SELECT (SELECT count(*) FROM assets),
                       (SELECT count(*) FROM video_processing_jobs),
                       (SELECT count(*) FROM video_published_derivatives);
                """;
            await using NpgsqlDataReader result = await counts.ExecuteReaderAsync();
            Assert.True(await result.ReadAsync());
            Assert.Equal(3L, result.GetInt64(0));
            Assert.Equal(0L, result.GetInt64(1));
            Assert.Equal(0L, result.GetInt64(2));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
