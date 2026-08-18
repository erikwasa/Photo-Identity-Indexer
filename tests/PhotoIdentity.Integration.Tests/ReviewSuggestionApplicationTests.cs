using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewSuggestionApplicationTests
{
    [Fact]
    public async Task Review_api_exposes_ranked_suggestions_with_exact_model_revision()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            string faceId = Guid.NewGuid().ToString();
            string personId = Guid.NewGuid().ToString();
            string modelHash = new('b', 64);
            string sourceRoot = Path.Combine(directory, "private-photos");
            _ = await SeedSuggestionAsync(database, faceId, personId, modelHash, sourceRoot);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                $"/api/review/faces/{faceId}/suggestions");

            await response.EnsureSuccessWithDiagnosticBodyAsync("load ranked review suggestions");
            Assert.Contains(
                "no-store",
                response.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            string json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(sourceRoot, json, StringComparison.OrdinalIgnoreCase);

            ReviewIdentitySuggestionResponse[] suggestions =
                JsonSerializer.Deserialize<ReviewIdentitySuggestionResponse[]>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            ReviewIdentitySuggestionResponse suggestion = Assert.Single(suggestions);

            Assert.Equal(personId, suggestion.Person.Id);
            Assert.Equal("Ada Lovelace", suggestion.Person.DisplayName);
            Assert.Equal("sface-baseline", suggestion.ModelId);
            Assert.Equal(modelHash, suggestion.ModelHash);
            Assert.Equal(1, suggestion.Rank);
            Assert.Equal(0.91, suggestion.Score, 6);
            Assert.Equal(0.12, Assert.IsType<double>(suggestion.ScoreMargin), 6);
            Assert.Equal("pending", suggestion.Status);
            Assert.Null(suggestion.LatestAction);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Accepting_a_suggestion_creates_audited_manual_assignment()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            string faceId = Guid.NewGuid().ToString();
            string personId = Guid.NewGuid().ToString();
            long suggestionId = await SeedSuggestionAsync(
                database,
                faceId,
                personId,
                new string('c', 64),
                Path.Combine(directory, "private-photos"));

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/suggestions/{suggestionId}/accept",
                new ReviewSuggestionActionRequest("human:test", "Identity confirmed."));

            await response.EnsureSuccessWithDiagnosticBodyAsync("accept review suggestion");
            ReviewIdentitySuggestionResponse accepted =
                await response.Content.ReadFromJsonAsync<ReviewIdentitySuggestionResponse>()
                ?? throw new InvalidOperationException("The accepted suggestion response was empty.");
            Assert.Equal("accepted", accepted.Status);
            Assert.Equal("accept", accepted.LatestAction?.Kind);
            Assert.Equal("human:test", accepted.LatestAction?.Actor);
            Assert.NotNull(accepted.LatestAction?.ReviewActionId);

            ReviewFaceDetailsResponse details =
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>($"/api/review/faces/{faceId}")
                ?? throw new InvalidOperationException("The face details response was empty.");
            Assert.Equal("assigned", details.Face.State);
            Assert.Equal(personId, details.Face.Person?.Id);
            ReviewActionResponse action = Assert.Single(details.Actions);
            Assert.Equal("assign", action.Kind);
            Assert.Equal("human:test", action.Actor);
            Assert.Equal("Identity confirmed.", action.Note);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM person_labels WHERE face_occurrence_id = $face_id AND person_id = $person_id AND label_kind = 'manual';",
                    ("$face_id", faceId),
                    ("$person_id", personId)));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM review_actions WHERE face_occurrence_id = $face_id AND person_id = $person_id AND action_kind = 'assign';",
                    ("$face_id", faceId),
                    ("$person_id", personId)));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM identity_suggestion_review_actions WHERE suggestion_id = $suggestion_id AND action_kind = 'accept' AND review_action_id IS NOT NULL;",
                    ("$suggestion_id", suggestionId)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Rejecting_a_suggestion_records_a_durable_pair_exclusion_without_rejecting_the_face()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            string faceId = Guid.NewGuid().ToString();
            string personId = Guid.NewGuid().ToString();
            long suggestionId = await SeedSuggestionAsync(
                database,
                faceId,
                personId,
                new string('d', 64),
                Path.Combine(directory, "private-photos"));

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/suggestions/{suggestionId}/reject",
                new ReviewSuggestionActionRequest("human:test", "Not the same person."));

            await response.EnsureSuccessWithDiagnosticBodyAsync("reject review suggestion");
            ReviewIdentitySuggestionResponse rejected =
                await response.Content.ReadFromJsonAsync<ReviewIdentitySuggestionResponse>()
                ?? throw new InvalidOperationException("The rejected suggestion response was empty.");
            Assert.Equal("rejected", rejected.Status);
            Assert.Equal("reject", rejected.LatestAction?.Kind);
            Assert.Equal("human:test", rejected.LatestAction?.Actor);
            Assert.Null(rejected.LatestAction?.ReviewActionId);

            using HttpResponseMessage duplicate = await client.PostAsJsonAsync(
                $"/api/review/faces/{faceId}/suggestions/{suggestionId}/accept",
                new ReviewSuggestionActionRequest("human:test"));
            await duplicate.EnsureStatusCodeWithDiagnosticBodyAsync(
                HttpStatusCode.Conflict,
                "accept already-rejected review suggestion");

            ReviewFaceDetailsResponse details =
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>($"/api/review/faces/{faceId}")
                ?? throw new InvalidOperationException("The face details response was empty.");
            Assert.Equal("unreviewed", details.Face.State);
            Assert.Empty(details.Actions);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM identity_suggestions WHERE id = $suggestion_id AND status = 'rejected';",
                    ("$suggestion_id", suggestionId)));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM identity_suggestion_review_actions WHERE suggestion_id = $suggestion_id AND action_kind = 'reject' AND review_action_id IS NULL;",
                    ("$suggestion_id", suggestionId)));
            Assert.Equal(0, await ReadInt64Async(connection, "SELECT COUNT(*) FROM review_actions;"));
            Assert.Equal(0, await ReadInt64Async(connection, "SELECT COUNT(*) FROM person_labels;"));
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
        string modelHash,
        string sourceRoot)
    {
        string now = new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero).ToString("O");
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
                VALUES ($asset_id, $source_id, 'family/secret-photo.jpg', $created_at_utc);

                INSERT INTO asset_revisions (
                    id,
                    asset_id,
                    content_sha256,
                    size_bytes,
                    observed_at_utc,
                    media_type,
                    width,
                    height)
                VALUES (
                    $revision_id,
                    $asset_id,
                    $revision_hash,
                    3,
                    $created_at_utc,
                    'image/jpeg',
                    1200,
                    800);

                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, 0, $created_at_utc);

                INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES ($person_id, 'Ada Lovelace', $created_at_utc, NULL);

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
                    'sface-baseline',
                    $model_hash,
                    0.91,
                    'pending',
                    $created_at_utc);

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
                    'sface-baseline',
                    $model_hash,
                    1,
                    id,
                    0.12,
                    $created_at_utc
                FROM identity_suggestions
                WHERE face_occurrence_id = $face_id
                  AND suggested_person_id = $person_id
                  AND model_id = 'sface-baseline'
                  AND model_hash = $model_hash;
                """;
            command.Parameters.AddWithValue("$source_id", sourceId);
            command.Parameters.AddWithValue("$source_root", sourceRoot);
            command.Parameters.AddWithValue("$asset_id", assetId);
            command.Parameters.AddWithValue("$revision_id", revisionId);
            command.Parameters.AddWithValue("$revision_hash", new string('a', 64));
            command.Parameters.AddWithValue("$face_id", faceId);
            command.Parameters.AddWithValue("$person_id", personId);
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
              AND model_id = 'sface-baseline'
              AND model_hash = $model_hash;
            """;
        read.Parameters.AddWithValue("$face_id", faceId);
        read.Parameters.AddWithValue("$person_id", personId);
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

    private sealed class ReviewApiFactory : PhotoIdentityApiTestFactory
    {
        public ReviewApiFactory(string databasePath)
            : base(databasePath)
        {
        }
    }
}
