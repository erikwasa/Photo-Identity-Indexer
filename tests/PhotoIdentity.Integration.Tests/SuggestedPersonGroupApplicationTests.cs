using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SuggestedPersonGroupApplicationTests
{
    [Fact]
    public async Task Groups_are_exact_model_pending_unreviewed_and_page_deterministically()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededGroups seed = await SeedAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            ReviewSuggestedPersonGroupPageResponse first = Assert.IsType<ReviewSuggestedPersonGroupPageResponse>(
                await client.GetFromJsonAsync<ReviewSuggestedPersonGroupPageResponse>(
                    $"/api/review/suggestion-faces/groups?modelId=sface&modelHash={seed.ModelHash}&offset=0&limit=1"));
            Assert.Equal(2, first.Total);
            ReviewSuggestedPersonGroupResponse ada = Assert.Single(first.Items);
            Assert.Equal("Ada", ada.Person.DisplayName);
            Assert.True(ada.IsFavorite);
            Assert.Equal(2, ada.PendingCount);
            Assert.Equal(1, ada.HighCount);
            Assert.Equal(1, ada.MediumCount);
            Assert.Equal(0, ada.LowCount);
            Assert.Equal(seed.AdaHighFaceId, ada.RepresentativeFaceIds[0]);
            Assert.Equal(seed.AdaMediumFaceId, ada.RepresentativeFaceIds[1]);
            Assert.Equal(2, ada.RepresentativeImageUrls.Count);

            ReviewSuggestedPersonGroupPageResponse second = Assert.IsType<ReviewSuggestedPersonGroupPageResponse>(
                await client.GetFromJsonAsync<ReviewSuggestedPersonGroupPageResponse>(
                    $"/api/review/suggestion-faces/groups?modelId=sface&modelHash={seed.ModelHash}&offset=1&limit=1"));
            ReviewSuggestedPersonGroupResponse bob = Assert.Single(second.Items);
            Assert.Equal("Bob", bob.Person.DisplayName);
            Assert.Equal(1, bob.PendingCount);
            Assert.Equal(0, bob.HighCount);
            Assert.Equal(0, bob.MediumCount);
            Assert.Equal(1, bob.LowCount);

            ReviewSuggestedPersonGroupPageResponse otherModel = Assert.IsType<ReviewSuggestedPersonGroupPageResponse>(
                await client.GetFromJsonAsync<ReviewSuggestedPersonGroupPageResponse>(
                    $"/api/review/suggestion-faces/groups?modelId=sface&modelHash={seed.OtherModelHash}&offset=0&limit=10"));
            ReviewSuggestedPersonGroupResponse isolated = Assert.Single(otherModel.Items);
            Assert.Equal("Ada", isolated.Person.DisplayName);
            Assert.Equal(1, isolated.PendingCount);

            using HttpResponseMessage missingModel = await client.GetAsync(
                "/api/review/suggestion-faces/groups?offset=0&limit=10");
            Assert.Equal(HttpStatusCode.BadRequest, missingModel.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Group_counts_drop_after_suggestion_is_rejected()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededGroups seed = await SeedAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage rejected = await client.PostAsJsonAsync(
                $"/api/review/faces/{seed.BobLowFaceId}/suggestions/{seed.BobLowSuggestionId}/reject",
                new ReviewSuggestionActionRequest("suggested-groups:test", "intentional exception"));
            rejected.EnsureSuccessStatusCode();

            ReviewSuggestedPersonGroupPageResponse after = Assert.IsType<ReviewSuggestedPersonGroupPageResponse>(
                await client.GetFromJsonAsync<ReviewSuggestedPersonGroupPageResponse>(
                    $"/api/review/suggestion-faces/groups?modelId=sface&modelHash={seed.ModelHash}&offset=0&limit=10"));
            Assert.Equal(1, after.Total);
            ReviewSuggestedPersonGroupResponse remaining = Assert.Single(after.Items);
            Assert.Equal("Ada", remaining.Person.DisplayName);
            Assert.DoesNotContain(after.Items, group => group.Person.DisplayName == "Bob");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededGroups> SeedAsync(SqliteCatalogueDatabase database)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string revisionId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string caraPersonId = Guid.NewGuid().ToString("D");
        string adaHighFaceId = Guid.NewGuid().ToString("D");
        string adaMediumFaceId = Guid.NewGuid().ToString("D");
        string bobLowFaceId = Guid.NewGuid().ToString("D");
        string bobRejectedFaceId = Guid.NewGuid().ToString("D");
        string caraReviewedFaceId = Guid.NewGuid().ToString("D");
        string otherModelFaceId = Guid.NewGuid().ToString("D");
        string modelHash = new('a', 64);
        string otherModelHash = new('b', 64);
        string revisionHash = new('c', 64);
        string now = new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero).ToString("O");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', 'C:/private/photos', $now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES ($asset_id, $source_id, 'group.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES ($revision_id, $asset_id, $revision_hash, 100, $now, 'image/jpeg', 1000, 800);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES
                    ($ada_person_id, 'Ada', $now, NULL),
                    ($bob_person_id, 'Bob', $now, NULL),
                    ($cara_person_id, 'Cara', $now, NULL);
            INSERT INTO person_favorites (person_id, favorited_at_utc)
                VALUES ($ada_person_id, $now);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($ada_high_face_id, $revision_id, 0, $now),
                    ($ada_medium_face_id, $revision_id, 1, $now),
                    ($bob_low_face_id, $revision_id, 2, $now),
                    ($bob_rejected_face_id, $revision_id, 3, $now),
                    ($cara_reviewed_face_id, $revision_id, 4, $now),
                    ($other_model_face_id, $revision_id, 5, $now);
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($ada_high_face_id, $ada_person_id, 'sface', $model_hash, 0.91, 'pending', $now),
                    ($ada_medium_face_id, $ada_person_id, 'sface', $model_hash, 0.60, 'pending', $now),
                    ($bob_low_face_id, $bob_person_id, 'sface', $model_hash, 0.40, 'pending', $now),
                    ($bob_rejected_face_id, $bob_person_id, 'sface', $model_hash, 0.95, 'rejected', $now),
                    ($cara_reviewed_face_id, $cara_person_id, 'sface', $model_hash, 0.96, 'pending', $now),
                    ($other_model_face_id, $ada_person_id, 'sface', $other_model_hash, 0.99, 'pending', $now);
            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
                SELECT
                    face_occurrence_id,
                    model_id,
                    model_hash,
                    1,
                    id,
                    CASE
                        WHEN face_occurrence_id = $ada_high_face_id THEN 0.20
                        WHEN face_occurrence_id = $ada_medium_face_id THEN 0.05
                        WHEN face_occurrence_id = $bob_low_face_id THEN 0.02
                        ELSE 0.25
                    END,
                    created_at_utc
                FROM identity_suggestions;
            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, note, created_at_utc, reversed_at_utc, reverses_action_id)
                VALUES ($cara_reviewed_face_id, 'unknown', NULL, NULL, 'seed', NULL, $now, NULL, NULL);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$revision_id", revisionId);
        command.Parameters.AddWithValue("$revision_hash", revisionHash);
        command.Parameters.AddWithValue("$ada_person_id", adaPersonId);
        command.Parameters.AddWithValue("$bob_person_id", bobPersonId);
        command.Parameters.AddWithValue("$cara_person_id", caraPersonId);
        command.Parameters.AddWithValue("$ada_high_face_id", adaHighFaceId);
        command.Parameters.AddWithValue("$ada_medium_face_id", adaMediumFaceId);
        command.Parameters.AddWithValue("$bob_low_face_id", bobLowFaceId);
        command.Parameters.AddWithValue("$bob_rejected_face_id", bobRejectedFaceId);
        command.Parameters.AddWithValue("$cara_reviewed_face_id", caraReviewedFaceId);
        command.Parameters.AddWithValue("$other_model_face_id", otherModelFaceId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$other_model_hash", otherModelHash);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();

        using SqliteCommand readSuggestion = connection.CreateCommand();
        readSuggestion.CommandText =
            "SELECT id FROM identity_suggestions WHERE face_occurrence_id = $face_id AND status = 'pending';";
        readSuggestion.Parameters.AddWithValue("$face_id", bobLowFaceId);
        long bobLowSuggestionId = Convert.ToInt64(await readSuggestion.ExecuteScalarAsync());

        return new SeededGroups(
            modelHash,
            otherModelHash,
            adaHighFaceId,
            adaMediumFaceId,
            bobLowFaceId,
            bobLowSuggestionId);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PhotoIdentity-SuggestedGroups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SeededGroups(
        string ModelHash,
        string OtherModelHash,
        string AdaHighFaceId,
        string AdaMediumFaceId,
        string BobLowFaceId,
        long BobLowSuggestionId);

    private sealed class ReviewApiFactory(string databasePath) : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("PhotoIdentity:DatabasePath", databasePath);
        }
    }
}
