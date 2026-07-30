using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PersonAuditApplicationTests
{
    [Fact]
    public async Task Audit_returns_active_assignments_and_flags_only_current_model_disagreements()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededAudit seeded = await SeedAuditAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string scope = $"modelId=sface&modelHash={seeded.ModelHash}";

            PersonAuditPageResponse page = Assert.IsType<PersonAuditPageResponse>(
                await client.GetFromJsonAsync<PersonAuditPageResponse>(
                    $"/api/review/people/{seeded.AdaPersonId}/assigned-faces" +
                    $"?{scope}&sort=disagreement-first&limit=10"));

            Assert.Equal("Ada", page.Person.DisplayName);
            Assert.Equal(3, page.Total);
            Assert.Equal(1, page.DisagreementCount);
            Assert.Equal("disagreement-first", page.Sort);
            Assert.Equal(seeded.DifferentSuggestionFaceId, page.Items[0].Id);

            PersonAuditFaceResponse disagreement = page.Items[0];
            Assert.True(disagreement.SuggestionDisagrees);
            Assert.Equal("Bob", Assert.IsType<ReviewTopSuggestionResponse>(
                disagreement.TopSuggestion).Person.DisplayName);
            Assert.Equal("Ada", disagreement.AssignedPerson.DisplayName);

            PersonAuditFaceResponse agreement = page.Items.Single(
                item => item.Id == seeded.SameSuggestionFaceId);
            Assert.False(agreement.SuggestionDisagrees);
            Assert.Equal("Ada", Assert.IsType<ReviewTopSuggestionResponse>(
                agreement.TopSuggestion).Person.DisplayName);

            PersonAuditFaceResponse rejectedSuggestion = page.Items.Single(
                item => item.Id == seeded.RejectedSuggestionFaceId);
            Assert.False(rejectedSuggestion.SuggestionDisagrees);
            Assert.Null(rejectedSuggestion.TopSuggestion);

            string json = await client.GetStringAsync(
                $"/api/review/people/{seeded.AdaPersonId}/assigned-faces" +
                $"?{scope}&sort=disagreement-first&limit=10");
            Assert.DoesNotContain(seeded.SourceRoot, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(seeded.CropStorageRoot, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storage_path", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("embedding", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Audit_supports_disagreement_filter_pagination_and_stable_assignment_order()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededAudit seeded = await SeedAuditAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string scope = $"modelId=sface&modelHash={seeded.ModelHash}";

            PersonAuditPageResponse disagreements = Assert.IsType<PersonAuditPageResponse>(
                await client.GetFromJsonAsync<PersonAuditPageResponse>(
                    $"/api/review/people/{seeded.AdaPersonId}/assigned-faces" +
                    $"?{scope}&disagreementsOnly=true&sort=assigned-desc&limit=10"));
            Assert.Single(disagreements.Items);
            Assert.Equal(seeded.DifferentSuggestionFaceId, disagreements.Items[0].Id);

            PersonAuditPageResponse oldest = Assert.IsType<PersonAuditPageResponse>(
                await client.GetFromJsonAsync<PersonAuditPageResponse>(
                    $"/api/review/people/{seeded.AdaPersonId}/assigned-faces" +
                    $"?{scope}&sort=assigned-asc&offset=0&limit=2"));
            Assert.Equal(3, oldest.Total);
            Assert.Equal(
                new[] { seeded.RejectedSuggestionFaceId, seeded.DifferentSuggestionFaceId },
                oldest.Items.Select(item => item.Id).ToArray());

            PersonAuditPageResponse secondPage = Assert.IsType<PersonAuditPageResponse>(
                await client.GetFromJsonAsync<PersonAuditPageResponse>(
                    $"/api/review/people/{seeded.AdaPersonId}/assigned-faces" +
                    $"?{scope}&sort=assigned-asc&offset=2&limit=2"));
            Assert.Single(secondPage.Items);
            Assert.Equal(seeded.SameSuggestionFaceId, secondPage.Items[0].Id);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Audit_rejects_invalid_scope_and_missing_people()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededAudit seeded = await SeedAuditAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage incompleteModel = await client.GetAsync(
                $"/api/review/people/{seeded.AdaPersonId}/assigned-faces?modelId=sface");
            Assert.Equal(HttpStatusCode.BadRequest, incompleteModel.StatusCode);

            using HttpResponseMessage disagreementWithoutModel = await client.GetAsync(
                $"/api/review/people/{seeded.AdaPersonId}/assigned-faces?disagreementsOnly=true");
            Assert.Equal(HttpStatusCode.BadRequest, disagreementWithoutModel.StatusCode);

            using HttpResponseMessage invalidSort = await client.GetAsync(
                $"/api/review/people/{seeded.AdaPersonId}/assigned-faces?sort=alphabetical");
            Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);

            using HttpResponseMessage missingPerson = await client.GetAsync(
                $"/api/review/people/{Guid.NewGuid():D}/assigned-faces");
            Assert.Equal(HttpStatusCode.NotFound, missingPerson.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededAudit> SeedAuditAsync(SqliteCatalogueDatabase database)
    {
        string sourceRoot = Path.Combine(
            Path.GetTempPath(),
            "private-person-audit",
            Guid.NewGuid().ToString("N"));
        string cropStorageRoot = Path.Combine(sourceRoot, "private-crops");
        string sourceId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string sameFaceId = Guid.NewGuid().ToString("D");
        string differentFaceId = Guid.NewGuid().ToString("D");
        string rejectedFaceId = Guid.NewGuid().ToString("D");
        string modelHash = new('a', 64);
        string now = new DateTimeOffset(2026, 7, 30, 22, 30, 0, TimeSpan.Zero).ToString("O");

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
                VALUES
                    ($same_asset_id, $source_id, 'same.jpg', $now, $now),
                    ($different_asset_id, $source_id, 'different.jpg', $now, $now),
                    ($rejected_asset_id, $source_id, 'rejected.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES
                    ($same_revision_id, $same_asset_id, $same_hash, 100, $now, 'image/jpeg', 640, 480),
                    ($different_revision_id, $different_asset_id, $different_hash, 100, $now, 'image/jpeg', 640, 480),
                    ($rejected_revision_id, $rejected_asset_id, $rejected_hash, 100, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($same_face_id, $same_revision_id, 0, $same_face_created),
                    ($different_face_id, $different_revision_id, 0, $different_face_created),
                    ($rejected_face_id, $rejected_revision_id, 0, $rejected_face_created);
            INSERT INTO face_observations (
                face_occurrence_id, detector_model_id, detector_model_hash,
                confidence, bounding_box_json, landmarks_json, observed_at_utc)
                VALUES
                    ($same_face_id, 'detector', $detector_hash, 0.90, '{}', '[]', $now),
                    ($different_face_id, 'detector', $detector_hash, 0.40, '{}', '[]', $now),
                    ($rejected_face_id, 'detector', $detector_hash, 0.70, '{}', '[]', $now);
            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256,
                storage_path, width, height, created_at_utc)
                VALUES
                    ($same_crop_id, $same_face_id, 'test', $same_crop_hash, $same_crop_path, 112, 112, $now),
                    ($different_crop_id, $different_face_id, 'test', $different_crop_hash, $different_crop_path, 112, 112, $now),
                    ($rejected_crop_id, $rejected_face_id, 'test', $rejected_crop_hash, $rejected_crop_path, 112, 112, $now);
            INSERT INTO person_labels (
                person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES
                    ($ada_person_id, $same_face_id, 'manual', 'audit:test', $same_assigned),
                    ($ada_person_id, $different_face_id, 'manual', 'audit:test', $different_assigned),
                    ($ada_person_id, $rejected_face_id, 'manual', 'audit:test', $rejected_assigned);
            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, created_at_utc, reversed_at_utc, reverses_action_id)
                SELECT
                    label.face_occurrence_id,
                    'assign',
                    label.person_id,
                    label.id,
                    'audit:test',
                    label.assigned_at_utc,
                    NULL,
                    NULL
                FROM person_labels AS label
                WHERE label.person_id = $ada_person_id;
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($same_face_id, $ada_person_id, 'sface', $model_hash, 0.93, 'pending', $now),
                    ($different_face_id, $bob_person_id, 'sface', $model_hash, 0.82, 'pending', $now),
                    ($rejected_face_id, $bob_person_id, 'sface', $model_hash, 0.80, 'rejected', $now);
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
                        WHEN face_occurrence_id = $same_face_id THEN 0.15
                        WHEN face_occurrence_id = $different_face_id THEN 0.04
                        ELSE 0.03
                    END,
                    created_at_utc
                FROM identity_suggestions;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$ada_person_id", adaPersonId);
        command.Parameters.AddWithValue("$bob_person_id", bobPersonId);
        command.Parameters.AddWithValue("$same_face_id", sameFaceId);
        command.Parameters.AddWithValue("$different_face_id", differentFaceId);
        command.Parameters.AddWithValue("$rejected_face_id", rejectedFaceId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$detector_hash", new string('d', 64));
        command.Parameters.AddWithValue("$now", now);

        string[] names = ["same", "different", "rejected"];
        for (int index = 0; index < names.Length; index++)
        {
            string name = names[index];
            string revisionId = Guid.NewGuid().ToString("D");
            command.Parameters.AddWithValue($"${name}_asset_id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"${name}_revision_id", revisionId);
            command.Parameters.AddWithValue($"${name}_hash", new string((char)('e' + index), 64));
            command.Parameters.AddWithValue($"${name}_crop_id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"${name}_crop_hash", new string((char)('h' + index), 64));
            command.Parameters.AddWithValue(
                $"${name}_crop_path",
                Path.Combine(cropStorageRoot, $"{name}.png"));
        }

        command.Parameters.AddWithValue(
            "$same_face_created",
            new DateTimeOffset(2026, 7, 30, 22, 0, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue(
            "$different_face_created",
            new DateTimeOffset(2026, 7, 30, 21, 59, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue(
            "$rejected_face_created",
            new DateTimeOffset(2026, 7, 30, 21, 58, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue(
            "$same_assigned",
            new DateTimeOffset(2026, 7, 30, 22, 20, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue(
            "$different_assigned",
            new DateTimeOffset(2026, 7, 30, 22, 10, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue(
            "$rejected_assigned",
            new DateTimeOffset(2026, 7, 30, 22, 5, 0, TimeSpan.Zero).ToString("O"));
        await command.ExecuteNonQueryAsync();

        return new SeededAudit(
            sourceRoot,
            cropStorageRoot,
            adaPersonId,
            modelHash,
            sameFaceId,
            differentFaceId,
            rejectedFaceId);
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

    private sealed record SeededAudit(
        string SourceRoot,
        string CropStorageRoot,
        string AdaPersonId,
        string ModelHash,
        string SameSuggestionFaceId,
        string DifferentSuggestionFaceId,
        string RejectedSuggestionFaceId);

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
