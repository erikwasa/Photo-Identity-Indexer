using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewQueueNavigationApplicationTests
{
    [Fact]
    public async Task Details_navigation_preserves_scope_and_captured_next_face_after_acceptance()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededQueue seeded = await SeedQueueAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string scope =
                $"state=unreviewed&processingRunId={seeded.RunId}" +
                $"&modelId=sface&modelHash={seeded.ModelHash}&sort=created-desc";

            ReviewFacePageResponse page = Assert.IsType<ReviewFacePageResponse>(
                await client.GetFromJsonAsync<ReviewFacePageResponse>(
                    $"/api/review/faces?{scope}&limit=10"));
            Assert.Equal(
                new[] { seeded.NewestFaceId, seeded.MiddleFaceId, seeded.OldestFaceId },
                page.Items.Select(face => face.Id).ToArray());

            ReviewFaceDetailsResponse middle = Assert.IsType<ReviewFaceDetailsResponse>(
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                    $"/api/review/faces/{seeded.MiddleFaceId}?{scope}"));
            ReviewFaceNavigationResponse navigation = Assert.IsType<ReviewFaceNavigationResponse>(middle.Navigation);
            Assert.Equal(seeded.NewestFaceId, navigation.PreviousFaceId);
            Assert.Equal(seeded.OldestFaceId, navigation.NextFaceId);
            Assert.Equal(2, navigation.Position);
            Assert.Equal(3, navigation.Total);
            Assert.Equal("created-desc", navigation.Sort);

            ReviewIdentitySuggestionResponse suggestion = Assert.Single(
                Assert.IsType<ReviewIdentitySuggestionResponse[]>(
                    await client.GetFromJsonAsync<ReviewIdentitySuggestionResponse[]>(
                        $"/api/review/faces/{seeded.MiddleFaceId}/suggestions")));
            using HttpResponseMessage accepted = await client.PostAsJsonAsync(
                $"/api/review/faces/{seeded.MiddleFaceId}/suggestions/{suggestion.Id}/accept",
                new ReviewSuggestionActionRequest("queue-navigation:test"));
            await accepted.EnsureSuccessWithDiagnosticBodyAsync("accept queue-navigation suggestion");

            ReviewFaceDetailsResponse oldest = Assert.IsType<ReviewFaceDetailsResponse>(
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                    $"/api/review/faces/{seeded.OldestFaceId}?{scope}"));
            ReviewFaceNavigationResponse afterMutation = Assert.IsType<ReviewFaceNavigationResponse>(oldest.Navigation);
            Assert.Equal(seeded.NewestFaceId, afterMutation.PreviousFaceId);
            Assert.Null(afterMutation.NextFaceId);
            Assert.Equal(2, afterMutation.Position);
            Assert.Equal(2, afterMutation.Total);

            string detailsJson = await client.GetRequiredStringAsync(
                $"/api/review/faces/{seeded.OldestFaceId}?{scope}");
            Assert.DoesNotContain(seeded.SourceRoot, detailsJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Unsupported_queue_sort_is_rejected()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededQueue seeded = await SeedQueueAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                $"/api/review/faces/{seeded.NewestFaceId}?state=unreviewed&sort=alphabetical");

            await response.EnsureStatusCodeWithDiagnosticBodyAsync(
                HttpStatusCode.BadRequest,
                "unsupported review queue sort");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededQueue> SeedQueueAsync(SqliteCatalogueDatabase database)
    {
        string sourceRoot = Path.Combine(Path.GetTempPath(), "private-queue", Guid.NewGuid().ToString("N"));
        string sourceId = Guid.NewGuid().ToString("D");
        string personId = Guid.NewGuid().ToString("D");
        string runId = Guid.NewGuid().ToString("D");
        string modelHash = new('a', 64);
        string newestFaceId = Guid.NewGuid().ToString("D");
        string middleFaceId = Guid.NewGuid().ToString("D");
        string oldestFaceId = Guid.NewGuid().ToString("D");
        string now = new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.Zero).ToString("O");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $source_root, $now);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES ($person_id, 'Queue Person', $now, NULL);
            INSERT INTO processing_runs (id, status, configuration_json, started_at_utc, completed_at_utc)
                VALUES ($run_id, 'completed', '{}', $now, $now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES
                    ($newest_asset_id, $source_id, 'newest.jpg', $now, $now),
                    ($middle_asset_id, $source_id, 'middle.jpg', $now, $now),
                    ($oldest_asset_id, $source_id, 'oldest.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES
                    ($newest_revision_id, $newest_asset_id, $newest_hash, 100, $now, 'image/jpeg', 640, 480),
                    ($middle_revision_id, $middle_asset_id, $middle_hash, 100, $now, 'image/jpeg', 640, 480),
                    ($oldest_revision_id, $oldest_asset_id, $oldest_hash, 100, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($newest_face_id, $newest_revision_id, 0, $newest_created),
                    ($middle_face_id, $middle_revision_id, 0, $middle_created),
                    ($oldest_face_id, $oldest_revision_id, 0, $oldest_created);
            INSERT INTO processing_jobs (
                id, processing_run_id, asset_revision_id, status, attempt_count,
                available_at_utc, completed_at_utc, idempotency_key)
                VALUES
                    ($newest_job_id, $run_id, $newest_revision_id, 'completed', 1, $now, $now, $newest_key),
                    ($middle_job_id, $run_id, $middle_revision_id, 'completed', 1, $now, $now, $middle_key),
                    ($oldest_job_id, $run_id, $oldest_revision_id, 'completed', 1, $now, $now, $oldest_key);
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($newest_face_id, $person_id, 'sface', $model_hash, 0.95, 'pending', $now),
                    ($middle_face_id, $person_id, 'sface', $model_hash, 0.94, 'pending', $now),
                    ($oldest_face_id, $person_id, 'sface', $model_hash, 0.93, 'pending', $now);
            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
                SELECT face_occurrence_id, model_id, model_hash, 1, id, 0.12, created_at_utc
                FROM identity_suggestions;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$person_id", personId);
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$now", now);

        string[] names = ["newest", "middle", "oldest"];
        string[] faceIds = [newestFaceId, middleFaceId, oldestFaceId];
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
        command.Parameters.AddWithValue("$newest_created", new DateTimeOffset(2026, 7, 30, 20, 3, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$middle_created", new DateTimeOffset(2026, 7, 30, 20, 2, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$oldest_created", new DateTimeOffset(2026, 7, 30, 20, 1, 0, TimeSpan.Zero).ToString("O"));
        await command.ExecuteNonQueryAsync();

        return new SeededQueue(
            sourceRoot,
            runId,
            modelHash,
            newestFaceId,
            middleFaceId,
            oldestFaceId);
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

    private sealed record SeededQueue(
        string SourceRoot,
        string RunId,
        string ModelHash,
        string NewestFaceId,
        string MiddleFaceId,
        string OldestFaceId);

    private sealed class ReviewApiFactory : PhotoIdentityApiTestFactory
    {
        public ReviewApiFactory(string databasePath)
            : base(databasePath)
        {
        }
    }
}
