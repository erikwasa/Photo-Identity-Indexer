using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class AssignmentAuditEndpointPagingTests
{
    [Fact]
    public async Task Catalogue_audit_pages_are_deterministic_and_non_overlapping()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            PostgresTestCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            Seeded seeded = await SeedAsync(database);

            await using ReviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            AssignmentAuditPageResponse first = Assert.IsType<AssignmentAuditPageResponse>(
                await client.GetFromJsonAsync<AssignmentAuditPageResponse>(
                    "/api/review/assigned-faces/audit?source=automatic&offset=0&limit=2"));
            AssignmentAuditPageResponse second = Assert.IsType<AssignmentAuditPageResponse>(
                await client.GetFromJsonAsync<AssignmentAuditPageResponse>(
                    "/api/review/assigned-faces/audit?source=automatic&offset=2&limit=2"));

            Assert.Equal(3, first.Total);
            Assert.Equal(0, first.Offset);
            Assert.Equal([seeded.AdaWeakFaceId, seeded.AdaStrongFaceId], first.Items.Select(item => item.Id));

            Assert.Equal(3, second.Total);
            Assert.Equal(2, second.Offset);
            AssignmentAuditFaceResponse last = Assert.Single(second.Items);
            Assert.Equal(seeded.BobFaceId, last.Id);

            Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<Seeded> SeedAsync(PostgresTestCatalogueDatabase database)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string adaId = Guid.NewGuid().ToString("D");
        string bobId = Guid.NewGuid().ToString("D");
        string adaWeakFaceId = Guid.NewGuid().ToString("D");
        string adaStrongFaceId = Guid.NewGuid().ToString("D");
        string bobFaceId = Guid.NewGuid().ToString("D");
        string modelHash = new('a', 64);
        string created = DateTimeOffset.UnixEpoch.ToString("O");

        await using PostgresCompatibilityConnection connection = await database.OpenConnectionAsync();
        using PostgresCompatibilityCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source, 'local-folder', 'audit-page-root', $created);
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                VALUES
                    ($ada, 'Ada', $created, NULL),
                    ($bob, 'Bob', $created, NULL);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES
                    ($asset1, $source, 'ada-weak.jpg', $created, $created),
                    ($asset2, $source, 'ada-strong.jpg', $created, $created),
                    ($asset3, $source, 'bob.jpg', $created, $created);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
                VALUES
                    ($revision1, $asset1, $hash1, 100, $created, 'image/jpeg', 640, 480),
                    ($revision2, $asset2, $hash2, 100, $created, 'image/jpeg', 640, 480),
                    ($revision3, $asset3, $hash3, 100, $created, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($face1, $revision1, 0, $created),
                    ($face2, $revision2, 0, $created),
                    ($face3, $revision3, 0, $created);

            INSERT INTO person_labels (
                person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES
                    ($ada, $face1, 'manual', 'identity-matcher:auto', $time1),
                    ($ada, $face2, 'manual', 'identity-matcher:auto', $time2),
                    ($bob, $face3, 'manual', 'identity-matcher:auto', $time3);
            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, created_at_utc, reversed_at_utc, reverses_action_id)
                VALUES
                    ($face1, 'assign', $ada,
                        (SELECT id FROM person_labels WHERE face_occurrence_id = $face1),
                        'identity-matcher:auto', $time1, NULL, NULL),
                    ($face2, 'assign', $ada,
                        (SELECT id FROM person_labels WHERE face_occurrence_id = $face2),
                        'identity-matcher:auto', $time2, NULL, NULL),
                    ($face3, 'assign', $bob,
                        (SELECT id FROM person_labels WHERE face_occurrence_id = $face3),
                        'identity-matcher:auto', $time3, NULL, NULL);

            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES
                    ($face1, $ada, 'sface', $model_hash, 0.91, 'accepted', $time1),
                    ($face2, $ada, 'sface', $model_hash, 0.96, 'accepted', $time2),
                    ($face3, $bob, 'sface', $model_hash, 0.94, 'accepted', $time3);
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
                        WHEN face_occurrence_id = $face1 THEN 0.05
                        WHEN face_occurrence_id = $face2 THEN 0.20
                        ELSE 0.10
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
                    ON action.face_occurrence_id = suggestion.face_occurrence_id;
            """;

        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$ada", adaId);
        command.Parameters.AddWithValue("$bob", bobId);
        command.Parameters.AddWithValue("$face1", adaWeakFaceId);
        command.Parameters.AddWithValue("$face2", adaStrongFaceId);
        command.Parameters.AddWithValue("$face3", bobFaceId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$created", created);

        for (int index = 1; index <= 3; index++)
        {
            command.Parameters.AddWithValue($"$asset{index}", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"$revision{index}", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue($"$hash{index}", new string((char)('b' + index - 1), 64));
        }

        command.Parameters.AddWithValue("$time1", Timestamp(10, 0));
        command.Parameters.AddWithValue("$time2", Timestamp(10, 5));
        command.Parameters.AddWithValue("$time3", Timestamp(10, 10));
        await command.ExecuteNonQueryAsync();

        return new Seeded(adaWeakFaceId, adaStrongFaceId, bobFaceId);
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

    private sealed record Seeded(string AdaWeakFaceId, string AdaStrongFaceId, string BobFaceId);

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
