using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CollectionContentApplicationTests
{
    [Fact]
    public async Task Collection_content_streams_local_photo_and_rejects_root_escape()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceRoot = Path.Combine(directory, "private-photos");
            string relativeDirectory = Path.Combine(sourceRoot, "family");
            Directory.CreateDirectory(relativeDirectory);

            byte[] image = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZlS8AAAAASUVORK5CYII=");
            string photoPath = Path.Combine(relativeDirectory, "photo.png");
            await File.WriteAllBytesAsync(photoPath, image);

            string outsidePath = Path.Combine(directory, "outside.png");
            await File.WriteAllBytesAsync(outsidePath, image);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 8, 1, 21, 0, 0, TimeSpan.Zero);
            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = AssetRevisionId.New();
            AssetId escapeAssetId = AssetId.New();
            AssetRevisionId escapeRevisionId = AssetRevisionId.New();
            string contentHash = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES ($source_id, 'local-folder', $root_locator, $created_at_utc);

                    INSERT INTO assets (
                        id,
                        source_id,
                        source_key,
                        created_at_utc,
                        last_seen_at_utc,
                        deleted_at_utc)
                    VALUES (
                        $asset_id,
                        $source_id,
                        'family/photo.png',
                        $created_at_utc,
                        $created_at_utc,
                        NULL);

                    INSERT INTO asset_revisions (
                        id,
                        asset_id,
                        content_sha256,
                        size_bytes,
                        observed_at_utc,
                        media_type,
                        width,
                        height)
                    VALUES (
                        $revision_id,
                        $asset_id,
                        $content_hash,
                        $size_bytes,
                        $created_at_utc,
                        'image/png',
                        1,
                        1);

                    INSERT INTO assets (
                        id,
                        source_id,
                        source_key,
                        created_at_utc,
                        last_seen_at_utc,
                        deleted_at_utc)
                    VALUES (
                        $escape_asset_id,
                        $source_id,
                        '../outside.png',
                        $created_at_utc,
                        $created_at_utc,
                        NULL);

                    INSERT INTO asset_revisions (
                        id,
                        asset_id,
                        content_sha256,
                        size_bytes,
                        observed_at_utc,
                        media_type,
                        width,
                        height)
                    VALUES (
                        $escape_revision_id,
                        $escape_asset_id,
                        $content_hash,
                        $size_bytes,
                        $created_at_utc,
                        'image/png',
                        1,
                        1);
                    """;
                command.Parameters.AddWithValue("$source_id", sourceId.ToString());
                command.Parameters.AddWithValue("$root_locator", sourceRoot);
                command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
                command.Parameters.AddWithValue("$asset_id", assetId.ToString());
                command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                command.Parameters.AddWithValue("$escape_asset_id", escapeAssetId.ToString());
                command.Parameters.AddWithValue("$escape_revision_id", escapeRevisionId.ToString());
                command.Parameters.AddWithValue("$content_hash", contentHash);
                command.Parameters.AddWithValue("$size_bytes", image.LongLength);
                await command.ExecuteNonQueryAsync();
            }

            await using CollectionContentApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/collections/photos/{revisionId}/content");
            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains(
                "no-store",
                response.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(image, await response.Content.ReadAsByteArrayAsync());

            using HttpResponseMessage escapeResponse = await client.GetAsync(
                $"/api/collections/photos/{escapeRevisionId}/content");
            Assert.Equal(HttpStatusCode.NotFound, escapeResponse.StatusCode);

            using HttpResponseMessage invalidResponse = await client.GetAsync(
                "/api/collections/photos/not-a-revision/content");
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
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

    private sealed class CollectionContentApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public CollectionContentApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
