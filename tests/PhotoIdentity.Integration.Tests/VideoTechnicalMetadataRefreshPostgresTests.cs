using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Disposable PostgreSQL only; synthetic catalogue rows, no original videos,
/// local source data, OneDrive, or model inference are used by these tests.
/// </summary>
public sealed class VideoTechnicalMetadataRefreshPostgresTests
{
    [Fact]
    public async Task Refresh_fills_missing_values_audits_once_and_preserves_existing_data()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string databaseName = "photoidentity_refresh_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection server = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await server.OpenAsync();
        await using (NpgsqlCommand create = server.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            string connection = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = databaseName,
                Pooling = false
            }.ConnectionString;
            await using PostgresCatalogueDatabase database = new(connection);
            await database.InitializeAsync();
            await database.InitializeAsync();

            Guid sourceId = Guid.NewGuid(), videoId = Guid.NewGuid(),
                revisionGuid = Guid.NewGuid(), photoId = Guid.NewGuid();
            Guid photoRevision = Guid.NewGuid();
            DateTimeOffset now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset captured = new(2024, 3, 21, 11, 12, 25, TimeSpan.Zero);
            string sourceRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "photoidentity-refresh-fixture-" + Guid.NewGuid().ToString("N")));
            string contentHash = new('a', 64);
            string originalTool = new('b', 64);
            string approvedTool = new('c', 64);
            string vendorTool = new('e', 64);

            await using (NpgsqlConnection open = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = open.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources (id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder',@root,@now);
                    INSERT INTO assets (id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@video,@source,'samples/phone.mp4',@now,'video'),
                           (@photo,@source,'family.jpg',@now,'photo');
                    INSERT INTO asset_revisions
                        (id,asset_id,content_sha256,size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@video,@hash,1234,@now,'video/mp4','video'),
                           (@photo_revision,@photo,@photo_hash,300,@now,'image/jpeg','photo');
                    INSERT INTO video_revision_metadata
                        (asset_revision_id,inspection_status,container_name,video_codec,
                         camera_make,camera_model,capture_at_utc,capture_time_provenance,
                         inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported','mov,mp4','h264',
                            'Existing camera maker',NULL,@capture,'format:creation_time',
                            @now,@original_tool,'wi0151-ffprobe-metadata-v2');
                    """;
                seed.Parameters.AddWithValue("source", sourceId);
                seed.Parameters.AddWithValue("root", sourceRoot);
                seed.Parameters.AddWithValue("video", videoId);
                seed.Parameters.AddWithValue("photo", photoId);
                seed.Parameters.AddWithValue("revision", revisionGuid);
                seed.Parameters.AddWithValue("photo_revision", photoRevision);
                seed.Parameters.AddWithValue("now", now);
                seed.Parameters.AddWithValue("capture", captured);
                seed.Parameters.AddWithValue("hash", contentHash);
                seed.Parameters.AddWithValue("photo_hash", new string('d', 64));
                seed.Parameters.AddWithValue("original_tool", originalTool);
                await seed.ExecuteNonQueryAsync();
            }

            AssetRevisionId revision = AssetRevisionId.From(revisionGuid);
            PostgresVideoDetailsRepository annotations = new(database, TimeProvider.System);
            DateTimeOffset corrected = captured.AddDays(1);
            await annotations.SetCaptureDateAsync(revision, corrected, "test");
            PostgresVideoTechnicalMetadataRefreshRepository repository = new(database);
            VideoTechnicalMetadataRefreshTarget target = Assert.IsType<VideoTechnicalMetadataRefreshTarget>(
                await repository.GetCurrentTargetAsync(revision, sourceRoot));
            Assert.Equal(SourceId.From(sourceId), target.SourceId);
            Assert.Equal("samples/phone.mp4", target.SourceKey);
            Assert.Equal(contentHash, target.ContentSha256);
            Assert.Equal(1234, target.SizeBytes);

            // The refresh may populate only absent values, preserving existing
            // camera maker and the immutable source/capture metadata.
            VideoTechnicalMetadataRefreshFacts facts = new(
                null, null, 18_500_000, 44100, 1, "mono",
                approvedTool, "wi0212-ffprobe-metadata-v3", now.AddMinutes(1));
            VideoTechnicalMetadataRefreshFacts vendorFacts = new(
                "Samsung", "SM-G736B", null, null, null, null,
                vendorTool, "wi0212-exiftool-samsung-camera-v1", now.AddMinutes(1));
            Assert.Equal(4, await repository.FillMissingAsync(
                revision, sourceRoot, contentHash, facts));
            Assert.Equal(1, await repository.FillMissingAsync(
                revision, sourceRoot, contentHash, vendorFacts));
            Assert.Equal(0, await repository.FillMissingAsync(
                revision, sourceRoot, contentHash, facts));
            Assert.Equal(0, await repository.FillMissingAsync(
                revision, sourceRoot, contentHash, vendorFacts));
            Assert.Equal(0, await repository.FillMissingAsync(
                revision, sourceRoot, contentHash, facts with
                {
                    CameraModel = "Other Model",
                    VideoBitrateBps = 22_000_000
                }));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.FillMissingAsync(revision, sourceRoot, contentHash,
                    facts with { AudioChannels = 100 }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.FillMissingAsync(revision, sourceRoot, new string('f', 64), facts));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.FillMissingAsync(AssetRevisionId.From(photoRevision),
                    sourceRoot, new string('d', 64), facts));

            await using (NpgsqlConnection inspect = await database.OpenConnectionAsync())
            await using (NpgsqlCommand check = inspect.CreateCommand())
            {
                check.CommandText = """
                    SELECT m.camera_make, m.camera_model, m.video_bitrate_bps,
                           m.audio_sample_rate_hz, m.audio_channels, m.audio_channel_layout,
                           m.capture_at_utc, m.tool_sha256, m.inspection_profile_id,
                           (SELECT COUNT(*) FROM video_metadata_refresh_actions),
                           (SELECT string_agg(changed_fields, '|' ORDER BY id)
                            FROM video_metadata_refresh_actions),
                           (SELECT string_agg(tool_sha256, '|' ORDER BY id)
                            FROM video_metadata_refresh_actions),
                           (SELECT string_agg(inspection_profile_id, '|' ORDER BY id)
                            FROM video_metadata_refresh_actions),
                           (SELECT COUNT(*) FROM asset_revisions WHERE media_kind='photo'),
                           (SELECT COUNT(*) FROM video_capture_date_actions)
                    FROM video_revision_metadata m
                    WHERE m.asset_revision_id = @revision;
                    """;
                check.Parameters.AddWithValue("revision", revisionGuid);
                await using NpgsqlDataReader rows = await check.ExecuteReaderAsync();
                Assert.True(await rows.ReadAsync());
                Assert.Equal("Existing camera maker", rows.GetString(0));
                Assert.Equal("SM-G736B", rows.GetString(1));
                Assert.Equal(18_500_000L, rows.GetInt64(2));
                Assert.Equal(44100, rows.GetInt32(3));
                Assert.Equal(1, rows.GetInt32(4));
                Assert.Equal("mono", rows.GetString(5));
                Assert.Equal(captured, rows.GetFieldValue<DateTimeOffset>(6));
                Assert.Equal(originalTool, rows.GetString(7));
                Assert.Equal("wi0151-ffprobe-metadata-v2", rows.GetString(8));
                Assert.Equal(2L, rows.GetInt64(9));
                Assert.Equal("video_bitrate_bps,audio_sample_rate_hz,audio_channels,audio_channel_layout|camera_model",
                    rows.GetString(10));
                Assert.Equal(approvedTool + "|" + vendorTool, rows.GetString(11));
                Assert.Equal("wi0212-ffprobe-metadata-v3|wi0212-exiftool-samsung-camera-v1",
                    rows.GetString(12));
                Assert.Equal(1L, rows.GetInt64(13));
                Assert.Equal(1L, rows.GetInt64(14));
            }

            Assert.Equal(corrected,
                (await annotations.GetAsync(revision))?.EffectiveCaptureAtUtc);

            // A new revision or explicit exclusion must invalidate any further
            // technical refresh of the historical or deleted source.
            Guid newer = Guid.NewGuid();
            await using (NpgsqlConnection open = await database.OpenConnectionAsync())
            await using (NpgsqlCommand next = open.CreateCommand())
            {
                next.CommandText = """
                    INSERT INTO asset_revisions
                        (id,asset_id,content_sha256,size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@next,@video,@hash,1235,@at,'video/mp4','video');
                    """;
                next.Parameters.AddWithValue("next", newer);
                next.Parameters.AddWithValue("video", videoId);
                next.Parameters.AddWithValue("hash", new string('e', 64));
                next.Parameters.AddWithValue("at", now.AddMinutes(2));
                await next.ExecuteNonQueryAsync();
            }
            Assert.Null(await repository.GetCurrentTargetAsync(revision, sourceRoot));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.FillMissingAsync(revision, sourceRoot, contentHash, facts));
            PostgresSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(SourceId.From(sourceId), "samples/phone.mp4", now.AddMinutes(3));
            Assert.Null(await repository.GetCurrentTargetAsync(
                AssetRevisionId.From(newer), sourceRoot));
        }
        finally
        {
            await using NpgsqlCommand drop = server.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
