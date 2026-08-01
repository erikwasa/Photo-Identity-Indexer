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
            Assert.Null(defaultPage.Query.SuggestionPolicy);
            CollectionPhotoResponse onlyTogether = Assert.Single(defaultPage.Items);
            Assert.Equal(together.RevisionId.ToString(), onlyTogether.RevisionId);
            Assert.Equal(2, onlyTogether.People.Count);
            Assert.All(onlyTogether.People, person =>
            {
                Assert.Equal(1, person.ConfirmedFaceCount);
                Assert.Equal(0, person.SuggestedFaceCount);
                Assert.Null(person.MaximumSuggestionScore);
            });

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
    public async Task Suggestion_backed_queries_are_explicit_exact_model_and_threshold_scoped()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceRoot = Path.Combine(directory, "private-family-archive");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 8, 1, 18, 0, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson ada = await reviewRepository.CreatePersonAsync("Ada Lovelace", now);
            CatalogueReviewPerson grace = await reviewRepository.CreatePersonAsync("Grace Hopper", now.AddSeconds(1));
            CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);

            SeededPhoto mixedEvidence = await SeedPhotoAsync(
                database,
                source,
                "family/private-mixed-evidence.jpg",
                'e',
                now.AddMinutes(1),
                0.97,
                0.96);
            await reviewRepository.AssignAsync(
                mixedEvidence.Faces[0],
                ada.Id,
                "human:test",
                now.AddMinutes(2));

            string modelId = "sface-baseline";
            string modelHash = new('f', 64);
            await SeedSuggestionAsync(
                database,
                mixedEvidence.Faces[1],
                grace.Id,
                modelId,
                modelHash,
                score: 0.92,
                scoreMargin: 0.11,
                now.AddMinutes(3));

            await using CollectionApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string people = $"{ada.Id},{grace.Id}";

            CollectionPhotoPageResponse confirmedOnly =
                await client.GetFromJsonAsync<CollectionPhotoPageResponse>(
                    $"/api/collections/photos?people={people}&match=all")
                ?? throw new InvalidOperationException("The confirmed collection response was empty.");
            Assert.True(confirmedOnly.Query.ConfirmedOnly);
            Assert.Empty(confirmedOnly.Items);

            using HttpResponseMessage suggestedResponse = await client.GetAsync(
                $"/api/collections/photos?people={people}&match=all" +
                $"&includeSuggestions=true&suggestionModelId={modelId}" +
                $"&suggestionModelHash={modelHash}&minimumSuggestionScore=0.9");
            suggestedResponse.EnsureSuccessStatusCode();
            CollectionPhotoPageResponse suggestedPage = Assert.IsType<CollectionPhotoPageResponse>(
                await suggestedResponse.Content.ReadFromJsonAsync<CollectionPhotoPageResponse>());

            Assert.False(suggestedPage.Query.ConfirmedOnly);
            CollectionSuggestionPolicyResponse policy = Assert.IsType<CollectionSuggestionPolicyResponse>(
                suggestedPage.Query.SuggestionPolicy);
            Assert.Equal(modelId, policy.ModelId);
            Assert.Equal(modelHash, policy.ModelHash);
            Assert.Equal(0.9, policy.MinimumScore, 6);

            CollectionPhotoResponse photo = Assert.Single(suggestedPage.Items);
            Assert.Equal(mixedEvidence.RevisionId.ToString(), photo.RevisionId);
            CollectionPersonMatchResponse adaMatch = Assert.Single(
                photo.People,
                person => person.Id == ada.Id.ToString());
            Assert.Equal(1, adaMatch.ConfirmedFaceCount);
            Assert.Equal(0, adaMatch.SuggestedFaceCount);
            Assert.Null(adaMatch.MaximumSuggestionScore);
            CollectionPersonMatchResponse graceMatch = Assert.Single(
                photo.People,
                person => person.Id == grace.Id.ToString());
            Assert.Equal(0, graceMatch.ConfirmedFaceCount);
            Assert.Equal(1, graceMatch.SuggestedFaceCount);
            Assert.Equal(0.92, Assert.IsType<double>(graceMatch.MaximumSuggestionScore), 6);

            string suggestedJson = JsonSerializer.Serialize(suggestedPage);
            Assert.DoesNotContain(sourceRoot, suggestedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-mixed-evidence.jpg", suggestedJson, StringComparison.OrdinalIgnoreCase);

            CollectionPhotoPageResponse stricterThreshold =
                await client.GetFromJsonAsync<CollectionPhotoPageResponse>(
                    $"/api/collections/photos?people={people}&match=all" +
                    $"&includeSuggestions=true&suggestionModelId={modelId}" +
                    $"&suggestionModelHash={modelHash}&minimumSuggestionScore=0.95")
                ?? throw new InvalidOperationException("The strict-threshold collection response was empty.");
            Assert.Empty(stricterThreshold.Items);

            string otherHash = new('a', 64);
            CollectionPhotoPageResponse otherRevision =
                await client.GetFromJsonAsync<CollectionPhotoPageResponse>(
                    $"/api/collections/photos?people={people}&match=all" +
                    $"&includeSuggestions=true&suggestionModelId={modelId}" +
                    $"&suggestionModelHash={otherHash}&minimumSuggestionScore=0.9")
                ?? throw new InvalidOperationException("The other-revision collection response was empty.");
            Assert.Empty(otherRevision.Items);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Collection_query_rejects_missing_people_unsupported_modes_and_implicit_suggestion_scope()
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

            using HttpResponseMessage missingSuggestionScope = await client.GetAsync(
                $"/api/collections/photos?people={PersonId.New()}&includeSuggestions=true");
            Assert.Equal(HttpStatusCode.BadRequest, missingSuggestionScope.StatusCode);

            using HttpResponseMessage implicitSuggestions = await client.GetAsync(
                $"/api/collections/photos?people={PersonId.New()}" +
                $"&suggestionModelId=sface-baseline&suggestionModelHash={new string('b', 64)}" +
                "&minimumSuggestionScore=0.9");
            Assert.Equal(HttpStatusCode.BadRequest, implicitSuggestions.StatusCode);

            using HttpResponseMessage invalidThreshold = await client.GetAsync(
                $"/api/collections/photos?people={PersonId.New()}&includeSuggestions=true" +
                $"&suggestionModelId=sface-baseline&suggestionModelHash={new string('b', 64)}" +
                "&minimumSuggestionScore=1.1");
            Assert.Equal(HttpStatusCode.BadRequest, invalidThreshold.StatusCode);
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

    private static async Task SeedSuggestionAsync(
        SqliteCatalogueDatabase database,
        FaceOccurrenceId faceId,
        PersonId personId,
        string modelId,
        string modelHash,
        double score,
        double? scoreMargin,
        DateTimeOffset generatedAtUtc)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity_suggestions (
                face_occurrence_id,
                suggested_person_id,
                model_id,
                model_hash,
                score,
                status,
                created_at_utc)
            VALUES (
                $face_id,
                $person_id,
                $model_id,
                $model_hash,
                $score,
                'pending',
                $generated_at_utc);

            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id,
                model_id,
                model_hash,
                rank,
                suggestion_id,
                score_margin,
                generated_at_utc)
            SELECT
                $face_id,
                $model_id,
                $model_hash,
                1,
                id,
                $score_margin,
                $generated_at_utc
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_id
              AND suggested_person_id = $person_id
              AND model_id = $model_id
              AND model_hash = $model_hash;
            """;
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$model_id", modelId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$score", score);
        command.Parameters.AddWithValue(
            "$score_margin",
            scoreMargin is null ? DBNull.Value : scoreMargin.Value);
        command.Parameters.AddWithValue("$generated_at_utc", generatedAtUtc.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync();
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
