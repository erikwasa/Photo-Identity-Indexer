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

public sealed class UnknownReviewStateApplicationTests
{
    private static readonly ModelId EmbeddingModelId = new("sface");
    private static readonly Sha256Digest EmbeddingModelHash = new(new string('e', 64));

    [Fact]
    public async Task Unknown_is_distinct_reversible_and_manual_assignment_supersedes_without_erasing_history()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 20, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId faceId = await SeedFaceAsync(database, directory, [1f, 0f, 0f], 0, now);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage unknownResponse = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/unknown",
                new ReviewFaceActionRequest("human:test", "Identity is not known."));
            unknownResponse.EnsureSuccessStatusCode();
            ReviewActionResponse unknownAction =
                (await unknownResponse.Content.ReadFromJsonAsync<ReviewActionResponse>())!;
            Assert.Equal("unknown", unknownAction.Kind);
            Assert.Null(unknownAction.Person);

            ReviewFaceDetailsResponse unknownDetails =
                (await client.GetFromJsonAsync<ReviewFaceDetailsResponse>($"/api/review/faces/{faceId}?state=all"))!;
            Assert.Equal("unknown", unknownDetails.Face.State);
            Assert.Null(unknownDetails.Face.Person);

            ReviewFacePageResponse unknownQueue =
                (await client.GetFromJsonAsync<ReviewFacePageResponse>("/api/review/faces?state=unknown"))!;
            Assert.Equal(1, unknownQueue.Total);
            Assert.Equal(faceId.ToString(), Assert.Single(unknownQueue.Items).Id);

            ReviewFacePageResponse rejectedQueue =
                (await client.GetFromJsonAsync<ReviewFacePageResponse>("/api/review/faces?state=rejected"))!;
            Assert.Equal(0, rejectedQueue.Total);

            using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                "/api/review/people",
                new CreatePersonRequest("Known Later"));
            createResponse.EnsureSuccessStatusCode();
            ReviewPersonResponse person =
                (await createResponse.Content.ReadFromJsonAsync<ReviewPersonResponse>())!;

            using HttpResponseMessage assignResponse = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/assign",
                new AssignFaceRequest(person.Id, "human:test", "Identified later."));
            assignResponse.EnsureSuccessStatusCode();

            ReviewFaceDetailsResponse assignedDetails =
                (await client.GetFromJsonAsync<ReviewFaceDetailsResponse>($"/api/review/faces/{faceId}?state=all"))!;
            Assert.Equal("assigned", assignedDetails.Face.State);
            Assert.Equal(person.Id, assignedDetails.Face.Person?.Id);
            Assert.Contains(assignedDetails.Actions, action => action.Kind == "unknown" && !action.Reversed);
            Assert.Contains(assignedDetails.Actions, action => action.Kind == "assign" && !action.Reversed);

            using HttpResponseMessage firstUndo = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/undo",
                new ReviewFaceActionRequest("human:test", "Undo later assignment."));
            firstUndo.EnsureSuccessStatusCode();

            ReviewFaceDetailsResponse restoredUnknown =
                (await client.GetFromJsonAsync<ReviewFaceDetailsResponse>($"/api/review/faces/{faceId}?state=all"))!;
            Assert.Equal("unknown", restoredUnknown.Face.State);
            Assert.Null(restoredUnknown.Face.Person);
            Assert.Contains(restoredUnknown.Actions, action => action.Kind == "assign" && action.Reversed);
            Assert.Contains(restoredUnknown.Actions, action => action.Kind == "unknown" && !action.Reversed);

            using HttpResponseMessage secondUndo = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/undo",
                new ReviewFaceActionRequest("human:test", "Return to normal review."));
            secondUndo.EnsureSuccessStatusCode();

            ReviewFaceDetailsResponse unreviewedDetails =
                (await client.GetFromJsonAsync<ReviewFaceDetailsResponse>($"/api/review/faces/{faceId}?state=all"))!;
            Assert.Equal("unreviewed", unreviewedDetails.Face.State);
            Assert.Equal(4, unreviewedDetails.Actions.Count);
            Assert.Contains(unreviewedDetails.Actions, action => action.Kind == "unknown" && action.Reversed);
            Assert.Equal(2, unreviewedDetails.Actions.Count(action => action.Kind == "undo"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Unknown_is_excluded_from_normal_matching_auto_assignment_and_collections_but_opt_in_rematch_is_advisory()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 20, 30, 0, TimeSpan.Zero);
            FaceOccurrenceId target = await SeedFaceAsync(database, directory, [1f, 0f, 0f], 0, now);
            FaceOccurrenceId firstExemplar = await SeedFaceAsync(database, directory, [1f, 0f, 0f], 1, now);
            FaceOccurrenceId secondExemplar = await SeedFaceAsync(database, directory, [0f, 1f, 0f], 2, now);

            SqliteReviewRepository review = new(database);
            CatalogueReviewPerson firstPerson = await review.CreatePersonAsync("First", now);
            CatalogueReviewPerson secondPerson = await review.CreatePersonAsync("Second", now.AddSeconds(1));
            await review.AssignAsync(firstExemplar, firstPerson.Id, "human:test", now.AddMinutes(1));
            await review.AssignAsync(secondExemplar, secondPerson.Id, "human:test", now.AddMinutes(2));
            await review.MarkUnknownAsync(target, "human:test", now.AddMinutes(3), "Real face, unknown identity.");

            SqliteIdentityMatcher matcher = new(database);
            IdentityMatchSummary normal = await matcher.RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);
            Assert.Equal(new IdentityMatchSummary(0, 0, 0), normal);
            Assert.Empty(await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash));

            IdentityMatchSummary rematch = await matcher.RegenerateAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                IdentityMatchTargetScope.UnreviewedAndUnknown);
            Assert.Equal(new IdentityMatchSummary(1, 1, 2), rematch);
            IReadOnlyList<CatalogueRankedIdentitySuggestion> ranked =
                await matcher.GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash);
            Assert.Equal(2, ranked.Count);
            Assert.Equal(firstPerson.Id, ranked[0].SuggestedPersonId);
            Assert.Equal(1d, ranked[0].Score, 6);
            Assert.Equal(1d, Assert.IsType<double>(ranked[0].ScoreMargin), 6);

            CatalogueReviewFace stillUnknown = Assert.IsType<CatalogueReviewFace>(await review.GetFaceAsync(target));
            Assert.Equal(CatalogueReviewStates.Unknown, stillUnknown.State);
            Assert.Null(stillUnknown.Person);

            _ = await new SqliteIdentitySuggestionPolicyRepository(database).UpdateAsync(
                EmbeddingModelId,
                EmbeddingModelHash,
                autoAssignEnabled: true,
                highScoreThreshold: 0.8,
                highMarginThreshold: 0.2,
                mediumScoreThreshold: 0.5,
                actor: "human:test");
            IdentityAutoAssignmentSummary autoSummary = await new SqliteIdentityAutoAssignmentService(database)
                .ApplyAsync(EmbeddingModelId, EmbeddingModelHash);
            Assert.Equal(new IdentityAutoAssignmentSummary(0, 0, 0), autoSummary);
            Assert.Equal(
                CatalogueReviewStates.Unknown,
                Assert.IsType<CatalogueReviewFace>(await review.GetFaceAsync(target)).State);

            CatalogueCollectionPhotoPage collection = await new SqliteCollectionQueryRepository(database)
                .QueryPhotosAsync(
                    [firstPerson.Id],
                    CatalogueCollectionMatchModes.Any,
                    new CatalogueCollectionSuggestionPolicy(EmbeddingModelId, EmbeddingModelHash, -1),
                    CatalogueCollectionReviewStates.Unreviewed);
            Assert.Empty(collection.Items);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Unknown_supersedes_an_older_assignment_for_exemplar_selection()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 10, 21, 0, 0, TimeSpan.Zero);
            FaceOccurrenceId hiddenExemplar = await SeedFaceAsync(database, directory, [1f, 0f, 0f], 0, now);
            FaceOccurrenceId target = await SeedFaceAsync(database, directory, [1f, 0f, 0f], 1, now);

            SqliteReviewRepository review = new(database);
            CatalogueReviewPerson person = await review.CreatePersonAsync("Hidden exemplar", now);
            await review.AssignAsync(hiddenExemplar, person.Id, "human:test", now.AddMinutes(1));
            await review.MarkUnknownAsync(hiddenExemplar, "human:test", now.AddMinutes(2));

            IdentityMatchSummary summary = await new SqliteIdentityMatcher(database)
                .RegenerateAsync(EmbeddingModelId, EmbeddingModelHash);

            Assert.Equal(new IdentityMatchSummary(1, 0, 0), summary);
            Assert.Empty(await new SqliteIdentityMatcher(database)
                .GetRankedSuggestionsAsync(target, EmbeddingModelId, EmbeddingModelHash));
            Assert.Equal(
                CatalogueReviewStates.Unknown,
                Assert.IsType<CatalogueReviewFace>(await review.GetFaceAsync(hiddenExemplar)).State);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<FaceOccurrenceId> SeedFaceAsync(
        SqliteCatalogueDatabase database,
        string directory,
        float[] vector,
        int index,
        DateTimeOffset now)
    {
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(directory, $"source-{index}"),
            now);
        CatalogueAsset asset = new(assetId, sourceId, $"photo-{index:000}.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string("abcdef"[index % 6], 64)),
            1234,
            now.AddSeconds(index),
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
                $"faces/{index:000}/aligned.png",
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
