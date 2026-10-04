using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class AssignmentAuditApplicationTests
{
    [Fact]
    public async Task Catalogue_audit_filters_current_automatic_assignments_and_uses_accepted_provenance()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            PostgresTestCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededAssignmentAudit seeded = await SeedAuditAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            string modelScope =
                $"modelId=sface&modelHash={seeded.ModelHash}";

            AssignmentAuditPageResponse automatic = Assert.IsType<AssignmentAuditPageResponse>(
                await client.GetFromJsonAsync<AssignmentAuditPageResponse>(
                    $"/api/review/assigned-faces/audit?source=automatic&{modelScope}&limit=20"));

            Assert.Equal(3, automatic.Total);
            Assert.Equal("automatic", automatic.Source);
            Assert.Equal(
                new[] { seeded.AdaMultiFaceId, seeded.AdaAutoFaceId, seeded.BobAutoFaceId },
                automatic.Items.Select(item => item.Id).ToArray());

            AssignmentAuditFaceResponse weakest = automatic.Items[0];
            Assert.Equal("Ada", weakest.AssignedPerson.DisplayName);
            Assert.Equal("identity-matcher:auto-multi-evidence", weakest.AssignmentActor);
            ReviewTopSuggestionResponse accepted = Assert.IsType<ReviewTopSuggestionResponse>(
                weakest.AcceptedSuggestion);
            Assert.Equal(0.91, accepted.Score, 3);
            Assert.Equal(0.10, accepted.ScoreMargin);
            Assert.Equal("sface", accepted.ModelId);
            Assert.Equal(seeded.ModelHash, accepted.ModelHash);
            Assert.False(weakest.CurrentSuggestionDisagrees);

            Assert.DoesNotContain(
                automatic.Items,
                item => item.Id == seeded.CorrectedFaceId);

            string from = Uri.EscapeDataString(
                new DateTimeOffset(2026, 10, 4, 10, 10, 0, TimeSpan.Zero).ToString("O"));
            string to = Uri.EscapeDataString(
                new DateTimeOffset(2026, 10, 4, 10, 20, 0, TimeSpan.Zero).ToString("O"));
            AssignmentAuditPageResponse window = Assert.IsType<AssignmentAuditPageResponse>(
                await client.GetFromJsonAsync<AssignmentAuditPageResponse>(
                    $"/api/review/assigned-faces/audit?source=automatic&fromUtc={from}&toUtc={to}&limit=20"));
            AssignmentAuditFaceResponse inWindow = Assert.Single(window.Items);
            Assert.Equal(seeded.AdaMultiFaceId, inWindow.Id);
            Assert.Equal(
                new DateTimeOffset(2026, 10, 4, 10, 10, 0, TimeSpan.Zero),
                inWindow.AssignedAtUtc);

            AssignmentAuditPageResponse manual = Assert.IsType<AssignmentAuditPageResponse>(
                await client.GetFromJsonAsync<AssignmentAuditPageResponse>(
                    "/api/review/assigned-faces/audit?source=manual&limit=20"));
            Assert.Equal(2, manual.Total);
            Assert.Contains(manual.Items, item => item.Id == seeded.CorrectedFaceId && item.AssignedPerson.DisplayName == "Bob");
            Assert.Contains(manual.Items, item => item.Id == seeded.ManualFaceId && item.AssignedPerson.DisplayName == "Ada");

            using HttpResponseMessage invalid = await client.GetAsync(
                "/api/review/assigned-faces/audit?source=robot");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededAssignmentAudit> SeedAuditAsync(
        PostgresTestCatalogueDatabase database)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string adaAutoFaceId = Guid.NewGuid().ToString("D");
        string adaMultiFaceId = Guid.NewGuid().ToString("D");
        string bobAutoFaceId = Guid.NewGuid().ToString("D");
        string correctedFaceId = Guid.NewGuid().ToString("D");
        string manualFaceId = Guid.NewGuid().ToString("D");
        string modelHash = new('a', 64);
        string created = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero).ToString("O");

        await using PostgresCompatibilityConnection connection = await database.OpenConnectionAsync();
        using PostgresCompatibilityCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', 'audit-root', $created);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES
                    ($ada_person_id, 'Ada', $created, NULL),
                    ($bob_person_id, 'Bob', $created, NULL);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES
                    ($asset_1, $source_id, 'ada-auto.jpg', $created, $created),
                    ($asset_2, $source_id, 'ada-multi.jpg', $created, $created),
                    ($asset_3, $source_id, 'bob-auto.jpg', $created, $created),
                    ($asset_4, $source_id, 'corrected.jpg', $created, $created),
                    ($asset_5, $source_id, 'manual.jpg', $created, $created);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES
                    ($revision_1, $asset_1, $hash_1, 100, $created, 'image/jpeg', 640, 480),
                    ($revision_2, $asset_2, $hash_2, 100, $created, 'image/jpeg', 640, 480),
                    ($revision_3, $asset_3, $hash_3, 100, $created, 'image/jpeg', 640, 480),
                    ($revision_4, $asset_4, $hash_4, 100, $created, 'image/jpeg', 640, 480),
                    ($revision_5, $asset_5, $hash_5, 100, $created, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($face_1, $revision_1, 0, $created),
                    ($face_2, $revision_2, 0, $created),
                    ($face_3, $revision_3, 0, $created),
                    ($face_4, $revision_4, 0, $created),
                    ($face_5, $revision_5, 0, $created);

            INSERT INTO person_labels (
                person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES
                    ($ada_person_id, $face_1, 'manual', 'identity-matcher:auto', $time_1),
                    ($ada_person_id, $face_2, 'manual', 'identity-matcher:auto-multi-evidence', $time_2),
                    ($bob_person_id, $face_3, 'manual', 'identity-matcher:auto', $time_3),
                    ($ada_person_id, $face_4, 'manual', 'identity-matcher:auto', $time_4_auto),
                    ($bob_person_id, $face_4, 'manual', 'maintainer', $time_4_manual),
                    ($ada_person_id, $face_5, 'manual', 'maintainer', $time_5);

            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, created_at_utc, reversed_at_utc, reverses_action_id)
                VALUES
                    ($face_1, 'assign', $ada_person_id,
                        (SELECT id FROM person_labels WHERE person_id = $ada_person_id AND face_occurrence_id = $face_1),
                        'identity-matcher:auto', $time_1, NULL, NULL),
                    ($face_2, 'assign', $ada_person_id,
                        (SELECT id FROM person_labels WHERE person_id = $ada_person_id AND face_occurrence_id = $face_2),
                        'identity-matcher:auto-multi-evidence', $time_2, NULL, NULL),
                    ($face_3, 'assign', $bob_person_id,
                        (SELECT id FROM person_labels WHERE person_id = $bob_person_id AND face_occurrence_id = $face_3),
                        'identity-matcher:auto', $time_3, NULL, NULL),
                    ($face_4, 'assign', $ada_person_id,
                        (SELECT id FROM person_labels WHERE person_id = $ada_person_id AND face_occurrence_id = $face_4),
                        'identity-matcher:auto', $time_4_auto, NULL, NULL),
                    ($face_4, 'assign', $bob_person_id,
                        (SELECT id FROM person_labels WHERE person_id = $bob_person_id AND face_occurrence_id = $face_4),
                        'maintainer', $time_4_manual, NULL, NULL),
                    ($face_5, 'assign', $ada_person_id,
                        (SELECT id FROM person_labels WHERE person_id = $ada_person_id AND face_occurrence_id = $face_5),
                        'maintainer', $time_5, NULL, NULL);

            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($face_1, $ada_person_id, 'sface', $model_hash, 0.95, 'accepted', $time_1),
                    ($face_2, $ada_person_id, 'sface', $model_hash, 0.91, 'accepted', $time_2),
                    ($face_3, $bob_person_id, 'sface', $model_hash, 0.94, 'accepted', $time_3),
                    ($face_4, $ada_person_id, 'sface', $model_hash, 0.92, 'accepted', $time_4_auto);
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
                        WHEN face_occurrence_id = $face_1 THEN 0.20
                        WHEN face_occurrence_id = $face_2 THEN 0.10
                        WHEN face_occurrence_id = $face_3 THEN 0.15
                        ELSE 0.12
                    END,
                    created_at_utc
                FROM identity_suggestions;
            INSERT INTO identity_suggestion_review_actions (
                suggestion_id, action_kind, review_action_id, actor, created_at_utc)
                SELECT
                    suggestion.id,
                    'accept',
                    action.id,
                    action.actor,
                    action.created_at_utc
                FROM identity_suggestions AS suggestion
                INNER JOIN review_actions AS action
                    ON action.face_occurrence_id = suggestion.face_occurrence_id
                   AND action.actor IN ('identity-matcher:auto', 'identity-matcher:auto-multi-evidence');
            """;

        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$ada_person_id", adaPersonId);
        command.Parameters.AddWithValue("$bob_person_id", bobPersonId);
        command.Parameters.AddWithValue("$face_1", adaAutoFaceId);
        command.Parameters.AddWithValue("$face_2", adaMultiFaceId);
        command.Parameters.AddWithValue("$face_3", bobAutoFaceId);
        command.Parameters.AddWithValue("$face_4", correctedFaceId);
        command.Parameters.AddWithValue("$face_5", manualFaceId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$created", created);

        for (int index = 1; index <= 5; index++)
        {
            command.Parameters.AddWithValue($"$asset_{index}", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"$revision_{index}", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"$hash_{index}", new string((char)('b' + index - 1), 64));
        }

        command.Parameters.AddWithValue("$time_1", Timestamp(10, 0));
        command.Parameters.AddWithValue("$time_2", Timestamp(10, 10));
        command.Parameters.AddWithValue("$time_3", Timestamp(10, 20));
        command.Parameters.AddWithValue("$time_4_auto", Timestamp(10, 25));
        command.Parameters.AddWithValue("$time_4_manual", Timestamp(10, 30));
        command.Parameters.AddWithValue("$time_5", Timestamp(10, 40));
        await command.ExecuteNonQueryAsync();

        return new SeededAssignmentAudit(
            modelHash,
            adaAutoFaceId,
            adaMultiFaceId,
            bobAutoFaceId,
            correctedFaceId,
            manualFaceId);
    }

    private static string Timestamp(int hour, int minute) =>
        new DateTimeOffset(2026, 10, 4, hour, minute, 0, TimeSpan.Zero).ToString("O");

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

    private sealed record SeededAssignmentAudit(
        string ModelHash,
        string AdaAutoFaceId,
        string AdaMultiFaceId,
        string BobAutoFaceId,
        string CorrectedFaceId,
        string ManualFaceId);

    private sealed class ReviewApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public ReviewApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(
                "PhotoIdentity:Postgres:ConnectionString",
                PostgresTestCatalogueDatabase.GetCompatibilityConnectionString(_databasePath));
        }
    }
}
