using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoDetailsMetadataApplicationTests
{
    [Fact]
    public async Task Details_returns_persisted_capture_camera_and_raw_metadata()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            AssetRevisionId revisionId = await SeedRevisionAsync(database, directory);

            PhotoCaptureMetadata metadata = new(
                new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Unspecified),
                TimeSpan.FromHours(2),
                59.3293,
                18.0686,
                cameraMake: "Example Camera Co.",
                cameraModel: "Model X",
                lensModel: "35mm Prime",
                iso: "ISO 200",
                gpsAltitude: "42 metres",
                rawTags: [new PhotoMetadataTag("Exif IFD0", "Make", "Example Camera Co.")]);
            await new SqliteExtendedPhotoMetadataRepository(database).SaveAsync(revisionId, metadata);
            await new SqliteAssetCatalogueRepository(database).SavePhotoMetadataAsync(
                revisionId,
                metadata,
                new DateTimeOffset(2026, 8, 19, 18, 0, 0, TimeSpan.Zero));

            await using PhotoDetailsMetadataApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            PhotoDetailsResponse response = Assert.IsType<PhotoDetailsResponse>(
                await client.GetFromJsonAsync<PhotoDetailsResponse>(
                    $"/api/collections/photos/{revisionId}/details"));
            PhotoMetadataResponse returned = Assert.IsType<PhotoMetadataResponse>(response.Metadata);

            Assert.Equal(new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Unspecified), returned.TakenAtLocal);
            Assert.Equal(120, returned.UtcOffsetMinutes);
            Assert.Equal(59.3293, returned.Latitude);
            Assert.Equal(18.0686, returned.Longitude);
            Assert.Equal("Example Camera Co.", returned.CameraMake);
            Assert.Equal("Model X", returned.CameraModel);
            Assert.Equal("35mm Prime", returned.LensModel);
            Assert.Equal("ISO 200", returned.Iso);
            Assert.Equal("42 metres", returned.GpsAltitude);
            PhotoMetadataTagResponse rawTag = Assert.Single(returned.Tags);
            Assert.Equal("Exif IFD0", rawTag.Directory);
            Assert.Equal("Make", rawTag.Name);
            Assert.Equal("Example Camera Co.", rawTag.Value);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Details_distinguishes_not_inspected_from_inspected_empty_metadata()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            AssetRevisionId revisionId = await SeedRevisionAsync(database, directory);

            await using PhotoDetailsMetadataApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            PhotoDetailsResponse before = Assert.IsType<PhotoDetailsResponse>(
                await client.GetFromJsonAsync<PhotoDetailsResponse>(
                    $"/api/collections/photos/{revisionId}/details"));
            Assert.Null(before.Metadata);

            await new SqliteExtendedPhotoMetadataRepository(database)
                .SaveAsync(revisionId, new PhotoCaptureMetadata());
            await new SqliteAssetCatalogueRepository(database).SavePhotoMetadataAsync(
                revisionId,
                new PhotoCaptureMetadata(),
                DateTimeOffset.UtcNow);

            PhotoDetailsResponse after = Assert.IsType<PhotoDetailsResponse>(
                await client.GetFromJsonAsync<PhotoDetailsResponse>(
                    $"/api/collections/photos/{revisionId}/details"));
            PhotoMetadataResponse inspected = Assert.IsType<PhotoMetadataResponse>(after.Metadata);
            Assert.Null(inspected.TakenAtLocal);
            Assert.Null(inspected.Latitude);
            Assert.Empty(inspected.Tags);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<AssetRevisionId> SeedRevisionAsync(
        SqliteCatalogueDatabase database,
        string root)
    {
        string sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceRoot);
        string revisionId = Guid.NewGuid().ToString("D");
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string now = new DateTimeOffset(2026, 8, 19, 18, 0, 0, TimeSpan.Zero).ToString("O");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $source_root, $now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES ($asset_id, $source_id, 'photos/example.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES ($revision_id, $asset_id, $content_hash, 12345, $now, 'image/jpeg', 2000, 1500);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$revision_id", revisionId);
        command.Parameters.AddWithValue("$content_hash", new string('a', 64));
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();

        return AssetRevisionId.From(Guid.Parse(revisionId));
    }

    private sealed class PhotoDetailsMetadataApiFactory : PhotoIdentityApiTestFactory
    {
        public PhotoDetailsMetadataApiFactory(string databasePath)
            : base(databasePath)
        {
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
