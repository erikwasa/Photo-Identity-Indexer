using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SuggestionUndoRegressionTests
{
    [Fact]
    public async Task Undoing_a_suggestion_assignment_restores_the_ranked_suggestion_to_the_queue()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            string faceId = Guid.NewGuid().ToString();
            string personId = Guid.NewGuid().ToString();
            string modelId = "sface-baseline";
            string modelHash = new('c', 64);
            long suggestionId = await SeedSuggestionAsync(
                database,
                faceId,
                personId,
                modelId,
                modelHash,
                Path.Combine(directory, "private-photos"));

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage accept = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/suggestions/{suggestionId}/accept",
                new ReviewSuggestionActionRequest("human:test"));
            accept.EnsureSuccessStatusCode();

            using HttpResponseMessage undo = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/undo",
                new ReviewFaceActionRequest("human:test", "Correcting the previous decision."));
            undo.EnsureSuccessStatusCode();

            ReviewFacePageResponse queue =
                await client.GetFromJsonAsync<ReviewFacePageResponse>(
                    $"/api/review/suggestion-faces?state=unreviewed&offset=0&limit=40&sort=margin-desc" +
                    $"&modelId={Uri.EscapeDataString(modelId)}&modelHash={modelHash}")
                ?? throw new InvalidOperationException("The suggestion queue response was empty.");

            ReviewFaceResponse face = Assert.Single(queue.Items);
            Assert.Equal(faceId, face.Id);
            Assert.Equal("unreviewed", face.State);
            ReviewTopSuggestionResponse topSuggestion = Assert.IsType<ReviewTopSuggestionResponse>(face.TopSuggestion);
            Assert.Equal(suggestionId, topSuggestion.Id);
            Assert.Equal("pending", topSuggestion.Status);
            Assert.Equal(0.91, topSuggestion.Score, 6);
            Assert.Equal(0.12, Assert.IsType<double>(topSuggestion.ScoreMargin), 6);

            ReviewIdentitySuggestionResponse[] suggestions =
                await client.GetFromJsonAsync<ReviewIdentitySuggestionResponse[]>(
                    $"/api/review/faces/{faceId}/suggestions") ?? [];
            Assert.Equal("pending", Assert.Single(suggestions).Status);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM review_actions WHERE face_occurrence_id = $face_id AND action_kind = 'assign' AND reversed_at_utc IS NOT NULL;",
                    ("$face_id", faceId)));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM identity_suggestions WHERE id = $suggestion_id AND status = 'pending';",
                    ("$suggestion_id", suggestionId)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<long> SeedSuggestionAsync(
        SqliteCatalogueDatabase database,
        string faceId,
        string personId,
        string modelId,
        string modelHash,
        string sourceRoot)
    {
        string now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero).ToString("O");
        string sourceId = Guid.NewGuid().ToString();
        string assetId = Guid.NewGuid().ToString();
        string revisionId = Guid.NewGuid().ToString();

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $source_root, $created_at_utc);

                INSERT INTO assets (id, source_id, source_key, created_at_utc)
                VALUES ($asset_id, $source_id, 'family/private-photo.jpg', $created_at_utc);

                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes, observed_at_utc,
                    media_type, width, height)
                VALUES (
                    $revision_id, $asset_id, $revision_hash, 3, $created_at_utc,
                    'image/jpeg', 1200, 800);

                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, 0, $created_at_utc);

                INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES ($person_id, 'Verification Person', $created_at_utc, NULL);

                INSERT INTO identity_suggestions (
                    face_occurrence_id, suggested_person_id, model_id, model_hash,
                    score, status, created_at_utc)
                VALUES (
                    $face_id, $person_id, $model_id, $model_hash,
                    0.91, 'pending', $created_at_utc);

                INSERT INTO identity_suggestion_rankings (
                    face_occurrence_id, model_id, model_hash, rank,
                    suggestion_id, score_margin, generated_at_utc)
                SELECT
                    $face_id, $model_id, $model_hash, 1,
                    id, 0.12, $created_at_utc
                FROM identity_suggestions
                WHERE face_occurrence_id = $face_id
                  AND suggested_person_id = $person_id
                  AND model_id = $model_id
                  AND model_hash = $model_hash;
                """;
            command.Parameters.AddWithValue("$source_id", sourceId);
            command.Parameters.AddWithValue("$source_root", sourceRoot);
            command.Parameters.AddWithValue("$asset_id", assetId);
            command.Parameters.AddWithValue("$revision_id", revisionId);
            command.Parameters.AddWithValue("$revision_hash", new string('a', 64));
            command.Parameters.AddWithValue("$face_id", faceId);
            command.Parameters.AddWithValue("$person_id", personId);
            command.Parameters.AddWithValue("$model_id", modelId);
            command.Parameters.AddWithValue("$model_hash", modelHash);
            command.Parameters.AddWithValue("$created_at_utc", now);
            await command.ExecuteNonQueryAsync();
        }

        using SqliteCommand read = connection.CreateCommand();
        read.CommandText = """
            SELECT id
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_id
              AND suggested_person_id = $person_id
              AND model_id = $model_id
              AND model_hash = $model_hash;
            """;
        read.Parameters.AddWithValue("$face_id", faceId);
        read.Parameters.AddWithValue("$person_id", personId);
        read.Parameters.AddWithValue("$model_id", modelId);
        read.Parameters.AddWithValue("$model_hash", modelHash);
        object? value = await read.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long> ReadInt64Async(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
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
