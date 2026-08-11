using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class FavoritePeopleApplicationTests
{
    private static readonly ModelId EmbeddingModelId = new("sface");
    private static readonly Sha256Digest EmbeddingModelHash = new(new string('e', 64));

    [Fact]
    public async Task Favorite_state_persists_orders_people_and_survives_rename_and_unfavorite()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 19, 0, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson zelda = await reviewRepository.CreatePersonAsync("Zelda", now);
            CatalogueReviewPerson ada = await reviewRepository.CreatePersonAsync("Ada", now.AddMinutes(1));
            CatalogueReviewPerson grace = await reviewRepository.CreatePersonAsync("Grace", now.AddMinutes(2));
            CatalogueReviewPerson bob = await reviewRepository.CreatePersonAsync("Bob", now.AddMinutes(3));

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage favoriteGrace = await client.PutAsJsonAsync(
                $"/api/review/people/{grace.Id}/favorite",
                new SetPersonFavoriteRequest(true));
            favoriteGrace.EnsureSuccessStatusCode();
            using HttpResponseMessage favoriteBob = await client.PutAsJsonAsync(
                $"/api/review/people/{bob.Id}/favorite",
                new SetPersonFavoriteRequest(true));
            favoriteBob.EnsureSuccessStatusCode();

            ReviewPersonResponse[] assignmentPeople =
                await client.GetFromJsonAsync<ReviewPersonResponse[]>("/api/review/people") ?? [];
            Assert.Equal(["Bob", "Grace", "Ada", "Zelda"], assignmentPeople.Select(person => person.DisplayName));
            Assert.Equal([true, true, false, false], assignmentPeople.Select(person => person.IsFavorite));

            PersonMaintenancePersonResponse[] maintenancePeople =
                await client.GetFromJsonAsync<PersonMaintenancePersonResponse[]>(
                    "/api/review/people/maintenance") ?? [];
            Assert.Equal(["Bob", "Grace", "Ada", "Zelda"], maintenancePeople.Select(person => person.DisplayName));
            Assert.Equal([true, true, false, false], maintenancePeople.Select(person => person.IsFavorite));

            using HttpResponseMessage rename = await client.PostAsJsonAsync(
                $"/api/review/people/{grace.Id}/rename",
                new RenamePersonRequest("Aaron", "local-reviewer"));
            rename.EnsureSuccessStatusCode();

            assignmentPeople = await client.GetFromJsonAsync<ReviewPersonResponse[]>("/api/review/people") ?? [];
            Assert.Equal(["Aaron", "Bob", "Ada", "Zelda"], assignmentPeople.Select(person => person.DisplayName));
            Assert.True(assignmentPeople[0].IsFavorite);
            Assert.Equal(grace.Id.ToString(), assignmentPeople[0].Id);

            SqliteCatalogueDatabase reopenedDatabase = new(databasePath);
            IReadOnlySet<PersonId> persistedFavorites = await new SqliteFavoritePeopleRepository(reopenedDatabase)
                .GetFavoritePersonIdsAsync();
            Assert.Contains(grace.Id, persistedFavorites);
            Assert.Contains(bob.Id, persistedFavorites);
            Assert.DoesNotContain(ada.Id, persistedFavorites);
            Assert.DoesNotContain(zelda.Id, persistedFavorites);

            using HttpResponseMessage unfavoriteBob = await client.PutAsJsonAsync(
                $"/api/review/people/{bob.Id}/favorite",
                new SetPersonFavoriteRequest(false));
            unfavoriteBob.EnsureSuccessStatusCode();

            assignmentPeople = await client.GetFromJsonAsync<ReviewPersonResponse[]>("/api/review/people") ?? [];
            Assert.Equal(["Aaron", "Ada", "Bob", "Zelda"], assignmentPeople.Select(person => person.DisplayName));
            Assert.Equal([true, false, false, false], assignmentPeople.Select(person => person.IsFavorite));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Merge_keeps_survivor_favorite_when_either_person_was_favorite()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 20, 0, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);

            CatalogueReviewPerson favoriteSource = await reviewRepository.CreatePersonAsync("Favorite source", now);
            CatalogueReviewPerson plainTarget = await reviewRepository.CreatePersonAsync("Plain target", now.AddMinutes(1));
            CatalogueReviewPerson plainSource = await reviewRepository.CreatePersonAsync("Plain source", now.AddMinutes(2));
            CatalogueReviewPerson favoriteTarget = await reviewRepository.CreatePersonAsync("Favorite target", now.AddMinutes(3));

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            await FavoriteAsync(client, favoriteSource.Id, true);
            await FavoriteAsync(client, favoriteTarget.Id, true);

            await MergeAsync(client, favoriteSource.Id, plainTarget.Id);
            await MergeAsync(client, plainSource.Id, favoriteTarget.Id);

            PersonMaintenancePersonResponse[] active =
                await client.GetFromJsonAsync<PersonMaintenancePersonResponse[]>(
                    "/api/review/people/maintenance") ?? [];
            Assert.Equal(2, active.Length);
            Assert.All(active, person => Assert.True(person.IsFavorite));
            Assert.Equal(
                ["Favorite target", "Plain target"],
                active.Select(person => person.DisplayName));

            IReadOnlySet<PersonId> favorites = await new SqliteFavoritePeopleRepository(new SqliteCatalogueDatabase(databasePath))
                .GetFavoritePersonIdsAsync();
            Assert.Contains(plainTarget.Id, favorites);
            Assert.Contains(favoriteTarget.Id, favorites);
            Assert.DoesNotContain(favoriteSource.Id, favorites);
            Assert.DoesNotContain(plainSource.Id, favorites);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Favorite_state_does_not_change_identity_match_evidence_or_scores()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 21, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, [1f, 0f, 0f], 0, now);
            FaceOccurrenceId exemplar = await SeedFaceAsync(database, [0.8f, 0.6f, 0f], 1, now);

            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson person = await reviewRepository.CreatePersonAsync("Favorite candidate", now);
            await reviewRepository.AssignAsync(exemplar, person.Id, "human:test", now.AddMinutes(1));

            SqliteIdentityMatcher matcher = new(database, new FixedTimeProvider(now.AddMinutes(2)));
            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            CatalogueRankedIdentitySuggestion before = Assert.Single(
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash));

            await new SqliteFavoritePeopleRepository(database).SetFavoriteAsync(
                person.Id,
                true,
                now.AddMinutes(3));

            _ = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            CatalogueRankedIdentitySuggestion after = Assert.Single(
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash));

            Assert.Equal(before.SuggestedPersonId, after.SuggestedPersonId);
            Assert.Equal(before.Rank, after.Rank);
            Assert.Equal(before.Score, after.Score, 12);
            Assert.Equal(before.ScoreMargin, after.ScoreMargin);
            Assert.Equal(before.Status, after.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task FavoriteAsync(HttpClient client, PersonId personId, bool favorite)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/review/people/{personId}/favorite",
            new SetPersonFavoriteRequest(favorite));
        response.EnsureSuccessStatusCode();
    }

    private static async Task MergeAsync(HttpClient client, PersonId source, PersonId target)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/review/people/{source}/merge",
            new MergePersonRequest(target.ToString(), true, "local-reviewer"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<FaceOccurrenceId> SeedFaceAsync(
        SqliteCatalogueDatabase database,
        float[] vector,
        int index,
        DateTimeOffset now)
    {
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(Path.GetTempPath(), sourceId.ToString()),
            now);
        CatalogueAsset asset = new(assetId, sourceId, $"favorite-photo-{index:000}.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string("abcdef"[index % 6], 64)),
            1234,
            now,
            "image/jpeg",
            640,
            480);
        CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);

        FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
        FaceCropId cropId = FaceCropId.New();
        CatalogueFaceInspection inspection = new(
            new CatalogueFaceOccurrence(occurrenceId, persistedRevision.Id, 0, now.AddMinutes(index)),
            new CatalogueFaceObservation(
                occurrenceId,
                new ModelId("yunet"),
                new Sha256Digest(new string('b', 64)),
                0.95,
                new NormalizedBoundingBox(0.1, 0.1, 0.4, 0.5),
                CreateLandmarks(),
                now.AddMinutes(index)),
            new CatalogueFaceCrop(
                cropId,
                occurrenceId,
                new AlignmentProtocolId("sface-five-point-v1"),
                new Sha256Digest(new string('c', 64)),
                $"faces/favorites/{index:000}/aligned.png",
                112,
                112,
                now.AddMinutes(index)),
            new CatalogueFaceEmbedding(
                cropId,
                EmbeddingModelId,
                EmbeddingModelHash,
                new EmbeddingVector(vector),
                now.AddMinutes(index)));

        CatalogueFaceInspection persisted = await new SqliteFaceCatalogueRepository(database).SaveInspectionAsync(
            inspection.Occurrence,
            inspection.Observation,
            inspection.Crop,
            inspection.Embedding);
        return persisted.Occurrence.Id;
    }

    private static NormalizedFaceLandmarks CreateLandmarks() =>
        new(
            new NormalizedPoint(0.2, 0.25),
            new NormalizedPoint(0.4, 0.25),
            new NormalizedPoint(0.3, 0.35),
            new NormalizedPoint(0.24, 0.48),
            new NormalizedPoint(0.36, 0.48));

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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
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
