using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Requires an explicitly disposable PostgreSQL administrator connection.
/// No production catalogue, originals, OneDrive, GeoNames or video bytes.
/// </summary>
public sealed class VideoDetailsPostgresTests
{
    [Fact]
    public async Task Metadata_manual_people_places_current_revision_and_exclusion_are_isolated()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_wi0212_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection server = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await server.OpenAsync();
        await using (NpgsqlCommand cmd = server.CreateCommand())
        {
            cmd.CommandText = $"CREATE DATABASE \"{name}\";";
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            string connectionString = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = name,
                Pooling = false
            }.ConnectionString;
            await using PostgresCatalogueDatabase database = new(connectionString);
            await database.InitializeAsync();
            var repository = new PostgresVideoDetailsRepository(database, TimeProvider.System);
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(),
                videoRevision = Guid.NewGuid(), photoAsset = Guid.NewGuid(),
                photoRevision = Guid.NewGuid(), person = Guid.NewGuid();
            DateTimeOffset now = new(2026, 11, 8, 11, 30, 0, TimeSpan.Zero);
            DateTimeOffset original = new(2024, 7, 2, 15, 45, 0, TimeSpan.Zero);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'C:/synthetic/not-present/wi0212', @now);
                    INSERT INTO assets
                        (id, source_id, source_key, media_kind, created_at_utc)
                    VALUES (@asset, @source, 'private/clip.mov', 'video', @now),
                           (@photo_asset, @source, 'private/photo.jpg', 'photo', @now);
                    INSERT INTO asset_revisions
                        (id, asset_id, media_kind, content_sha256, size_bytes,
                         observed_at_utc, media_type)
                    VALUES (@revision, @asset, 'video', @video_hash, 1234, @now, 'video/quicktime'),
                           (@photo_revision, @photo_asset, 'photo', @photo_hash, 234, @now, 'image/jpeg');
                    INSERT INTO video_revision_metadata
                        (asset_revision_id, inspection_status, capture_at_utc,
                         capture_time_provenance, camera_make, camera_model,
                         gps_latitude, gps_longitude, coded_width, coded_height,
                         display_rotation_degrees, duration_seconds, average_frame_rate,
                         inspected_at_utc, tool_sha256, inspection_profile_id)
                    VALUES (@revision, 'supported', @capture, 'quicktime-tag',
                            'Camera Co', 'Camera X', 59.3293, 18.0686, 1920, 1080,
                            90, 12.5, '30/1', @now, @tool_hash, 'wi0151-v1');
                    INSERT INTO people
                        (id, display_name, created_at_utc, merged_into_person_id)
                    VALUES (@person, 'Known Person', @now, NULL);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("photo_asset", photoAsset);
                seed.Parameters.AddWithValue("revision", videoRevision);
                seed.Parameters.AddWithValue("photo_revision", photoRevision);
                seed.Parameters.AddWithValue("person", person);
                seed.Parameters.AddWithValue("now", now);
                seed.Parameters.AddWithValue("capture", original);
                seed.Parameters.AddWithValue("video_hash", new string('a', 64));
                seed.Parameters.AddWithValue("photo_hash", new string('b', 64));
                seed.Parameters.AddWithValue("tool_hash", new string('c', 64));
                await seed.ExecuteNonQueryAsync();
            }

            AssetRevisionId revision = AssetRevisionId.From(videoRevision);
            VideoDetailsRecord before = Assert.IsType<VideoDetailsRecord>(await repository.GetAsync(revision));
            Assert.Equal("clip.mov", before.FileName);
            Assert.Equal(original, before.EffectiveCaptureAtUtc);
            Assert.Equal("Camera Co", before.CameraMake);
            Assert.Equal("Camera X", before.CameraModel);
            Assert.Equal(59.3293, before.Latitude);
            Assert.Equal(18.0686, before.Longitude);
            Assert.Equal(90, before.RotationDegrees);
            Assert.Null(before.Place);
            Assert.Empty(before.People);

            DateTimeOffset corrected = new(2024, 7, 2, 17, 30, 0, TimeSpan.FromHours(2));
            await repository.SetCaptureDateAsync(revision, corrected, "test");
            await repository.SetPersonAsync(revision, person, true, "test");
            await repository.SetPersonAsync(revision, person, true, "test");
            await repository.SetPlaceAsync(revision, "Sweden/Stockholm region/Norrtälje", "manual", "test");
            await repository.SetPlaceAsync(revision, "France/Paris", "automatic", "geonames", "geonames");

            VideoDetailsRecord changed = Assert.IsType<VideoDetailsRecord>(await repository.GetAsync(revision));
            Assert.Equal(corrected.ToUniversalTime(), changed.EffectiveCaptureAtUtc);
            Assert.Equal("manual", changed.CaptureSource);
            Assert.Equal("Sweden/Stockholm region/Norrtälje", changed.Place?.Value);
            Assert.True(changed.ManualPlaceOverride);
            Assert.Equal("Known Person", Assert.Single(changed.People).DisplayName);

            await repository.SetPersonAsync(revision, person, false, "test");
            await repository.SetCaptureDateAsync(revision, null, "test");
            VideoDetailsRecord reset = Assert.IsType<VideoDetailsRecord>(await repository.GetAsync(revision));
            Assert.Empty(reset.People);
            Assert.Equal(original, reset.EffectiveCaptureAtUtc);

            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.SetPersonAsync(
                AssetRevisionId.From(photoRevision), person, true, "test"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.SetPlaceAsync(
                AssetRevisionId.From(photoRevision), "Sweden", "manual", "test"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.SetCaptureDateAsync(
                AssetRevisionId.From(photoRevision), now, "test"));

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand check = connection.CreateCommand();
                check.CommandText = """
                    SELECT
                        (SELECT count(*) FROM video_person_actions WHERE asset_revision_id = @revision),
                        (SELECT count(*) FROM video_place_actions WHERE asset_revision_id = @revision),
                        (SELECT count(*) FROM video_capture_date_actions WHERE asset_revision_id = @revision),
                        (SELECT count(*) FROM face_occurrences),
                        (SELECT count(*) FROM photo_person_actions),
                        (SELECT count(*) FROM photo_place_actions);
                    """;
                check.Parameters.AddWithValue("revision", videoRevision);
                await using NpgsqlDataReader reader = await check.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0)); // one add, one remove; no duplicate
                Assert.Equal(1L, reader.GetInt64(1)); // manual overrides provider
                Assert.Equal(2L, reader.GetInt64(2));
                Assert.Equal(0L, reader.GetInt64(3));
                Assert.Equal(0L, reader.GetInt64(4));
                Assert.Equal(0L, reader.GetInt64(5));
            }

            // A newer revision invalidates the old detail and every mutation.
            Guid nextRevision = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand next = connection.CreateCommand();
                next.CommandText = """
                    INSERT INTO asset_revisions
                        (id, asset_id, media_kind, content_sha256, size_bytes,
                         observed_at_utc, media_type)
                    VALUES (@next, @asset, 'video', @hash, 4321, @later, 'video/quicktime');
                    """;
                next.Parameters.AddWithValue("next", nextRevision);
                next.Parameters.AddWithValue("asset", asset);
                next.Parameters.AddWithValue("hash", new string('d', 64));
                next.Parameters.AddWithValue("later", now.AddMinutes(10));
                await next.ExecuteNonQueryAsync();
            }
            Assert.Null(await repository.GetAsync(revision));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.SetPersonAsync(
                revision, person, true, "test"));

            AssetRevisionId nextId = AssetRevisionId.From(nextRevision);
            Assert.NotNull(await repository.GetAsync(nextId));
            await repository.SetPersonAsync(nextId, person, true, "test");
            await repository.SetPlaceAsync(nextId, "Sweden/Stockholm", "manual", "test");

            // Source exclusion removes revision-bound manual annotations.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand exclude = connection.CreateCommand();
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions
                        (source_id, source_key, excluded_at_utc, purge_state, purge_updated_at_utc)
                    VALUES (@source, 'private/clip.mov', @now, 'pending', @now);
                    """;
                exclude.Parameters.AddWithValue("source", source);
                exclude.Parameters.AddWithValue("now", now.AddMinutes(20));
                await exclude.ExecuteNonQueryAsync();
            }
            Assert.Null(await repository.GetAsync(nextId));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.SetPlaceAsync(
                nextId, "Sweden", "manual", "test"));
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            {
                await using NpgsqlCommand counts = connection.CreateCommand();
                counts.CommandText = """
                    SELECT (SELECT count(*) FROM video_person_actions),
                           (SELECT count(*) FROM video_place_actions),
                           (SELECT count(*) FROM video_capture_date_actions);
                    """;
                await using NpgsqlDataReader reader = await counts.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
            }
        }
        finally
        {
            await using (NpgsqlCommand stop = server.CreateCommand())
            {
                stop.CommandText = $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='{name}' AND pid <> pg_backend_pid();";
                await stop.ExecuteNonQueryAsync();
            }
            await using (NpgsqlCommand drop = server.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE \"{name}\";";
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
