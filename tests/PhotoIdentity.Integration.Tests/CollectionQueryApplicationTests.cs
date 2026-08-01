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

public sealed class CollectionQueryApplicationTests
{
    [Fact]
    public async Task Confirmed_collection_queries_support_explicit_any_and_all_semantics_without_paths()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceRoot = Path.Combine(directory, "private-family-archive");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 8, 1, 17, 0, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson ada = await reviewRepository.CreatePersonAsync("Ada Lovelace", now);
            CatalogueReviewPerson grace = await reviewRepository.CreatePersonAsync("Grace Hopper", now.AddSeconds(1));

            SourceId sourceId = SourceId.New();
            CatalogueSource source = new(sourceId, "local-folder", sourceRoot, now);

            SeededPhoto adaOnly = await SeedPhotoAsync(
                database,
                source,
                "family/private-ada.jpg",
                'a',
                now.AddMinutes(1),
                0.99);
            await reviewRepository.AssignAsync(
                adaOnly.Faces[0],
                ada.Id,
                "human:test",
                now.AddMinutes(2));

            SeededPhoto graceOnly = await SeedPhotoAsync(
                database,
                source,
                "family/private-grace.jpg",
                'b',
                now.AddMinutes(3),
                0.98);
            await reviewRepository.AssignAsync(
                graceOnly.Faces[0],
                grace.Id,
                "human:test",
                now.AddMinutes(4));

            SeededPhoto together = await SeedPhotoAsync(
                database,
                source,
                "family/private-together.jpg",
                'c',
                now.AddMinutes(5),
                0.90,
                0.96);
            await reviewRepository.AssignAsync(
                together.Faces[0],
                ada.Id,
                "human:test",
                now.AddMinutes(6));
            await reviewRepository.AssignAsync(
                together.Faces[1],
                grace.Id,
                "human:test",
                now.AddMinutes(7));

            await using CollectionApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string people = $"{ada.Id},{grace.Id}";

            using HttpResponseMessage defaultResponse = await client.GetAsync(
                $"/api/collections/photos?people={people}");
            defaultResponse.EnsureSuccessStatusCode();
            Assert.Contains(
                "no-store",
                defaultResponse.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            CollectionPhotoPageResponse defaultPage = Assert.IsType<CollectionPhotoPageResponse>(
                await defaultResponse.Content.ReadFromJsonAsync<CollectionPhotoPageResponse>());
            Assert.Equal(CatalogueCollectionMatchModes.All, defaultPage.Query.MatchMode);
            Assert.True(defaultPage.Query.ConfirmedOnly);
            CollectionPhotoResponse onlyTogether = Assert.Single(defaultPage.Items);
            Assert.Equal(together.RevisionId.ToString(), onlyTogether.RevisionId);
            Assert.Equal(2, onlyTogether.People.Count);

            using HttpResponseMessage anyResponse = await client.GetAsync(
                $"/api/collections/photos?people={people}&match=any");
            anyResponse.EnsureSuccessStatusCode();
            CollectionPhotoPageResponse anyPage = Assert.IsType<CollectionPhotoPageResponse>(
                await anyResponse.Content.ReadFromJsonAsync<CollectionPhotoPageResponse>());
            Assert.Equal(CatalogueCollectionMatchModes.Any, anyPage.Query.MatchMode);
            Assert.Equal(3, anyPage.Total);
            Assert.Equal(3, anyPage.Items.Count);

            string anyJson = JsonSerializer.Serialize(anyPage);
            Assert.DoesNotContain(sourceRoot, anyJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-ada.jpg", anyJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-grace.jpg", anyJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-together.jpg", anyJson, StringComparison.OrdinalIgnoreCase);

            using HttpResponseMessage confidenceResponse = await client.GetAsync(
                $"/api/collections/photos?people={people}&match=all&minimumConfidence=0.95");
            confidenceResponse.EnsureSuccessStatusCode();
            CollectionPhotoPageResponse confidencePage = Assert.IsType<CollectionPhotoPageResponse>(
                await confidenceResponse.Content.ReadFromJsonAsync<CollectionPhotoPageResponse>());
            Assert.Empty(confidencePage.Items);
            Assert.Equal(0, confidencePage.Total);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Collection_query_rejects_missing_people_and_unsupported_match_modes()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using CollectionApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage missingPeople = await client.GetAsync("/api/collections/photos");
            Assert.Equal(HttpStatusCode.BadRequest, missingPeople.StatusCode);

            using HttpResponseMessage invalidMatch = await client.GetAsync(
                $"/api/collections/photos?people={PersonId.New()}&match=some");
            Assert.Equal(HttpStatusCode.BadRequest, invalidMatch.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededPhoto> SeedPhotoAsync(
        SqliteCatalogueDatabase database,
        CatalogueSource source,
        string sourceKey,
        char hashCharacter,
        DateTimeOffset observedAtUtc,
        params double[] confidences)
    {
        AssetId assetId = AssetId.New();
        CatalogueAsset asset = new(assetId, source.Id, sourceKey, observedAtUtc);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string(hashCharacter, 64)),
            100,
            observedAtUtc,
            "image/jpeg",
            1920,
            1080);
        CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);

        List<FaceOccurrenceId> faces = [];
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        for (int ordinal = 0; ordinal < confidences.Length; ordinal++)
        {
            FaceOccurrenceId faceId = FaceOccurrenceId.New();
            faces.Add(faceId);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, $ordinal, $created_at_utc);

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
                    $model_hash,
                    $confidence,
                    '{"x":10,"y":10,"width":80,"height":80}',
                    '[]',
                    $created_at_utc);
                """;
            command.Parameters.AddWithValue("$face_id", faceId.ToString());
            command.Parameters.AddWithValue("$revision_id", persistedRevision.Id.ToString());
            command.Parameters.AddWithValue("$ordinal", ordinal);
            command.Parameters.AddWithValue("$created_at_utc", observedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$model_hash", new string('d', 64));
            command.Parameters.AddWithValue("$confidence", confidences[ordinal]);
            await command.ExecuteNonQueryAsync();
        }

        return new SeededPhoto(persistedRevision.Id, faces);
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

    private sealed record SeededPhoto(
        AssetRevisionId RevisionId,
        IReadOnlyList<FaceOccurrenceId> Faces);

    private sealed class CollectionApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public CollectionApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
