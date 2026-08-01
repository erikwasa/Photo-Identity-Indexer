using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using OpenCvSharp;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CollectionThumbnailApplicationTests
{
    [Fact]
    public async Task Collection_results_use_fixed_size_no_store_thumbnails()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceRoot = Path.Combine(directory, "private-photos");
            Directory.CreateDirectory(sourceRoot);

            byte[] image;
            using (Mat original = new(
                       new Size(1600, 1200),
                       MatType.CV_8UC3,
                       new Scalar(70, 120, 180)))
            {
                Cv2.Rectangle(
                    original,
                    new Rect(250, 180, 900, 700),
                    new Scalar(210, 190, 90),
                    thickness: -1);
                Assert.True(Cv2.ImEncode(".png", original, out image));
            }

            string photoPath = Path.Combine(sourceRoot, "photo.png");
            await File.WriteAllBytesAsync(photoPath, image);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteReviewRepository reviewRepository = new(database);

            DateTimeOffset now = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
            CatalogueReviewPerson person = await reviewRepository.CreatePersonAsync("Ada Lovelace", now);
            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = AssetRevisionId.New();
            FaceOccurrenceId faceId = FaceOccurrenceId.New();
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
                        'photo.png',
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
                        1600,
                        1200);

                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES ($face_id, $revision_id, 0, $created_at_utc);

                    INSERT INTO face_observations (
                        face_occurrence_id,
                        detector_model_id,
                        detector_model_hash,
                        confidence,
                        bounding_box_json,
                        landmarks_json,
                        observed_at_utc)
                    VALUES (
                        $face_id,
                        'test-detector',
                        $detector_hash,
                        0.99,
                        '{"x":250,"y":180,"width":900,"height":700}',
                        '[]',
                        $created_at_utc);
                    """;
                command.Parameters.AddWithValue("$source_id", sourceId.ToString());
                command.Parameters.AddWithValue("$root_locator", sourceRoot);
                command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
                command.Parameters.AddWithValue("$asset_id", assetId.ToString());
                command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                command.Parameters.AddWithValue("$face_id", faceId.ToString());
                command.Parameters.AddWithValue("$content_hash", contentHash);
                command.Parameters.AddWithValue("$size_bytes", image.LongLength);
                command.Parameters.AddWithValue("$detector_hash", new string('d', 64));
                await command.ExecuteNonQueryAsync();
            }

            await reviewRepository.AssignAsync(
                faceId,
                person.Id,
                "human:test",
                now.AddMinutes(1));

            await using CollectionThumbnailApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            CollectionPhotoPageResponse page =
                await client.GetFromJsonAsync<CollectionPhotoPageResponse>(
                    $"/api/collections/photos?people={person.Id}")
                ?? throw new InvalidOperationException("The collection response was empty.");
            CollectionPhotoResponse photo = Assert.Single(page.Items);
            Assert.Equal(
                $"/api/collections/photos/{revisionId}/thumbnail",
                photo.ContentUrl);

            using HttpResponseMessage thumbnailResponse = await client.GetAsync(photo.ContentUrl);
            thumbnailResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", thumbnailResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains(
                "no-store",
                thumbnailResponse.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            byte[] thumbnail = await thumbnailResponse.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(thumbnail);
            Assert.NotEqual(image, thumbnail);
            using Mat decoded = Cv2.ImDecode(thumbnail, ImreadModes.Color);
            Assert.False(decoded.Empty());
            Assert.Equal(OpenCvThumbnailRenderer.ThumbnailWidth, decoded.Cols);
            Assert.Equal(OpenCvThumbnailRenderer.ThumbnailHeight, decoded.Rows);
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

    private sealed class CollectionThumbnailApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public CollectionThumbnailApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
