using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoLibraryRepositoryTests
{
    [Fact]
    public async Task Current_video_only_page_and_poster_hide_excluded_photo_and_superseded_revisions()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string databaseName = "photoidentity_video_library_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection adminConnection = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await create.ExecuteNonQueryAsync();
        }

        string folder = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-library-" + Guid.NewGuid().ToString("N"));
        string originals = Path.Combine(folder, "originals");
        string derivatives = Path.Combine(folder, "derivatives");
        Directory.CreateDirectory(originals);
        Directory.CreateDirectory(derivatives);
        try
        {
            NpgsqlConnectionStringBuilder connection = new(admin)
            {
                Database = databaseName,
                Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(connection.ConnectionString);
            await database.InitializeAsync();
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(),
                oldRevision = Guid.NewGuid(), currentRevision = Guid.NewGuid(),
                excludedAsset = Guid.NewGuid(), excludedRevision = Guid.NewGuid(),
                photoAsset = Guid.NewGuid(), photoRevision = Guid.NewGuid();
            DateTimeOffset now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
            string tool = new('d', 64);
            string relative = $"video-posters/wi0151-sdr-still-v1/{currentRevision:D}/{tool}.jpg";
            string imagePath = Path.Combine(derivatives,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);

            // Bounded JPEG-like fixture: parser exercises exact binary delivery
            // without depending on a media decoder or user's private originals.
            byte[] jpeg = new byte[128];
            jpeg[0] = 0xff; jpeg[1] = 0xd8;
            jpeg[2] = 0xff; jpeg[3] = 0xc0; jpeg[4] = 0; jpeg[5] = 17;
            jpeg[6] = 8; jpeg[7] = 0x01; jpeg[8] = 0x68;
            jpeg[9] = 0x02; jpeg[10] = 0x80;
            jpeg[^2] = 0xff; jpeg[^1] = 0xd9;
            await File.WriteAllBytesAsync(imagePath, jpeg);
            string checksum = Convert.ToHexString(SHA256.HashData(jpeg)).ToLowerInvariant();

            await using (NpgsqlConnection db = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = db.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', @root, @now);
                    INSERT INTO assets(id, source_id, source_key, media_kind, created_at_utc)
                    VALUES (@asset, @source, 'private/clip.mov', 'video', @now),
                           (@excluded_asset, @source, 'private/hidden.mov', 'video', @now),
                           (@photo_asset, @source, 'private/image.jpg', 'photo', @now);
                    INSERT INTO asset_revisions(id, asset_id, content_sha256,
                        size_bytes, observed_at_utc, media_type, media_kind)
                    VALUES (@old, @asset, repeat('a',64), 100, @old_time, 'video/quicktime', 'video'),
                           (@current, @asset, repeat('b',64), 100, @now, 'video/quicktime', 'video'),
                           (@excluded, @excluded_asset, repeat('c',64), 100, @now, 'video/quicktime', 'video'),
                           (@photo, @photo_asset, repeat('d',64), 100, @now, 'image/jpeg', 'photo');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status, color_transfer, duration_seconds,
                        coded_width, coded_height, inspected_at_utc,
                        tool_sha256, inspection_profile_id)
                    VALUES (@old, 'supported', 'bt709', 11, 640, 360, @now,
                            repeat('e',64), 'wi0151-inspection-v2'),
                           (@current, 'supported', 'bt709', 18, 1280, 720, @now,
                            repeat('e',64), 'wi0151-inspection-v2'),
                           (@excluded, 'supported', 'bt709', 9, 640, 360, @now,
                            repeat('e',64), 'wi0151-inspection-v2');
                    INSERT INTO video_still_posters(asset_revision_id, poster_profile_id,
                        tool_sha256, poster_sha256, poster_relative_path,
                        encoded_bytes, width, height, generated_at_utc)
                    VALUES (@current, 'wi0151-sdr-still-v1', @tool, @checksum, @relative,
                            @bytes, 640, 360, @now);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("root", originals);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("excluded_asset", excludedAsset);
                seed.Parameters.AddWithValue("photo_asset", photoAsset);
                seed.Parameters.AddWithValue("old", oldRevision);
                seed.Parameters.AddWithValue("current", currentRevision);
                seed.Parameters.AddWithValue("excluded", excludedRevision);
                seed.Parameters.AddWithValue("photo", photoRevision);
                seed.Parameters.AddWithValue("now", now);
                seed.Parameters.AddWithValue("old_time", now.AddDays(-1));
                seed.Parameters.AddWithValue("tool", tool);
                seed.Parameters.AddWithValue("checksum", checksum);
                seed.Parameters.AddWithValue("relative", relative);
                seed.Parameters.AddWithValue("bytes", (long)jpeg.Length);
                await seed.ExecuteNonQueryAsync();
            }

            var excluded = new PostgresSourceCopyExclusionRepository(database);
            await excluded.ExcludeAsync(SourceId.From(source), "private/hidden.mov", now.AddMinutes(1));
            var library = new PostgresVideoLibraryRepository(database);
            VideoLibraryPage page = await library.ListAsync(0, 24);
            VideoLibraryEntry video = Assert.Single(page.Items);
            Assert.Equal(AssetRevisionId.From(currentRevision), video.RevisionId);
            Assert.Equal(18m, video.DurationSeconds);
            Assert.True(video.HasPoster);
            Assert.False(page.HasMore);
            Assert.Empty((await library.ListAsync(1, 24)).Items);

            Assert.Null(await library.GetPosterBytesAsync(AssetRevisionId.From(oldRevision), derivatives));
            Assert.Null(await library.GetPosterBytesAsync(AssetRevisionId.From(excludedRevision), derivatives));
            Assert.Null(await library.GetPosterBytesAsync(AssetRevisionId.From(photoRevision), derivatives));
            Assert.Equal(jpeg,
                await library.GetPosterBytesAsync(AssetRevisionId.From(currentRevision), derivatives));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                library.ListAsync(-1, 24).GetAwaiter().GetResult());

            await File.WriteAllBytesAsync(imagePath, new byte[128]);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                library.GetPosterBytesAsync(AssetRevisionId.From(currentRevision), derivatives));

            await excluded.ExcludeAsync(SourceId.From(source), "private/clip.mov", now.AddMinutes(2));
            Assert.Empty((await library.ListAsync(0, 24)).Items);
            Assert.Null(await library.GetPosterBytesAsync(
                AssetRevisionId.From(currentRevision), derivatives));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
