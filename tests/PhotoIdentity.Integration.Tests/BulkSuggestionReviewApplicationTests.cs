using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class BulkSuggestionReviewApplicationTests
{
    [Fact]
    public async Task Preview_and_commit_accept_one_person_group_with_linked_audit_actions()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededSuggestions seeded = await SeedSuggestionsAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            long[] adaSuggestions = [seeded.FirstAdaSuggestionId, seeded.SecondAdaSuggestionId];

            using HttpResponseMessage mixedResponse = await client.PostAsJsonAsync(
                "/api/review/bulk-suggestions/preview",
                new BulkSuggestionPreviewRequest(
                    [seeded.FirstAdaSuggestionId, seeded.BobSuggestionId],
                    seeded.ModelId,
                    seeded.ModelHash));
            Assert.Equal(HttpStatusCode.BadRequest, mixedResponse.StatusCode);

            using HttpResponseMessage previewResponse = await client.PostAsJsonAsync(
                "/api/review/bulk-suggestions/preview",
                new BulkSuggestionPreviewRequest(
                    adaSuggestions,
                    seeded.ModelId,
                    seeded.ModelHash));
            previewResponse.EnsureSuccessStatusCode();
            string previewJson = await previewResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(seeded.SourceRoot, previewJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storage_path", previewJson, StringComparison.OrdinalIgnoreCase);
            BulkSuggestionPreviewResponse preview = Assert.IsType<BulkSuggestionPreviewResponse>(
                await previewResponse.Content.ReadFromJsonAsync<BulkSuggestionPreviewResponse>());
            Assert.Equal(2, preview.RequestedCount);
            Assert.Equal(2, preview.AffectedCount);
            Assert.Equal(0, preview.SkippedCount);
            Assert.Equal(seeded.AdaPersonId, preview.Person.Id);
            Assert.Equal(seeded.ModelId, preview.ModelId);
            Assert.Equal(seeded.ModelHash, preview.ModelHash);
            Assert.Equal(64, preview.PreviewToken.Length);

            BulkSuggestionCommitRequest unconfirmed = new(
                adaSuggestions,
                seeded.ModelId,
                seeded.ModelHash,
                preview.AffectedCount,
                preview.PreviewToken,
                Confirm: false,
                Actor: "bulk-suggestion:test",
                Note: "Accept grouped matches.");
            using HttpResponseMessage unconfirmedResponse = await client.PostAsJsonAsync(
                "/api/review/bulk-suggestions/commit",
                unconfirmed);
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmedResponse.StatusCode);

            using HttpResponseMessage commitResponse = await client.PostAsJsonAsync(
                "/api/review/bulk-suggestions/commit",
                unconfirmed with { Confirm = true });
            commitResponse.EnsureSuccessStatusCode();
            BulkSuggestionCommitResponse result = Assert.IsType<BulkSuggestionCommitResponse>(
                await commitResponse.Content.ReadFromJsonAsync<BulkSuggestionCommitResponse>());
            Assert.Equal(2, result.AffectedCount);
            Assert.Equal(seeded.AdaPersonId, result.Person.Id);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(2, await ReadInt64Async(
                connection,
                "SELECT COUNT(*) FROM person_labels WHERE label_kind = 'manual';"));
            Assert.Equal(2, await ReadInt64Async(
                connection,
                "SELECT COUNT(*) FROM review_actions WHERE action_kind = 'assign';"));
            Assert.Equal(2, await ReadInt64Async(
                connection,
                "SELECT COUNT(*) FROM identity_suggestions WHERE status = 'accepted';"));
            Assert.Equal(1, await ReadInt64Async(
                connection,
                "SELECT COUNT(*) FROM identity_suggestions WHERE status = 'pending';"));
            Assert.Equal(2, await ReadInt64Async(
                connection,
                "SELECT COUNT(*) FROM identity_suggestion_review_actions WHERE action_kind = 'accept' AND review_action_id IS NOT NULL;"));
            Assert.Equal(2, await ReadInt64Async(
                connection,
                """
                SELECT COUNT(*)
                FROM identity_suggestion_review_actions AS suggestion_action
                INNER JOIN identity_suggestions AS suggestion
                    ON suggestion.id = suggestion_action.suggestion_id
                INNER JOIN review_actions AS review_action
                    ON review_action.id = suggestion_action.review_action_id
                   AND review_action.face_occurrence_id = suggestion.face_occurrence_id
                   AND review_action.person_id = suggestion.suggested_person_id
                WHERE suggestion_action.action_kind = 'accept';
                """));
            Assert.Equal(4, await ReadInt64Async(
                connection,
                """
                SELECT COUNT(*)
                FROM (
                    SELECT note FROM review_actions WHERE note = 'Accept grouped matches.'
                    UNION ALL
                    SELECT note FROM identity_suggestion_review_actions WHERE note = 'Accept grouped matches.'
                );
                """));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Stale_group_preview_is_rejected_without_partial_acceptance()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededSuggestions seeded = await SeedSuggestionsAsync(database);
            long[] suggestionIds = [seeded.FirstAdaSuggestionId, seeded.SecondAdaSuggestionId];

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage previewResponse = await client.PostAsJsonAsync(
                "/api/review/bulk-suggestions/preview",
                new BulkSuggestionPreviewRequest(suggestionIds, seeded.ModelId, seeded.ModelHash));
            previewResponse.EnsureSuccessStatusCode();
            BulkSuggestionPreviewResponse preview = Assert.IsType<BulkSuggestionPreviewResponse>(
                await previewResponse.Content.ReadFromJsonAsync<BulkSuggestionPreviewResponse>());

            SqliteReviewSuggestionRepository suggestionRepository = new(database);
            await suggestionRepository.AcceptAsync(
                FaceOccurrenceId.From(Guid.Parse(seeded.FirstAdaFaceId)),
                seeded.FirstAdaSuggestionId,
                "other-reviewer",
                new DateTimeOffset(2026, 7, 31, 0, 10, 0, TimeSpan.Zero));

            using HttpResponseMessage commitResponse = await client.PostAsJsonAsync(
                "/api/review/bulk-suggestions/commit",
                new BulkSuggestionCommitRequest(
                    suggestionIds,
                    seeded.ModelId,
                    seeded.ModelHash,
                    preview.AffectedCount,
                    preview.PreviewToken,
                    Confirm: true,
                    Actor: "bulk-suggestion:test"));
            Assert.Equal(HttpStatusCode.Conflict, commitResponse.StatusCode);
            string conflict = await commitResponse.Content.ReadAsStringAsync();
            Assert.Contains("preview", conflict, StringComparison.OrdinalIgnoreCase);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await ReadInt64Async(
                connection,
                "SELECT COUNT(*) FROM review_actions WHERE action_kind = 'assign';"));
            Assert.Equal(1, await ReadInt64Async(
                connection,
                "SELECT COUNT(*) FROM identity_suggestion_review_actions WHERE action_kind = 'accept';"));
            Assert.Equal("pending", await ReadStringAsync(
                connection,
                $"SELECT status FROM identity_suggestions WHERE id = {seeded.SecondAdaSuggestionId};"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededSuggestions> SeedSuggestionsAsync(SqliteCatalogueDatabase database)
    {
        string sourceRoot = Path.Combine(
            Path.GetTempPath(),
            "private-bulk-suggestions",
            Guid.NewGuid().ToString("N"));
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string revisionId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string firstAdaFaceId = Guid.NewGuid().ToString("D");
        string secondAdaFaceId = Guid.NewGuid().ToString("D");
        string bobFaceId = Guid.NewGuid().ToString("D");
        const long firstAdaSuggestionId = 101;
        const long secondAdaSuggestionId = 102;
        const long bobSuggestionId = 103;
        const string modelId = "sface";
        string modelHash = new('a', 64);
        string now = new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero).ToString("O");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $source_root, $now);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES
                    ($ada_person_id, 'Ada', $now, NULL),
                    ($bob_person_id, 'Bob', $now, NULL);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES ($asset_id, $source_id, 'group-photo.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES ($revision_id, $asset_id, $revision_hash, 1234, $now, 'image/jpeg', 800, 600);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($first_ada_face_id, $revision_id, 0, $now),
                    ($second_ada_face_id, $revision_id, 1, $now),
                    ($bob_face_id, $revision_id, 2, $now);
            INSERT INTO identity_suggestions (
                id, face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($first_ada_suggestion_id, $first_ada_face_id, $ada_person_id, $model_id, $model_hash, 0.94, 'pending', $now),
                    ($second_ada_suggestion_id, $second_ada_face_id, $ada_person_id, $model_id, $model_hash, 0.92, 'pending', $now),
                    ($bob_suggestion_id, $bob_face_id, $bob_person_id, $model_id, $model_hash, 0.90, 'pending', $now);
            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
                VALUES
                    ($first_ada_face_id, $model_id, $model_hash, 1, $first_ada_suggestion_id, 0.20, $now),
                    ($second_ada_face_id, $model_id, $model_hash, 1, $second_ada_suggestion_id, 0.18, $now),
                    ($bob_face_id, $model_id, $model_hash, 1, $bob_suggestion_id, 0.15, $now);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$ada_person_id", adaPersonId);
        command.Parameters.AddWithValue("$bob_person_id", bobPersonId);
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$revision_id", revisionId);
        command.Parameters.AddWithValue("$revision_hash", new string('b', 64));
        command.Parameters.AddWithValue("$first_ada_face_id", firstAdaFaceId);
        command.Parameters.AddWithValue("$second_ada_face_id", secondAdaFaceId);
        command.Parameters.AddWithValue("$bob_face_id", bobFaceId);
        command.Parameters.AddWithValue("$first_ada_suggestion_id", firstAdaSuggestionId);
        command.Parameters.AddWithValue("$second_ada_suggestion_id", secondAdaSuggestionId);
        command.Parameters.AddWithValue("$bob_suggestion_id", bobSuggestionId);
        command.Parameters.AddWithValue("$model_id", modelId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();

        return new SeededSuggestions(
            sourceRoot,
            adaPersonId,
            modelId,
            modelHash,
            firstAdaFaceId,
            firstAdaSuggestionId,
            secondAdaSuggestionId,
            bobSuggestionId);
    }

    private static async Task<long> ReadInt64Async(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadStringAsync(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
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

    private sealed record SeededSuggestions(
        string SourceRoot,
        string AdaPersonId,
        string ModelId,
        string ModelHash,
        string FirstAdaFaceId,
        long FirstAdaSuggestionId,
        long SecondAdaSuggestionId,
        long BobSuggestionId);

    private sealed class ReviewApiFactory : PhotoIdentityApiTestFactory
    {
        public ReviewApiFactory(string databasePath)
            : base(databasePath)
        {
        }
    }
}
