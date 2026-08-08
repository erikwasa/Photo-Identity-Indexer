using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CollectionManifestApplicationTests
{
    private const int PhotoCount = 201;

    [Fact]
    public async Task Manifest_pages_internally_and_returns_complete_path_free_consumer_document()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceRoot = Path.Combine(directory, "private-family-archive");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson person = await reviewRepository.CreatePersonAsync("Ada Lovelace", now);
            SourceId sourceId = SourceId.New();
            List<FaceOccurrenceId> faceIds = [];
            List<AssetRevisionId> revisionIds = [];

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                using (SqliteCommand sourceCommand = connection.CreateCommand())
                {
                    sourceCommand.Transaction = transaction;
                    sourceCommand.CommandText = """
                        INSERT INTO sources (id, kind, root_locator, created_at_utc)
                        VALUES ($id, 'local-folder', $root, $created_at_utc);
                        """;
                    sourceCommand.Parameters.AddWithValue("$id", sourceId.ToString());
                    sourceCommand.Parameters.AddWithValue("$root", sourceRoot);
                    sourceCommand.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
                    await sourceCommand.ExecuteNonQueryAsync();
                }

                for (int index = 0; index < PhotoCount; index++)
                {
                    AssetId assetId = AssetId.New();
                    AssetRevisionId revisionId = AssetRevisionId.New();
                    FaceOccurrenceId faceId = FaceOccurrenceId.New();
                    revisionIds.Add(revisionId);
                    faceIds.Add(faceId);
                    DateTimeOffset observedAt = now.AddMinutes(index + 1);

                    using SqliteCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
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
                            $source_key,
                            $observed_at_utc,
                            $observed_at_utc,
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
                            1,
                            $observed_at_utc,
                            'image/jpeg',
                            1600,
                            1200);

                        INSERT INTO face_occurrences (
                            id,
                            asset_revision_id,
                            ordinal,
                            created_at_utc)
                        VALUES (
                            $face_id,
                            $revision_id,
                            0,
                            $observed_at_utc);

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
                            '{"x":10,"y":10,"width":80,"height":80}',
                            '[]',
                            $observed_at_utc);
                        """;
                    command.Parameters.AddWithValue("$asset_id", assetId.ToString());
                    command.Parameters.AddWithValue("$source_id", sourceId.ToString());
                    command.Parameters.AddWithValue("$source_key", $"family/private-photo-{index:D3}.jpg");
                    command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                    command.Parameters.AddWithValue("$face_id", faceId.ToString());
                    command.Parameters.AddWithValue("$content_hash", new string('a', 64));
                    command.Parameters.AddWithValue("$detector_hash", new string('b', 64));
                    command.Parameters.AddWithValue("$observed_at_utc", observedAt.ToString("O"));
                    await command.ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }

            for (int index = 0; index < faceIds.Count; index++)
            {
                await reviewRepository.AssignAsync(
                    faceIds[index],
                    person.Id,
                    "human:test",
                    now.AddHours(1).AddSeconds(index));
            }

            await using CollectionManifestApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/collections/manifest?people={person.Id}");
            response.EnsureSuccessStatusCode();
            Assert.Equal(
                "application/vnd.photoidentity.collection-manifest+json",
                response.Content.Headers.ContentType?.MediaType);
            Assert.Contains(
                "no-store",
                response.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            CollectionManifestResponse manifest =
                await response.Content.ReadFromJsonAsync<CollectionManifestResponse>()
                ?? throw new InvalidOperationException("The collection manifest response was empty.");
            Assert.Equal("photoidentity.collection-manifest", manifest.Format);
            Assert.Equal(2, manifest.Version);
            Assert.Equal(PhotoCount, manifest.Total);
            Assert.Equal(PhotoCount, manifest.Photos.Count);
            Assert.Equal("all", manifest.Query.MatchMode);
            Assert.Equal("assigned", manifest.Query.ReviewState);
            Assert.True(manifest.Query.ConfirmedOnly);
            Assert.Null(manifest.Query.SuggestionPolicy);
            Assert.Equal(person.Id.ToString(), Assert.Single(manifest.Query.PersonIds));

            Assert.Equal(revisionIds[^1].ToString(), manifest.Photos[0].RevisionId);
            Assert.Equal(revisionIds[0].ToString(), manifest.Photos[^1].RevisionId);
            Assert.All(manifest.Photos, photo =>
            {
                Assert.StartsWith(
                    "http://localhost/api/collections/photos/",
                    photo.ThumbnailUrl,
                    StringComparison.Ordinal);
                Assert.EndsWith("/thumbnail", photo.ThumbnailUrl, StringComparison.Ordinal);
                Assert.StartsWith(
                    "http://localhost/api/collections/photos/",
                    photo.PreviewUrl,
                    StringComparison.Ordinal);
                Assert.EndsWith("/preview", photo.PreviewUrl, StringComparison.Ordinal);
                Assert.StartsWith(
                    "http://localhost/api/collections/photos/",
                    photo.OriginalUrl,
                    StringComparison.Ordinal);
                Assert.EndsWith("/original", photo.OriginalUrl, StringComparison.Ordinal);
                Assert.Equal(photo.OriginalUrl, photo.ContentUrl);
                Assert.Equal("image/jpeg", photo.MediaType);
                Assert.Equal(1600, photo.Width);
                Assert.Equal(1200, photo.Height);
                CollectionPersonMatchResponse match = Assert.Single(photo.People);
                Assert.Equal(person.Id.ToString(), match.Id);
                Assert.Equal("Ada Lovelace", match.DisplayName);
                Assert.Equal(1, match.ConfirmedFaceCount);
                Assert.Equal(0, match.SuggestedFaceCount);
            });

            string json = JsonSerializer.Serialize(manifest);
            Assert.DoesNotContain(sourceRoot, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-photo-", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rootLocator", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sourceKey", json, StringComparison.OrdinalIgnoreCase);

            using HttpResponseMessage missingPeople = await client.GetAsync("/api/collections/manifest");
            Assert.Equal(HttpStatusCode.BadRequest, missingPeople.StatusCode);
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

    private sealed class CollectionManifestApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public CollectionManifestApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
