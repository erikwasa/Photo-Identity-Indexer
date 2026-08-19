using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SuggestedPersonReviewFilterApplicationTests
{
    [Fact]
    public async Task Suggested_person_filter_uses_only_current_rank_one_and_composes_with_review_scope()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededReviewFilter seeded = await SeedAsync(database);

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string modelScope =
                $"modelId=sface&modelHash={seeded.ModelHash}" +
                $"&processingRunId={seeded.RunId}";

            ReviewFacePageResponse ada = await GetPageAsync(
                client,
                $"state=unreviewed&{modelScope}&sort=suggested-person" +
                $"&suggestedPersonId={seeded.AdaPersonId}");
            Assert.Equal(2, ada.Total);
            Assert.Equal(
                new[] { seeded.AdaHighFaceId, seeded.AdaMediumFaceId },
                ada.Items.Select(face => face.Id).ToArray());
            Assert.All(
                ada.Items,
                face => Assert.Equal(seeded.AdaPersonId, Assert.IsType<ReviewTopSuggestionResponse>(face.TopSuggestion).Person.Id));
            Assert.DoesNotContain(ada.Items, face => face.Id == seeded.BobFaceId);
            Assert.DoesNotContain(ada.Items, face => face.Id == seeded.StaleSuggestionFaceId);

            ReviewFacePageResponse adaHigh = await GetPageAsync(
                client,
                $"state=unreviewed&{modelScope}&sort=confidence-group&confidenceGroup=high" +
                $"&suggestedPersonId={seeded.AdaPersonId}");
            Assert.Equal(new[] { seeded.AdaHighFaceId }, adaHigh.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse adaMedium = await GetPageAsync(
                client,
                $"state=unreviewed&{modelScope}&sort=confidence-group&confidenceGroup=medium" +
                $"&suggestedPersonId={seeded.AdaPersonId}");
            Assert.Equal(new[] { seeded.AdaMediumFaceId }, adaMedium.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse adaLow = await GetPageAsync(
                client,
                $"state=unreviewed&{modelScope}&sort=confidence-group&confidenceGroup=low" +
                $"&suggestedPersonId={seeded.AdaPersonId}");
            Assert.Empty(adaLow.Items);
            Assert.Equal(0, adaLow.Total);

            ReviewFaceDetailsResponse adaMediumDetails = Assert.IsType<ReviewFaceDetailsResponse>(
                await client.GetFromJsonAsync<ReviewFaceDetailsResponse>(
                    $"/api/review/suggestion-faces/{seeded.AdaMediumFaceId}" +
                    $"?state=unreviewed&{modelScope}&sort=suggested-person" +
                    $"&suggestedPersonId={seeded.AdaPersonId}"));
            ReviewFaceNavigationResponse navigation = Assert.IsType<ReviewFaceNavigationResponse>(
                adaMediumDetails.Navigation);
            Assert.Equal(seeded.AdaHighFaceId, navigation.PreviousFaceId);
            Assert.Null(navigation.NextFaceId);
            Assert.Equal(2, navigation.Position);
            Assert.Equal(2, navigation.Total);

            using HttpResponseMessage hideBob = await client.PutAsJsonAsync(
                $"/api/review/people/{seeded.BobPersonId}/smart-collection-visibility",
                new SetPersonSmartCollectionVisibilityRequest(true));
            await hideBob.EnsureSuccessWithDiagnosticBodyAsync();

            ReviewPersonResponse[] people = await client.GetFromJsonAsync<ReviewPersonResponse[]>("/api/review/people") ?? [];
            ReviewPersonResponse hiddenBob = Assert.Single(people, person => person.Id == seeded.BobPersonId);
            Assert.True(hiddenBob.HiddenFromSmartCollections);

            ReviewFacePageResponse bob = await GetPageAsync(
                client,
                $"state=unreviewed&{modelScope}&sort=score-desc" +
                $"&suggestedPersonId={seeded.BobPersonId}");
            Assert.Equal(new[] { seeded.BobFaceId }, bob.Items.Select(face => face.Id).ToArray());

            using HttpResponseMessage assign = await client.PostAsJsonAsync(
                $"/api/review/faces/{seeded.AdaMediumFaceId}/assign",
                new AssignFaceRequest(seeded.AdaPersonId, "suggested-person-filter:test"));
            await assign.EnsureSuccessWithDiagnosticBodyAsync();

            ReviewFacePageResponse assignedAda = await GetPageAsync(
                client,
                $"state=assigned&{modelScope}&sort=suggested-person" +
                $"&suggestedPersonId={seeded.AdaPersonId}");
            Assert.Equal(new[] { seeded.AdaMediumFaceId }, assignedAda.Items.Select(face => face.Id).ToArray());

            ReviewFacePageResponse unfiltered = await GetPageAsync(
                client,
                $"state=all&{modelScope}&sort=created-desc");
            Assert.Equal(4, unfiltered.Total);
            Assert.Contains(unfiltered.Items, face => face.Id == seeded.StaleSuggestionFaceId && face.TopSuggestion is null);

            using HttpResponseMessage invalidPerson = await client.GetAsync(
                $"/api/review/suggestion-faces?state=unreviewed&{modelScope}" +
                "&suggestedPersonId=not-a-person-id");
            await invalidPerson.EnsureStatusCodeWithDiagnosticBodyAsync(HttpStatusCode.BadRequest);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<ReviewFacePageResponse> GetPageAsync(HttpClient client, string query) =>
        Assert.IsType<ReviewFacePageResponse>(
            await client.GetFromJsonAsync<ReviewFacePageResponse>(
                $"/api/review/suggestion-faces?{query}&limit=20"));

    private static async Task<SeededReviewFilter> SeedAsync(SqliteCatalogueDatabase database)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string sourceRoot = Path.Combine(Path.GetTempPath(), "private-suggested-person-filter", Guid.NewGuid().ToString("N"));
        string runId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string modelHash = new('a', 64);
        DateTimeOffset now = new(2026, 8, 19, 20, 0, 0, TimeSpan.Zero);

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $source_root, $now);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES
                    ($ada_person_id, 'Ada', $now, NULL),
                    ($bob_person_id, 'Bob', $now, NULL);
            INSERT INTO processing_runs (id, status, configuration_json, started_at_utc, completed_at_utc)
                VALUES ($run_id, 'completed', '{}', $now, $now);
            """,
            ("$source_id", sourceId),
            ("$source_root", sourceRoot),
            ("$ada_person_id", adaPersonId),
            ("$bob_person_id", bobPersonId),
            ("$run_id", runId),
            ("$now", now.ToString("O")));

        SeededFace adaHigh = await SeedFaceAsync(connection, sourceId, runId, "ada-high", 'b', now.AddMinutes(-1));
        SeededFace adaMedium = await SeedFaceAsync(connection, sourceId, runId, "ada-medium", 'c', now.AddMinutes(-2));
        SeededFace bob = await SeedFaceAsync(connection, sourceId, runId, "bob", 'd', now.AddMinutes(-3));
        SeededFace stale = await SeedFaceAsync(connection, sourceId, runId, "stale", 'e', now.AddMinutes(-4));

        long adaHighSuggestion = await SeedSuggestionAsync(
            connection, adaHigh.FaceId, adaPersonId, modelHash, 0.91, now);
        await SeedRankingAsync(connection, adaHigh.FaceId, modelHash, 1, adaHighSuggestion, 0.20, now);

        long adaMediumSuggestion = await SeedSuggestionAsync(
            connection, adaMedium.FaceId, adaPersonId, modelHash, 0.88, now);
        await SeedRankingAsync(connection, adaMedium.FaceId, modelHash, 1, adaMediumSuggestion, 0.05, now);

        long bobSuggestion = await SeedSuggestionAsync(
            connection, bob.FaceId, bobPersonId, modelHash, 0.87, now);
        await SeedRankingAsync(connection, bob.FaceId, modelHash, 1, bobSuggestion, 0.04, now);

        long bobLowerRankAda = await SeedSuggestionAsync(
            connection, bob.FaceId, adaPersonId, modelHash, 0.70, now);
        await SeedRankingAsync(connection, bob.FaceId, modelHash, 2, bobLowerRankAda, null, now);

        _ = await SeedSuggestionAsync(
            connection, stale.FaceId, adaPersonId, modelHash, 0.93, now);

        return new SeededReviewFilter(
            runId,
            modelHash,
            adaPersonId,
            bobPersonId,
            adaHigh.FaceId,
            adaMedium.FaceId,
            bob.FaceId,
            stale.FaceId);
    }

    private static async Task<SeededFace> SeedFaceAsync(
        SqliteConnection connection,
        string sourceId,
        string runId,
        string name,
        char hashCharacter,
        DateTimeOffset createdAt)
    {
        string assetId = Guid.NewGuid().ToString("D");
        string revisionId = Guid.NewGuid().ToString("D");
        string faceId = Guid.NewGuid().ToString("D");
        string jobId = Guid.NewGuid().ToString("D");
        string timestamp = createdAt.ToString("O");
        await ExecuteAsync(
            connection,
            """
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES ($asset_id, $source_id, $source_key, $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES ($revision_id, $asset_id, $hash, 100, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, 0, $now);
            INSERT INTO processing_jobs (
                id, processing_run_id, asset_revision_id, status, attempt_count,
                available_at_utc, completed_at_utc, idempotency_key)
                VALUES ($job_id, $run_id, $revision_id, 'completed', 1, $now, $now, $idempotency_key);
            """,
            ("$asset_id", assetId),
            ("$source_id", sourceId),
            ("$source_key", $"{name}.jpg"),
            ("$revision_id", revisionId),
            ("$hash", new string(hashCharacter, 64)),
            ("$face_id", faceId),
            ("$job_id", jobId),
            ("$run_id", runId),
            ("$idempotency_key", $"{runId}:{revisionId}"),
            ("$now", timestamp));
        return new SeededFace(faceId);
    }

    private static async Task<long> SeedSuggestionAsync(
        SqliteConnection connection,
        string faceId,
        string personId,
        string modelHash,
        double score,
        DateTimeOffset createdAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES ($face_id, $person_id, 'sface', $model_hash, $score, 'pending', $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$face_id", faceId);
        command.Parameters.AddWithValue("$person_id", personId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$score", score);
        command.Parameters.AddWithValue("$now", createdAt.ToString("O"));
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }

    private static Task SeedRankingAsync(
        SqliteConnection connection,
        string faceId,
        string modelHash,
        int rank,
        long suggestionId,
        double? scoreMargin,
        DateTimeOffset generatedAt) =>
        ExecuteAsync(
            connection,
            """
            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
                VALUES ($face_id, 'sface', $model_hash, $rank, $suggestion_id, $score_margin, $now);
            """,
            ("$face_id", faceId),
            ("$model_hash", modelHash),
            ("$rank", rank),
            ("$suggestion_id", suggestionId),
            ("$score_margin", scoreMargin),
            ("$now", generatedAt.ToString("O")));

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
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
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SeededFace(string FaceId);

    private sealed record SeededReviewFilter(
        string RunId,
        string ModelHash,
        string AdaPersonId,
        string BobPersonId,
        string AdaHighFaceId,
        string AdaMediumFaceId,
        string BobFaceId,
        string StaleSuggestionFaceId);
}
