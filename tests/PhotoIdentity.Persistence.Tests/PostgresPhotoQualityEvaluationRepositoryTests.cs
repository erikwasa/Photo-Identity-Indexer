using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoQualityEvaluationRepositoryTests
{
    private const string ProfileId = "jpeg-1600-q78";

    [Fact]
    public async Task Benchmark_population_reads_current_photo_metadata_and_excludes_video_rows()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_quality_benchmark_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };
            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            Assert.Null(initialization.Error);

            AssetRevisionId first = AssetRevisionId.New();
            AssetRevisionId second = AssetRevisionId.New();
            AssetRevisionId video = AssetRevisionId.New();
            await SeedAsync(database, first, second, video);

            PostgresPhotoQualityEvaluationRepository repository = new(database);
            IReadOnlyList<PhotoQualityBenchmarkPopulationRow> population =
                await repository.ReadCurrentProxyPopulationAsync(ProfileId);

            Assert.Equal(2, population.Count);
            PhotoQualityBenchmarkPopulationRow enriched =
                Assert.Single(population.Where(row => row.AssetRevisionId == first));
            Assert.Equal("2020/01/first.jpg", enriched.SourceKey);
            Assert.Equal("image/jpeg", enriched.MediaType);
            Assert.Equal(4000, enriched.Width);
            Assert.Equal(3000, enriched.Height);
            Assert.Equal(new DateTime(2020, 1, 2, 12, 30, 0), enriched.TakenAtLocal);
            Assert.Equal("Example", enriched.CameraMake);
            Assert.Equal("Camera 1", enriched.CameraModel);
            Assert.Equal(1, enriched.FaceCount);
            Assert.Equal("review-proxies/first.jpg", enriched.Proxy.RelativePath);

            PhotoQualityBenchmarkPopulationRow plain =
                Assert.Single(population.Where(row => row.AssetRevisionId == second));
            Assert.Null(plain.TakenAtLocal);
            Assert.Null(plain.CameraModel);
            Assert.Equal(0, plain.FaceCount);

            IReadOnlyList<PhotoQualityEvaluationCandidate> legacySample =
                await repository.ReadCurrentProxySampleAsync(ProfileId, 10);
            Assert.Equal(2, legacySample.Count);
            Assert.DoesNotContain(legacySample, row => row.AssetRevisionId == video);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAsync(
        PostgresCatalogueDatabase database,
        AssetRevisionId first,
        AssetRevisionId second,
        AssetRevisionId video)
    {
        DateTimeOffset now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        Guid source = Guid.NewGuid();
        Guid firstAsset = Guid.NewGuid();
        Guid secondAsset = Guid.NewGuid();
        Guid videoAsset = Guid.NewGuid();

        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source, 'test', @root, @now);

            INSERT INTO archive_configuration (id, source_id, configured_at_utc)
            VALUES (1, @source, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, media_kind)
            VALUES
                (@first_asset, @source, '2020/01/first.jpg', @now, 'photo'),
                (@second_asset, @source, 'Moa/20260216_141216971_iOS.heic', @now, 'photo'),
                (@video_asset, @source, 'video/clip.mp4', @now, 'video');

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height, media_kind)
            VALUES
                (@first_revision, @first_asset, @first_hash, 1000, @now, 'image/jpeg', 4000, 3000, 'photo'),
                (@second_revision, @second_asset, @second_hash, 1000, @now, 'image/heic', 3000, 4000, 'photo'),
                (@video_revision, @video_asset, @video_hash, 1000, @now, 'video/mp4', 1920, 1080, 'video');

            INSERT INTO archive_review_proxy_profiles (
                profile_id, protocol_version, encoder, format, jpeg_quality,
                maximum_long_edge, resize_policy, canonical_definition, recorded_at_utc)
            VALUES (
                @profile_id, 'v1', 'test', 'jpeg', 78, 1600,
                'fit', 'quality benchmark test proxy', @now);

            INSERT INTO asset_revision_review_proxies (
                asset_revision_id, profile_id, encoded_byte_length, content_sha256,
                width, height, generated_at_utc, relative_path)
            VALUES
                (@first_revision, @profile_id, 42, @proxy_hash_1, 1600, 1200, @now, 'review-proxies/first.jpg'),
                (@second_revision, @profile_id, 42, @proxy_hash_2, 1200, 1600, @now, 'review-proxies/second.jpg'),
                (@video_revision, @profile_id, 42, @proxy_hash_3, 1600, 900, @now, 'review-proxies/video.jpg');

            INSERT INTO photo_capture_metadata (
                asset_revision_id, taken_at_local, extracted_at_utc)
            VALUES (@first_revision, @taken, @now);

            INSERT INTO photo_extended_metadata (
                asset_revision_id, camera_make, camera_model)
            VALUES (@first_revision, 'Example', 'Camera 1');

            INSERT INTO face_occurrences (
                id, asset_revision_id, ordinal, created_at_utc)
            VALUES (@face_id, @first_revision, 0, @now);
            """;
        command.Parameters.AddWithValue("source", NpgsqlDbType.Uuid, source);
        command.Parameters.AddWithValue("root", NpgsqlDbType.Text, $"quality-benchmark-{source:N}");
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("first_asset", NpgsqlDbType.Uuid, firstAsset);
        command.Parameters.AddWithValue("second_asset", NpgsqlDbType.Uuid, secondAsset);
        command.Parameters.AddWithValue("video_asset", NpgsqlDbType.Uuid, videoAsset);
        command.Parameters.AddWithValue("first_revision", NpgsqlDbType.Uuid, first.Value);
        command.Parameters.AddWithValue("second_revision", NpgsqlDbType.Uuid, second.Value);
        command.Parameters.AddWithValue("video_revision", NpgsqlDbType.Uuid, video.Value);
        command.Parameters.AddWithValue("first_hash", NpgsqlDbType.Text, new string('1', 64));
        command.Parameters.AddWithValue("second_hash", NpgsqlDbType.Text, new string('2', 64));
        command.Parameters.AddWithValue("video_hash", NpgsqlDbType.Text, new string('3', 64));
        command.Parameters.AddWithValue("profile_id", NpgsqlDbType.Text, ProfileId);
        command.Parameters.AddWithValue("proxy_hash_1", NpgsqlDbType.Text, new string('4', 64));
        command.Parameters.AddWithValue("proxy_hash_2", NpgsqlDbType.Text, new string('5', 64));
        command.Parameters.AddWithValue("proxy_hash_3", NpgsqlDbType.Text, new string('6', 64));
        command.Parameters.AddWithValue(
            "taken",
            NpgsqlDbType.Timestamp,
            new DateTime(2020, 1, 2, 12, 30, 0, DateTimeKind.Unspecified));
        command.Parameters.AddWithValue("face_id", NpgsqlDbType.Uuid, Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
