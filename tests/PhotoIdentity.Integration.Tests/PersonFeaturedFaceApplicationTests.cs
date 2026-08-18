using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PersonFeaturedFaceApplicationTests
{
    [Fact]
    public async Task Representative_face_api_sets_clears_and_persists_an_explicit_face()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteReviewRepository review = new(database);
            DateTimeOffset now = new(2026, 8, 18, 19, 30, 0, TimeSpan.Zero);
            CatalogueReviewPerson alice = await review.CreatePersonAsync("Alice", now);
            FaceOccurrenceId first = await CreateAssignedFaceAsync(
                database,
                review,
                directory,
                alice.Id,
                "first.jpg",
                'a',
                now.AddMinutes(1));
            FaceOccurrenceId second = await CreateAssignedFaceAsync(
                database,
                review,
                directory,
                alice.Id,
                "second.jpg",
                'b',
                now.AddMinutes(2));

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            PersonRepresentativeFaceResponse automatic = await GetRepresentativeAsync(client, alice.Id);
            Assert.Equal(first.ToString(), automatic.FaceId);
            Assert.False(automatic.IsExplicit);
            Assert.Equal($"/api/review/faces/{first}/image?size=360", automatic.ImageUrl);

            PersonRepresentativeFaceResponse explicitResult = await SetFeaturedAsync(client, alice.Id, second);
            Assert.Equal(second.ToString(), explicitResult.FaceId);
            Assert.True(explicitResult.IsExplicit);

            CataloguePersonRepresentativeFace? reopened =
                await new SqlitePersonFeaturedFaceRepository(new SqliteCatalogueDatabase(databasePath))
                    .ResolveAsync(alice.Id);
            Assert.NotNull(reopened);
            Assert.Equal(second, reopened.FaceId);
            Assert.True(reopened.IsExplicit);

            PersonRepresentativeFaceResponse cleared = await ClearFeaturedAsync(client, alice.Id);
            Assert.Equal(first.ToString(), cleared.FaceId);
            Assert.False(cleared.IsExplicit);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Resolver_never_uses_a_featured_face_after_it_is_reassigned_to_another_person()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteReviewRepository review = new(database);
            SqlitePersonFeaturedFaceRepository featured = new(database);
            DateTimeOffset now = new(2026, 8, 18, 20, 0, 0, TimeSpan.Zero);
            CatalogueReviewPerson alice = await review.CreatePersonAsync("Alice", now);
            CatalogueReviewPerson bob = await review.CreatePersonAsync("Bob", now.AddSeconds(1));
            FaceOccurrenceId fallback = await CreateAssignedFaceAsync(
                database,
                review,
                directory,
                alice.Id,
                "fallback.jpg",
                'c',
                now.AddMinutes(1));
            FaceOccurrenceId featuredFace = await CreateAssignedFaceAsync(
                database,
                review,
                directory,
                alice.Id,
                "featured.jpg",
                'd',
                now.AddMinutes(2));

            await featured.SetFeaturedFaceAsync(alice.Id, featuredFace, now.AddMinutes(3));
            await review.AssignAsync(featuredFace, bob.Id, "test", now.AddMinutes(4));

            CataloguePersonRepresentativeFace? aliceRepresentative = await featured.ResolveAsync(alice.Id);
            Assert.NotNull(aliceRepresentative);
            Assert.Equal(fallback, aliceRepresentative.FaceId);
            Assert.False(aliceRepresentative.IsExplicit);

            CataloguePersonRepresentativeFace? bobRepresentative = await featured.ResolveAsync(bob.Id);
            Assert.NotNull(bobRepresentative);
            Assert.Equal(featuredFace, bobRepresentative.FaceId);
            Assert.False(bobRepresentative.IsExplicit);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage invalid = await client.PutAsJsonAsync(
                $"/api/review/people/{alice.Id}/featured-face",
                new SetPersonFeaturedFaceRequest(featuredFace.ToString()));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<PersonRepresentativeFaceResponse> GetRepresentativeAsync(
        HttpClient client,
        PersonId personId) =>
        await client.GetFromJsonAsync<PersonRepresentativeFaceResponse>(
            $"/api/review/people/{personId}/representative-face")
        ?? throw new InvalidOperationException("Representative face response was empty.");

    private static async Task<PersonRepresentativeFaceResponse> SetFeaturedAsync(
        HttpClient client,
        PersonId personId,
        FaceOccurrenceId faceId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/review/people/{personId}/featured-face",
            new SetPersonFeaturedFaceRequest(faceId.ToString()));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PersonRepresentativeFaceResponse>()
            ?? throw new InvalidOperationException("Featured face response was empty.");
    }

    private static async Task<PersonRepresentativeFaceResponse> ClearFeaturedAsync(
        HttpClient client,
        PersonId personId)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/review/people/{personId}/featured-face",
            new SetPersonFeaturedFaceRequest(null));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PersonRepresentativeFaceResponse>()
            ?? throw new InvalidOperationException("Featured face response was empty.");
    }

    private static async Task<FaceOccurrenceId> CreateAssignedFaceAsync(
        SqliteCatalogueDatabase database,
        SqliteReviewRepository review,
        string root,
        PersonId personId,
        string sourceKey,
        char hashCharacter,
        DateTimeOffset createdAtUtc)
    {
        SqliteAssetCatalogueRepository catalogue = new(database);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        string sourceRoot = Path.Combine(root, assetId.ToString());
        Directory.CreateDirectory(sourceRoot);
        CatalogueAssetRevision revision = await catalogue.SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, createdAtUtc),
            new CatalogueAsset(assetId, sourceId, sourceKey, createdAtUtc),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(new string(hashCharacter, 64)),
                100,
                createdAtUtc,
                "image/jpeg",
                100,
                100));

        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($id, $revision_id, 0, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$id", faceId.ToString());
        command.Parameters.AddWithValue("$revision_id", revision.Id.ToString());
        command.Parameters.AddWithValue("$created_at_utc", createdAtUtc.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync();

        await review.AssignAsync(faceId, personId, "test", createdAtUtc.AddSeconds(1));
        return faceId;
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
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ReviewApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public ReviewApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
