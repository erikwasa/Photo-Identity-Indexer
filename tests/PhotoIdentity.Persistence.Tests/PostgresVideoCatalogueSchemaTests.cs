using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoCatalogueSchemaTests
{
    [Fact]
    public async Task Migration_keeps_existing_photos_and_isolates_revision_bound_video_evidence()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_video_schema_" + Guid.NewGuid().ToString("N");
        string quoted = "\"" + name + "\"";
        await using NpgsqlConnection adminConnection = new(new NpgsqlConnectionStringBuilder(admin)
        {
            Pooling = false,
        }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quoted};";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder settings = new(admin)
            {
                Database = name,
                Pooling = false,
            };
            await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
            await database.InitializeAsync();
            await database.InitializeAsync(); // idempotent migration / bootstrap

            Guid source = Guid.NewGuid();
            Guid photo = Guid.NewGuid(), video = Guid.NewGuid();
            Guid photoRevision = Guid.NewGuid(), videoRevision = Guid.NewGuid();
            DateTimeOffset now = new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);
            await using NpgsqlConnection connection = await database.OpenConnectionAsync();

            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources (id,kind,root_locator,created_at_utc)
                        VALUES (@source,'local-folder','test-video-root',@now);
                    INSERT INTO assets (id,source_id,source_key,created_at_utc)
                        VALUES (@photo,@source,'family.jpg',@now);
                    INSERT INTO assets (id,source_id,source_key,created_at_utc,media_kind)
                        VALUES (@video,@source,'sample.MOV',@now,'video');
                    INSERT INTO asset_revisions (id,asset_id,content_sha256,size_bytes,observed_at_utc,media_type)
                        VALUES (@photo_revision,@photo,@photo_hash,5,@now,'image/jpeg');
                    INSERT INTO asset_revisions (id,asset_id,content_sha256,size_bytes,observed_at_utc,media_type,media_kind)
                        VALUES (@video_revision,@video,@video_hash,1024,@now,'video/quicktime','video');
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("photo", photo);
                seed.Parameters.AddWithValue("video", video);
                seed.Parameters.AddWithValue("photo_revision", photoRevision);
                seed.Parameters.AddWithValue("video_revision", videoRevision);
                seed.Parameters.AddWithValue("photo_hash", new string('a', 64));
                seed.Parameters.AddWithValue("video_hash", new string('b', 64));
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            await using (NpgsqlCommand defaults = connection.CreateCommand())
            {
                defaults.CommandText = """
                    SELECT a.media_kind, r.media_kind
                    FROM assets a
                    JOIN asset_revisions r ON r.asset_id = a.id
                    WHERE a.id = @photo;
                    """;
                defaults.Parameters.AddWithValue("photo", photo);
                await using NpgsqlDataReader rows = await defaults.ExecuteReaderAsync();
                Assert.True(await rows.ReadAsync());
                Assert.Equal("photo", rows.GetString(0));
                Assert.Equal("photo", rows.GetString(1));
            }

            await using (NpgsqlCommand metadata = connection.CreateCommand())
            {
                metadata.CommandText = """
                    INSERT INTO video_revision_metadata (
                        asset_revision_id, inspection_status, container_name, video_codec,
                        video_profile, audio_codec, pixel_format, color_transfer,
                        coded_width, coded_height, display_rotation_degrees,
                        duration_seconds, average_frame_rate, inspected_at_utc,
                        tool_sha256, inspection_profile_id)
                    VALUES (
                        @revision, 'supported', 'mov,mp4', 'hevc',
                        'Main 10', 'aac', 'yuv420p10le', 'arib-std-b67',
                        1920, 1080, -90,
                        152.013333, '1821600/30403', @now,
                        @tool_hash, 'wi0151-technical-v1');
                    """;
                metadata.Parameters.AddWithValue("revision", videoRevision);
                metadata.Parameters.AddWithValue("now", now);
                metadata.Parameters.AddWithValue("tool_hash", new string('c', 64));
                await metadata.ExecuteNonQueryAsync();
            }

            await using (NpgsqlCommand poster = connection.CreateCommand())
            {
                poster.CommandText = """
                    INSERT INTO video_still_posters (
                        asset_revision_id, poster_profile_id, tool_sha256,
                        poster_sha256, poster_relative_path, encoded_bytes,
                        width, height, generated_at_utc)
                    VALUES (
                        @revision,'bt709-poster-v1',@tool_hash,
                        @poster_hash,'video-posters/bt709-poster-v1/valid.jpg',
                        4096,720,1280,@now);
                    """;
                poster.Parameters.AddWithValue("revision", videoRevision);
                poster.Parameters.AddWithValue("tool_hash", new string('c', 64));
                poster.Parameters.AddWithValue("poster_hash", new string('d', 64));
                poster.Parameters.AddWithValue("now", now);
                await poster.ExecuteNonQueryAsync();
            }

            await using (NpgsqlCommand wrongPhoto = connection.CreateCommand())
            {
                wrongPhoto.CommandText = """
                    INSERT INTO video_revision_metadata (
                        asset_revision_id, inspection_status, inspected_at_utc,
                        tool_sha256, inspection_profile_id)
                    VALUES (@revision,'unsupported',@now,@hash,'v1');
                    """;
                wrongPhoto.Parameters.AddWithValue("revision", photoRevision);
                wrongPhoto.Parameters.AddWithValue("now", now);
                wrongPhoto.Parameters.AddWithValue("hash", new string('c', 64));
                PostgresException rejected = await Assert.ThrowsAsync<PostgresException>(
                    () => wrongPhoto.ExecuteNonQueryAsync());
                Assert.Equal("23503", rejected.SqlState); // wrong media kind -> composite FK
            }

            await using (NpgsqlCommand deleteRevision = connection.CreateCommand())
            {
                deleteRevision.CommandText = "DELETE FROM asset_revisions WHERE id = @revision;";
                deleteRevision.Parameters.AddWithValue("revision", videoRevision);
                Assert.Equal(1, await deleteRevision.ExecuteNonQueryAsync());
            }

            await using (NpgsqlCommand verifyCascade = connection.CreateCommand())
            {
                verifyCascade.CommandText = """
                    SELECT
                        (SELECT count(*) FROM video_revision_metadata),
                        (SELECT count(*) FROM video_still_posters),
                        (SELECT count(*) FROM asset_revisions WHERE id = @photo_revision);
                    """;
                verifyCascade.Parameters.AddWithValue("photo_revision", photoRevision);
                await using NpgsqlDataReader counts = await verifyCascade.ExecuteReaderAsync();
                Assert.True(await counts.ReadAsync());
                Assert.Equal(0, counts.GetInt64(0));
                Assert.Equal(0, counts.GetInt64(1));
                Assert.Equal(1, counts.GetInt64(2));
            }
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quoted} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
