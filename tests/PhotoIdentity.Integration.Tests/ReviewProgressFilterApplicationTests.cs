using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewProgressFilterApplicationTests
{
    [Fact]
    public async Task Filter_options_and_face_query_preserve_exact_run_model_and_state_intersection()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededProgress seeded = await SeedProgressAsync(database);

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            ReviewFilterOptionsResponse options = Assert.IsType<ReviewFilterOptionsResponse>(
                await client.GetFromJsonAsync<ReviewFilterOptionsResponse>("/api/review/filters"));
            Assert.Equal(2, options.ProcessingRuns.Count);
            Assert.Equal(2, options.ModelRevisions.Count);
            Assert.Contains(options.ProcessingRuns, run => run.Id == seeded.FirstRunId && run.FaceCount == 1);
            Assert.Contains(options.ProcessingRuns, run => run.Id == seeded.SecondRunId && run.FaceCount == 1);
            Assert.Contains(
                options.ModelRevisions,
                model => model.ModelId == "sface" &&
                         model.ModelHash == seeded.FirstModelHash &&
                         model.FaceCount == 1);
            Assert.Contains(
                options.ModelRevisions,
                model => model.ModelId == "sface" &&
                         model.ModelHash == seeded.SecondModelHash &&
                         model.FaceCount == 1);

            ReviewFacePageResponse firstPage = Assert.IsType<ReviewFacePageResponse>(
                await client.GetFromJsonAsync<ReviewFacePageResponse>(
                    $"/api/review/faces?state=unreviewed&processingRunId={seeded.FirstRunId}" +
                    $"&modelId=sface&modelHash={seeded.FirstModelHash}"));
            ReviewFaceResponse first = Assert.Single(firstPage.Items);
            Assert.Equal(seeded.FirstFaceId, first.Id);
            Assert.Equal("unreviewed", first.State);

            ReviewFacePageResponse secondPage = Assert.IsType<ReviewFacePageResponse>(
                await client.GetFromJsonAsync<ReviewFacePageResponse>(
                    $"/api/review/faces?state=assigned&processingRunId={seeded.SecondRunId}" +
                    $"&modelId=sface&modelHash={seeded.SecondModelHash}"));
            ReviewFaceResponse second = Assert.Single(secondPage.Items);
            Assert.Equal(seeded.SecondFaceId, second.Id);
            Assert.Equal("assigned", second.State);
            Assert.Equal("Ada", second.Person?.DisplayName);

            string filtersJson = await client.GetRequiredStringAsync("/api/review/filters");
            string facesJson = await client.GetRequiredStringAsync(
                $"/api/review/faces?state=all&processingRunId={seeded.FirstRunId}");
            Assert.DoesNotContain(seeded.SourceRoot, filtersJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(seeded.SourceRoot, facesJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Model_filter_requires_both_model_id_and_exact_hash()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            await SeedProgressAsync(database);

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                "/api/review/faces?state=all&modelId=sface");

            await response.EnsureStatusCodeWithDiagnosticBodyAsync(
                HttpStatusCode.BadRequest,
                "review face model-scope validation");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededProgress> SeedProgressAsync(SqliteCatalogueDatabase database)
    {
        string sourceRoot = Path.Combine(Path.GetTempPath(), "private-progress", Guid.NewGuid().ToString("N"));
        string sourceId = Guid.NewGuid().ToString("D");
        string personId = Guid.NewGuid().ToString("D");
        string firstAssetId = Guid.NewGuid().ToString("D");
        string secondAssetId = Guid.NewGuid().ToString("D");
        string firstRevisionId = Guid.NewGuid().ToString("D");
        string secondRevisionId = Guid.NewGuid().ToString("D");
        string firstFaceId = Guid.NewGuid().ToString("D");
        string secondFaceId = Guid.NewGuid().ToString("D");
        string firstRunId = Guid.NewGuid().ToString("D");
        string secondRunId = Guid.NewGuid().ToString("D");
        string firstJobId = Guid.NewGuid().ToString("D");
        string secondJobId = Guid.NewGuid().ToString("D");
        string firstModelHash = new('a', 64);
        string secondModelHash = new('b', 64);
        string now = new DateTimeOffset(2026, 7, 27, 18, 30, 0, TimeSpan.Zero).ToString("O");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $source_root, $now);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES ($person_id, 'Ada', $now, NULL);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES
                    ($first_asset_id, $source_id, 'first.jpg', $now, $now),
                    ($second_asset_id, $source_id, 'second.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES
                    ($first_revision_id, $first_asset_id, $first_revision_hash, 100, $now, 'image/jpeg', 640, 480),
                    ($second_revision_id, $second_asset_id, $second_revision_hash, 100, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($first_face_id, $first_revision_id, 0, $now),
                    ($second_face_id, $second_revision_id, 0, $now);
            INSERT INTO processing_runs (id, status, configuration_json, started_at_utc, completed_at_utc)
                VALUES
                    ($first_run_id, 'completed', '{}', $first_started, $first_completed),
                    ($second_run_id, 'completed', '{}', $second_started, $second_completed);
            INSERT INTO processing_jobs (
                id, processing_run_id, asset_revision_id, status, attempt_count,
                available_at_utc, completed_at_utc, idempotency_key)
                VALUES
                    ($first_job_id, $first_run_id, $first_revision_id, 'completed', 1, $now, $now, $first_idempotency),
                    ($second_job_id, $second_run_id, $second_revision_id, 'completed', 1, $now, $now, $second_idempotency);
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($first_face_id, $person_id, 'sface', $first_model_hash, 0.92, 'pending', $now),
                    ($second_face_id, $person_id, 'sface', $second_model_hash, 0.91, 'pending', $now);
            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
                SELECT face_occurrence_id, model_id, model_hash, 1, id, 0.10, created_at_utc
                FROM identity_suggestions;
            INSERT INTO person_labels (
                person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES ($person_id, $second_face_id, 'manual', 'filter:test', $now);
            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, created_at_utc, reversed_at_utc, reverses_action_id)
                SELECT
                    $second_face_id,
                    'assign',
                    $person_id,
                    id,
                    'filter:test',
                    $now,
                    NULL,
                    NULL
                FROM person_labels
                WHERE person_id = $person_id
                  AND face_occurrence_id = $second_face_id
                  AND label_kind = 'manual';
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$person_id", personId);
        command.Parameters.AddWithValue("$first_asset_id", firstAssetId);
        command.Parameters.AddWithValue("$second_asset_id", secondAssetId);
        command.Parameters.AddWithValue("$first_revision_id", firstRevisionId);
        command.Parameters.AddWithValue("$second_revision_id", secondRevisionId);
        command.Parameters.AddWithValue("$first_revision_hash", new string('c', 64));
        command.Parameters.AddWithValue("$second_revision_hash", new string('d', 64));
        command.Parameters.AddWithValue("$first_face_id", firstFaceId);
        command.Parameters.AddWithValue("$second_face_id", secondFaceId);
        command.Parameters.AddWithValue("$first_run_id", firstRunId);
        command.Parameters.AddWithValue("$second_run_id", secondRunId);
        command.Parameters.AddWithValue("$first_job_id", firstJobId);
        command.Parameters.AddWithValue("$second_job_id", secondJobId);
        command.Parameters.AddWithValue("$first_started", new DateTimeOffset(2026, 7, 27, 16, 0, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$first_completed", new DateTimeOffset(2026, 7, 27, 16, 5, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$second_started", new DateTimeOffset(2026, 7, 27, 17, 0, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$second_completed", new DateTimeOffset(2026, 7, 27, 17, 5, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$first_idempotency", $"{firstRunId}:{firstRevisionId}");
        command.Parameters.AddWithValue("$second_idempotency", $"{secondRunId}:{secondRevisionId}");
        command.Parameters.AddWithValue("$first_model_hash", firstModelHash);
        command.Parameters.AddWithValue("$second_model_hash", secondModelHash);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();

        return new SeededProgress(
            sourceRoot,
            firstRunId,
            secondRunId,
            firstModelHash,
            secondModelHash,
            firstFaceId,
            secondFaceId);
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

    private sealed record SeededProgress(
        string SourceRoot,
        string FirstRunId,
        string SecondRunId,
        string FirstModelHash,
        string SecondModelHash,
        string FirstFaceId,
        string SecondFaceId);
}
