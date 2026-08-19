using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoDetailsApplicationTests
{
    [Fact]
    public async Task Details_returns_file_name_and_confirmed_people_without_private_paths_or_suggestions()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededPhoto seeded = await SeedPhotoAsync(database, directory);

            Assert.False(Directory.Exists(seeded.SourceRoot));

            await using PhotoDetailsApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            PhotoDetailsResponse details = Assert.IsType<PhotoDetailsResponse>(
                await client.GetFromJsonAsync<PhotoDetailsResponse>(
                    $"/api/collections/photos/{seeded.RevisionId}/details"));

            Assert.Equal(seeded.RevisionId, details.RevisionId);
            Assert.Equal("IMG_0001.JPG", details.FileName);
            PhotoDetailsPersonResponse person = Assert.Single(details.People);
            Assert.Equal(seeded.ConfirmedPersonId, person.Id);
            Assert.Equal("Ada", person.DisplayName);
            Assert.Equal(2, person.ConfirmedFaceCount);
            Assert.False(person.ManualPresence);

            string json = await client.GetStringAsync(
                $"/api/collections/photos/{seeded.RevisionId}/details");
            Assert.DoesNotContain(seeded.SourceRoot, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-folder", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1970/01", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bob", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Details_rejects_invalid_revision_and_returns_not_found_for_missing_revision()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using PhotoDetailsApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage invalid = await client.GetAsync(
                "/api/collections/photos/not-a-guid/details");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

            using HttpResponseMessage missing = await client.GetAsync(
                $"/api/collections/photos/{Guid.NewGuid():D}/details");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededPhoto> SeedPhotoAsync(
        SqliteCatalogueDatabase database,
        string directory)
    {
        string sourceRoot = Path.Combine(directory, "do-not-open", "private-source");
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string revisionId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string confirmedFaceOneId = Guid.NewGuid().ToString("D");
        string confirmedFaceTwoId = Guid.NewGuid().ToString("D");
        string suggestedFaceId = Guid.NewGuid().ToString("D");
        string now = new DateTimeOffset(2026, 8, 16, 20, 30, 0, TimeSpan.Zero).ToString("O");

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
                VALUES ($asset_id, $source_id, '1970/01/private-folder/IMG_0001.JPG', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES ($revision_id, $asset_id, $content_hash, 12345, $now, 'image/jpeg', 2000, 1500);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES
                    ($confirmed_face_one_id, $revision_id, 0, $now),
                    ($confirmed_face_two_id, $revision_id, 1, $now),
                    ($suggested_face_id, $revision_id, 2, $now);
            INSERT INTO person_labels (
                person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES
                    ($ada_person_id, $confirmed_face_one_id, 'manual', 'details:test', $now),
                    ($ada_person_id, $confirmed_face_two_id, 'manual', 'details:test', $now);
            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, created_at_utc, reversed_at_utc, reverses_action_id)
                SELECT
                    label.face_occurrence_id,
                    'assign',
                    label.person_id,
                    label.id,
                    'details:test',
                    label.assigned_at_utc,
                    NULL,
                    NULL
                FROM person_labels AS label
                WHERE label.person_id = $ada_person_id;
            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
                VALUES ($suggested_face_id, $bob_person_id, 'sface', $model_hash, 0.91, 'pending', $now);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$revision_id", revisionId);
        command.Parameters.AddWithValue("$ada_person_id", adaPersonId);
        command.Parameters.AddWithValue("$bob_person_id", bobPersonId);
        command.Parameters.AddWithValue("$confirmed_face_one_id", confirmedFaceOneId);
        command.Parameters.AddWithValue("$confirmed_face_two_id", confirmedFaceTwoId);
        command.Parameters.AddWithValue("$suggested_face_id", suggestedFaceId);
        command.Parameters.AddWithValue("$content_hash", new string('a', 64));
        command.Parameters.AddWithValue("$model_hash", new string('b', 64));
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();

        return new SeededPhoto(sourceRoot, revisionId, adaPersonId);
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

    private sealed record SeededPhoto(
        string SourceRoot,
        string RevisionId,
        string ConfirmedPersonId);

    private sealed class PhotoDetailsApiFactory : PhotoIdentityApiTestFactory
    {
        public PhotoDetailsApiFactory(string databasePath)
            : base(databasePath)
        {
        }
    }
}
