using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewSuggestionGalleryApplicationTests
{
    [Fact]
    public async Task Gallery_returns_top_suggestions_and_stable_task_oriented_sorts()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededGallery seeded = await SeedGalleryAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string scope =
                $"modelId=sface&modelHash={seeded.ModelHash}" +
                $"&processingRunId={seeded.RunId}";

            ReviewFacePageResponse byPerson = await GetPageAsync(client, scope, "suggested-person");
            Assert.Equal(
                new[] { seeded.AdaFaceId, seeded.BobFaceId, seeded.NoSuggestionFaceId },
                byPerson.Items.Select(face => face.Id).ToArray());

            ReviewTopSuggestionResponse adaSuggestion = Assert.IsType<ReviewTopSuggestionResponse>(
                byPerson.Items[0].TopSuggestion);
            Assert.Equal("Ada", adaSuggestion.Person.DisplayName);
            Assert.Equal(0.91, adaSuggestion.Score, 6);
            Assert.Equal(0.20, Assert.IsType<double>(adaSuggestion.ScoreMargin), 6);
            Assert.Equal("sface", adaSuggestion.ModelId);
            Assert.Equal(seeded.ModelHash, adaSuggestion.ModelHash);
            Assert.Equal(IdentitySuggestionConfidenceGroups.High, adaSuggestion.ConfidenceGroup);

            ReviewTopSuggestionResponse bobSuggestion = Assert.IsType<ReviewTopSuggestionResponse>(
                byPerson.Items[1].TopSuggestion);
            Assert.Equal(IdentitySuggestionConfidenceGroups.Medium, bobSuggestion.ConfidenceGroup);
            Assert.Null(byPerson.Items[2].TopSuggestion);

            ReviewFacePageResponse confidenceFirst = await GetPageAsync(client, scope, "confidence-group");
            Assert.Equal(
                new[] { seeded.AdaFaceId, seeded.BobFaceId, seeded.NoSuggestionFaceId },
                confidenceFirst.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse highOnly = await GetPageAsync(
                client,
                scope,
                "confidence-group",
                IdentitySuggestionConfidenceGroups.High);
            Assert.Equal(new[] { seeded.AdaFaceId }, highOnly.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse mediumOnly = await GetPageAsync(
                client,
                scope,
                "confidence-group",
                IdentitySuggestionConfidenceGroups.Medium);
            Assert.Equal(new[] { seeded.BobFaceId }, mediumOnly.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse lowOnly = await GetPageAsync(
                client,
                scope,
                "confidence-group",
                IdentitySuggestionConfidenceGroups.Low);
            Assert.Empty(lowOnly.Items);
            Assert.Equal(0, lowOnly.Total);

            ReviewFacePageResponse easyFirst = await GetPageAsync(client, scope, "margin-desc");
            Assert.Equal(
                new[] { seeded.AdaFaceId, seeded.BobFaceId, seeded.NoSuggestionFaceId },
                easyFirst.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse ambiguousFirst = await GetPageAsync(client, scope, "margin-asc");
            Assert.Equal(
                new[] { seeded.BobFaceId, seeded.AdaFaceId, seeded.NoSuggestionFaceId },
                ambiguousFirst.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse scoreFirst = await GetPageAsync(client, scope, "score-desc");
            Assert.Equal(
                new[] { seeded.AdaFaceId, seeded.BobFaceId, seeded.NoSuggestionFaceId },
                scoreFirst.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse noSuggestionFirst = await GetPageAsync(client, scope, "no-suggestion-first");
            Assert.Equal(seeded.NoSuggestionFaceId, noSuggestionFirst.Items[0].Id);

            string json = await client.GetStringAsync(
                $"/api/review/suggestion-faces?state=unreviewed&{scope}&sort=suggested-person");
            Assert.DoesNotContain(seeded.SourceRoot, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storage_path", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("embedding", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Quick_details_navigation_uses_the_selected_suggestion_order_after_mutation()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededGallery seeded = await SeedGalleryAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string modelScope =
                $"modelId=sface&modelHash={seeded.ModelHash}" +
                $"&processingRunId={seeded.RunId}";
            string detailsScope = $"state=unreviewed&{modelScope}&sort=suggested-person";

            ReviewFaceDetailsResponse bobDetails = Assert.IsType<ReviewFaceDetailsResponse>(
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                    $"/api/review/suggestion-faces/{seeded.BobFaceId}?{detailsScope}"));
            ReviewFaceNavigationResponse navigation = Assert.IsType<ReviewFaceNavigationResponse>(
                bobDetails.Navigation);
            Assert.Equal(seeded.AdaFaceId, navigation.PreviousFaceId);
            Assert.Equal(seeded.NoSuggestionFaceId, navigation.NextFaceId);
            Assert.Equal(2, navigation.Position);
            Assert.Equal(3, navigation.Total);
            Assert.Equal("suggested-person", navigation.Sort);

            ReviewFacePageResponse page = await GetPageAsync(client, modelScope, "suggested-person");
            ReviewTopSuggestionResponse bobSuggestion = Assert.IsType<ReviewTopSuggestionResponse>(
                page.Items.Single(face => face.Id == seeded.BobFaceId).TopSuggestion);
            using HttpResponseMessage accepted = await client.PostAsJsonAsync(
                $"/api/review/faces/{seeded.BobFaceId}/suggestions/{bobSuggestion.Id}/accept",
                new ReviewSuggestionActionRequest("suggestion-gallery:test"));
            accepted.EnsureSuccessStatusCode();

            ReviewFaceDetailsResponse noSuggestionDetails = Assert.IsType<ReviewFaceDetailsResponse>(
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                    $"/api/review/suggestion-faces/{seeded.NoSuggestionFaceId}?{detailsScope}"));
            ReviewFaceNavigationResponse afterMutation = Assert.IsType<ReviewFaceNavigationResponse>(
                noSuggestionDetails.Navigation);
            Assert.Equal(seeded.AdaFaceId, afterMutation.PreviousFaceId);
            Assert.Null(afterMutation.NextFaceId);
            Assert.Equal(2, afterMutation.Position);
            Assert.Equal(2, afterMutation.Total);

            string highDetailsScope =
                $"state=unreviewed&{modelScope}&sort=confidence-group&confidenceGroup=high";
            ReviewFaceDetailsResponse adaDetails = Assert.IsType<ReviewFaceDetailsResponse>(
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                    $"/api/review/suggestion-faces/{seeded.AdaFaceId}?{highDetailsScope}"));
            ReviewFaceNavigationResponse highNavigation = Assert.IsType<ReviewFaceNavigationResponse>(
                adaDetails.Navigation);
            Assert.Null(highNavigation.PreviousFaceId);
            Assert.Null(highNavigation.NextFaceId);
            Assert.Equal(1, highNavigation.Position);
            Assert.Equal(1, highNavigation.Total);
            Assert.Equal("confidence-group", highNavigation.Sort);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Gallery_requires_exact_model_revision_and_rejects_unknown_sort_or_confidence_group()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededGallery seeded = await SeedGalleryAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage missingModel = await client.GetAsync(
                "/api/review/suggestion-faces?state=unreviewed&sort=score-desc");
            Assert.Equal(HttpStatusCode.BadRequest, missingModel.StatusCode);

            using HttpResponseMessage invalidSort = await client.GetAsync(
                $"/api/review/suggestion-faces?state=unreviewed" +
                $"&modelId=sface&modelHash={seeded.ModelHash}&sort=alphabetical");
            Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);

            using HttpResponseMessage invalidConfidenceGroup = await client.GetAsync(
                $"/api/review/suggestion-faces?state=unreviewed" +
                $"&modelId=sface&modelHash={seeded.ModelHash}" +
                "&sort=confidence-group&confidenceGroup=very-high");
            Assert.Equal(HttpStatusCode.BadRequest, invalidConfidenceGroup.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<ReviewFacePageResponse> GetPageAsync(
        HttpClient client,
        string scope,
        string sort,
        string confidenceGroup = CatalogueSuggestionConfidenceFilters.All) =>
        Assert.IsType<ReviewFacePageResponse>(
            await client.GetFromJsonAsync<ReviewFacePageResponse>(
                $"/api/review/suggestion-faces?state=unreviewed&{scope}&sort={sort}" +
                $"&confidenceGroup={confidenceGroup}&limit=10"));

    private static async Task<SeededGallery> SeedGalleryAsync(SqliteCatalogueDatabase database)
    {
        string sourceRoot = Path.Combine(
            Path.GetTempPath(),
            "private-suggestion-gallery",
            Guid.NewGuid().ToString("N"));
        string sourceId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string runId = Guid.NewGuid().ToString("D");
        string adaFaceId = Guid.NewGuid().ToString("D");
        string bobFaceId = Guid.NewGuid().ToString("D");
        string noSuggestionFaceId = Guid.NewGuid().ToString("D");
        string modelHash = new('a', 64);
        string now = new DateTimeOffset(2026, 7, 30, 21, 30, 0, TimeSpan.Zero).ToString("O");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $source_root, $now);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES
                    ($ada_person_id, 'Ada', $now, NULL),
                    ($bob_person_id, 'Bob', $now, NULL);
            INSERT INTO processing_runs (id, status, configuration_json, started_at_utc, completed_at_utc)
                VALUES ($run_id, 'completed', '{}', $now, $now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES
                    ($ada_asset_id, $source_id, 'ada.jpg', $now, $now),
                    ($bob_asset_id, $source_id, 'bob.jpg', $now, $now),
                    ($none_asset_id, $source_id, 'none.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES
                    ($ada_revision_id, $ada_asset_id, $ada_hash, 100, $now, 'image/jpeg', 640, 480),
                    ($bob_revision_id, $bob_asset_id, $bob_hash, 100, $now, 'image/jpeg', 640, 480),
                    ($none_revision_id, $none_asset_id, $none_hash, 100, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($ada_face_id, $ada_revision_id, 0, $ada_created),
                    ($bob_face_id, $bob_revision_id, 0, $bob_created),
                    ($none_face_id, $none_revision_id, 0, $none_created);
            INSERT INTO processing_jobs (
                id, processing_run_id, asset_revision_id, status, attempt_count,
                available_at_utc, completed_at_utc, idempotency_key)
                VALUES
                    ($ada_job_id, $run_id, $ada_revision_id, 'completed', 1, $now, $now, $ada_key),
                    ($bob_job_id, $run_id, $bob_revision_id, 'completed', 1, $now, $now, $bob_key),
                    ($none_job_id, $run_id, $none_revision_id, 'completed', 1, $now, $now, $none_key);
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($ada_face_id, $ada_person_id, 'sface', $model_hash, 0.91, 'pending', $now),
                    ($bob_face_id, $bob_person_id, 'sface', $model_hash, 0.88, 'pending', $now);
            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
                SELECT
                    face_occurrence_id,
                    model_id,
                    model_hash,
                    1,
                    id,
                    CASE WHEN face_occurrence_id = $ada_face_id THEN 0.20 ELSE 0.05 END,
                    created_at_utc
                FROM identity_suggestions;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$ada_person_id", adaPersonId);
        command.Parameters.AddWithValue("$bob_person_id", bobPersonId);
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$now", now);

        string[] names = ["ada", "bob", "none"];
        string[] faceIds = [adaFaceId, bobFaceId, noSuggestionFaceId];
        for (int index = 0; index < names.Length; index++)
        {
            string name = names[index];
            string revisionId = Guid.NewGuid().ToString("D");
            command.Parameters.AddWithValue($"${name}_asset_id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"${name}_revision_id", revisionId);
            command.Parameters.AddWithValue($"${name}_hash", new string((char)('b' + index), 64));
            command.Parameters.AddWithValue($"${name}_face_id", faceIds[index]);
            command.Parameters.AddWithValue($"${name}_job_id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"${name}_key", $"{runId}:{revisionId}");
        }

        command.Parameters.AddWithValue(
            "$ada_created",
            new DateTimeOffset(2026, 7, 30, 21, 3, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue(
            "$bob_created",
            new DateTimeOffset(2026, 7, 30, 21, 2, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue(
            "$none_created",
            new DateTimeOffset(2026, 7, 30, 21, 1, 0, TimeSpan.Zero).ToString("O"));
        await command.ExecuteNonQueryAsync();

        return new SeededGallery(
            sourceRoot,
            runId,
            modelHash,
            adaFaceId,
            bobFaceId,
            noSuggestionFaceId);
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

    private sealed record SeededGallery(
        string SourceRoot,
        string RunId,
        string ModelHash,
        string AdaFaceId,
        string BobFaceId,
        string NoSuggestionFaceId);

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
