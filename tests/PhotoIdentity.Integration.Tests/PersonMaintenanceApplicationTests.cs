using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PersonMaintenanceApplicationTests
{
    [Fact]
    public async Task Person_rename_and_irreversible_merge_are_audited_and_consolidate_identity_state()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 7, 27, 16, 0, 0, TimeSpan.Zero);

            FaceOccurrenceId firstFace = await SeedFaceAsync(database, directory, 1, now);
            FaceOccurrenceId duplicateFace = await SeedFaceAsync(database, directory, 2, now);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson source = await reviewRepository.CreatePersonAsync("Ada Old", now);
            CatalogueReviewPerson target = await reviewRepository.CreatePersonAsync("Ada", now);

            await reviewRepository.AssignAsync(firstFace, source.Id, "human:test", now.AddMinutes(1));
            await reviewRepository.AssignAsync(duplicateFace, source.Id, "human:test", now.AddMinutes(2));
            await reviewRepository.AssignAsync(duplicateFace, target.Id, "human:test", now.AddMinutes(3));
            await SeedDuplicateSuggestionsAsync(database, firstFace, source.Id, target.Id, now.AddMinutes(4));

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage renameResponse = await client.PostAsJsonAsync(
                $"/api/review/people/{source.Id}/rename",
                new RenamePersonRequest("Ada Duplicate", "local-reviewer", "Correct duplicate name"));
            renameResponse.EnsureSuccessStatusCode();
            PersonMaintenanceActionResponse renamed =
                (await renameResponse.Content.ReadFromJsonAsync<PersonMaintenanceActionResponse>())!;
            Assert.Equal("rename", renamed.Kind);
            Assert.Equal("Ada Old", renamed.PreviousDisplayName);
            Assert.Equal("Ada Duplicate", renamed.NewDisplayName);
            Assert.True(renamed.Reversible);

            using HttpResponseMessage unconfirmed = await client.PostAsJsonAsync(
                $"/api/review/people/{source.Id}/merge",
                new MergePersonRequest(target.Id.ToString(), false, "local-reviewer"));
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);

            using HttpResponseMessage mergeResponse = await client.PostAsJsonAsync(
                $"/api/review/people/{source.Id}/merge",
                new MergePersonRequest(
                    target.Id.ToString(),
                    true,
                    "local-reviewer",
                    "Same person confirmed locally"));
            mergeResponse.EnsureSuccessStatusCode();
            PersonMaintenanceActionResponse merged =
                (await mergeResponse.Content.ReadFromJsonAsync<PersonMaintenanceActionResponse>())!;
            Assert.Equal("merge", merged.Kind);
            Assert.Equal("Ada Duplicate", merged.PreviousDisplayName);
            Assert.Equal("Ada", merged.NewDisplayName);
            Assert.Equal(target.Id.ToString(), merged.TargetPersonId);
            Assert.False(merged.Reversible);

            using HttpResponseMessage peopleResponse = await client.GetAsync(
                "/api/review/people/maintenance");
            peopleResponse.EnsureSuccessStatusCode();
            string peopleJson = await peopleResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(directory, peopleJson, StringComparison.OrdinalIgnoreCase);
            PersonMaintenancePersonResponse active = Assert.Single(
                JsonSerializer.Deserialize<PersonMaintenancePersonResponse[]>(
                    peopleJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []);
            Assert.Equal(target.Id.ToString(), active.Id);
            Assert.Equal("Ada", active.DisplayName);
            Assert.Equal(2, active.LabelCount);
            Assert.Equal(1, active.SuggestionCount);

            PersonMaintenanceActionResponse[] history =
                await client.GetFromJsonAsync<PersonMaintenanceActionResponse[]>(
                    "/api/review/people/maintenance/history") ?? [];
            Assert.Equal(2, history.Length);
            Assert.Equal("merge", history[0].Kind);
            Assert.False(history[0].Reversible);
            Assert.Equal("rename", history[1].Kind);
            Assert.True(history[1].Reversible);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(
                target.Id.ToString(),
                await ReadStringAsync(
                    connection,
                    "SELECT merged_into_person_id FROM people WHERE id = $id;",
                    ("$id", source.Id.ToString())));
            Assert.Equal(
                0,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM person_labels WHERE person_id = $id;",
                    ("$id", source.Id.ToString())));
            Assert.Equal(
                2,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM person_labels WHERE person_id = $id;",
                    ("$id", target.Id.ToString())));
            Assert.Equal(
                0,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM review_actions WHERE person_id = $id;",
                    ("$id", source.Id.ToString())));
            Assert.Equal(
                3,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM review_actions WHERE person_id = $id;",
                    ("$id", target.Id.ToString())));
            Assert.Equal(
                0,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM identity_suggestions WHERE suggested_person_id = $id;",
                    ("$id", source.Id.ToString())));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM identity_suggestions WHERE suggested_person_id = $id AND status = 'rejected';",
                    ("$id", target.Id.ToString())));
            Assert.Equal(1, await ReadInt64Async(connection, "SELECT COUNT(*) FROM identity_suggestion_rankings;"));
            Assert.Equal(2, await ReadInt64Async(connection, "SELECT COUNT(*) FROM person_maintenance_actions;"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<FaceOccurrenceId> SeedFaceAsync(
        SqliteCatalogueDatabase database,
        string sourceRoot,
        int index,
        DateTimeOffset now)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string revisionId = Guid.NewGuid().ToString("D");
        FaceOccurrenceId faceId = FaceOccurrenceId.New();

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES ($source_id, 'local-folder', $source_root, $now);
            INSERT INTO assets (
                id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc)
            VALUES ($asset_id, $source_id, $source_key, $now, $now, NULL);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
            VALUES ($revision_id, $asset_id, $hash, 1234, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($face_id, $revision_id, 0, $now);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", Path.Combine(sourceRoot, $"private-{index}"));
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$source_key", $"private/photo-{index}.jpg");
        command.Parameters.AddWithValue("$revision_id", revisionId);
        command.Parameters.AddWithValue("$hash", new string("abcdef"[index % 6], 64));
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$now", now.AddMinutes(index).ToString("O"));
        await command.ExecuteNonQueryAsync();
        return faceId;
    }

    private static async Task SeedDuplicateSuggestionsAsync(
        SqliteCatalogueDatabase database,
        FaceOccurrenceId faceId,
        PersonId sourcePersonId,
        PersonId targetPersonId,
        DateTimeOffset now)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
            VALUES
                ($face_id, $source_person_id, 'sface', $model_hash, 0.95, 'rejected', $now),
                ($face_id, $target_person_id, 'sface', $model_hash, 0.90, 'pending', $now);

            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
            SELECT $face_id, 'sface', $model_hash, 1, id, 0.05, $now
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_id
              AND suggested_person_id = $source_person_id;

            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank,
                suggestion_id, score_margin, generated_at_utc)
            SELECT $face_id, 'sface', $model_hash, 2, id, 0.05, $now
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_id
              AND suggested_person_id = $target_person_id;

            INSERT INTO identity_suggestion_review_actions (
                suggestion_id, action_kind, review_action_id, actor, note, created_at_utc)
            SELECT id, 'reject', NULL, 'local-reviewer', 'Not this duplicate', $now
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_id
              AND suggested_person_id = $source_person_id;
            """;
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$source_person_id", sourcePersonId.ToString());
        command.Parameters.AddWithValue("$target_person_id", targetPersonId.ToString());
        command.Parameters.AddWithValue("$model_hash", new string('e', 64));
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        await command.ExecuteNonQueryAsync();
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

    private static async Task<string> ReadStringAsync(
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

        return (string)(await command.ExecuteScalarAsync())!;
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
