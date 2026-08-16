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

public sealed class ManualPhotoPeopleApplicationTests
{
    [Fact]
    public async Task Manual_person_add_remove_is_audited_without_creating_face_evidence()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededCatalogue seeded = await SeedCatalogueAsync(database, directory);

            await using ManualPeopleApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage addResponse = await client.PostAsJsonAsync(
                $"/api/collections/photos/{seeded.FirstRevisionId}/people",
                new PhotoPersonMutationRequest(seeded.AdaPersonId));
            Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
            PhotoDetailsResponse added = Assert.IsType<PhotoDetailsResponse>(
                await addResponse.Content.ReadFromJsonAsync<PhotoDetailsResponse>());
            PhotoDetailsPersonResponse manualAda = Assert.Single(added.People);
            Assert.Equal("Ada", manualAda.DisplayName);
            Assert.Equal(0, manualAda.ConfirmedFaceCount);
            Assert.True(manualAda.ManualPresence);

            await AssertFaceEvidenceCountsAsync(database, expected: 0);

            using HttpResponseMessage removeResponse = await client.DeleteAsync(
                $"/api/collections/photos/{seeded.FirstRevisionId}/people/{seeded.AdaPersonId}");
            Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
            PhotoDetailsResponse removed = Assert.IsType<PhotoDetailsResponse>(
                await removeResponse.Content.ReadFromJsonAsync<PhotoDetailsResponse>());
            Assert.Empty(removed.People);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand history = connection.CreateCommand();
            history.CommandText = """
                SELECT action_kind, actor
                FROM photo_person_actions
                WHERE asset_revision_id = $revision_id
                  AND person_id = $person_id
                ORDER BY id;
                """;
            history.Parameters.AddWithValue("$revision_id", seeded.FirstRevisionId);
            history.Parameters.AddWithValue("$person_id", seeded.AdaPersonId);
            await using SqliteDataReader reader = await history.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("add", reader.GetString(0));
            Assert.Equal("local-maintainer", reader.GetString(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("remove", reader.GetString(0));
            Assert.Equal("local-maintainer", reader.GetString(1));
            Assert.False(await reader.ReadAsync());

            await AssertFaceEvidenceCountsAsync(database, expected: 0);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Photo_details_and_smart_collections_union_face_and_manual_people()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededCatalogue seeded = await SeedCatalogueAsync(database, directory);
            await SeedConfirmedFaceAsync(database, seeded.FirstRevisionId, seeded.AdaPersonId);

            await using ManualPeopleApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage addBob = await client.PostAsJsonAsync(
                $"/api/collections/photos/{seeded.FirstRevisionId}/people",
                new PhotoPersonMutationRequest(seeded.BobPersonId));
            addBob.EnsureSuccessStatusCode();
            using HttpResponseMessage addAdaSecond = await client.PostAsJsonAsync(
                $"/api/collections/photos/{seeded.SecondRevisionId}/people",
                new PhotoPersonMutationRequest(seeded.AdaPersonId));
            addAdaSecond.EnsureSuccessStatusCode();
            using HttpResponseMessage addAdaFirst = await client.PostAsJsonAsync(
                $"/api/collections/photos/{seeded.FirstRevisionId}/people",
                new PhotoPersonMutationRequest(seeded.AdaPersonId));
            addAdaFirst.EnsureSuccessStatusCode();

            PhotoDetailsResponse details = Assert.IsType<PhotoDetailsResponse>(
                await client.GetFromJsonAsync<PhotoDetailsResponse>(
                    $"/api/collections/photos/{seeded.FirstRevisionId}/details"));
            Assert.Equal(2, details.People.Count);
            PhotoDetailsPersonResponse ada = Assert.Single(details.People, person => person.Id == seeded.AdaPersonId);
            Assert.Equal(1, ada.ConfirmedFaceCount);
            Assert.True(ada.ManualPresence);
            PhotoDetailsPersonResponse bob = Assert.Single(details.People, person => person.Id == seeded.BobPersonId);
            Assert.Equal(0, bob.ConfirmedFaceCount);
            Assert.True(bob.ManualPresence);

            SmartCollectionPageResponse allPage = await QueryPeopleAsync(
                client,
                [seeded.AdaPersonId, seeded.BobPersonId],
                "all");
            SmartCollectionPhotoResponse allPhoto = Assert.Single(allPage.Items);
            Assert.Equal(seeded.FirstRevisionId, allPhoto.RevisionId);

            SmartCollectionPageResponse anyPage = await QueryPeopleAsync(
                client,
                [seeded.AdaPersonId, seeded.BobPersonId],
                "any");
            Assert.Equal(2, anyPage.Total);
            Assert.Contains(anyPage.Items, photo => photo.RevisionId == seeded.FirstRevisionId);
            Assert.Contains(anyPage.Items, photo => photo.RevisionId == seeded.SecondRevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Person_merge_transfers_effective_manual_presence_without_rewriting_source_history()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededCatalogue seeded = await SeedCatalogueAsync(database, directory);

            SqlitePhotoPersonRepository manualRepository = new(database, TimeProvider.System);
            await manualRepository.AddManualPersonAsync(
                AssetRevisionId.From(Guid.Parse(seeded.FirstRevisionId)),
                PersonId.From(Guid.Parse(seeded.BobPersonId)),
                "merge:test");

            SqlitePersonMaintenanceRepository maintenance = new(database);
            await maintenance.MergeAsync(
                PersonId.From(Guid.Parse(seeded.BobPersonId)),
                PersonId.From(Guid.Parse(seeded.AdaPersonId)),
                confirmIrreversible: true,
                actor: "merge:test",
                createdAtUtc: DateTimeOffset.UtcNow);

            SqlitePhotoDetailsRepository detailsRepository = new(database);
            CataloguePhotoDetails details = Assert.IsType<CataloguePhotoDetails>(
                await detailsRepository.GetAsync(AssetRevisionId.From(Guid.Parse(seeded.FirstRevisionId))));
            CataloguePhotoDetailsPerson person = Assert.Single(details.People);
            Assert.Equal(seeded.AdaPersonId, person.PersonId.ToString());
            Assert.True(person.ManualPresence);
            Assert.Equal(0, person.ConfirmedFaceCount);

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand history = connection.CreateCommand();
                history.CommandText = """
                    SELECT person_id, action_kind, actor
                    FROM photo_person_actions
                    WHERE asset_revision_id = $revision_id
                    ORDER BY id;
                    """;
                history.Parameters.AddWithValue("$revision_id", seeded.FirstRevisionId);
                await using SqliteDataReader reader = await history.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(seeded.BobPersonId, reader.GetString(0));
                Assert.Equal("add", reader.GetString(1));
                Assert.Equal("merge:test", reader.GetString(2));
                Assert.True(await reader.ReadAsync());
                Assert.Equal(seeded.AdaPersonId, reader.GetString(0));
                Assert.Equal("add", reader.GetString(1));
                Assert.Equal("person-merge", reader.GetString(2));
                Assert.False(await reader.ReadAsync());
            }

            await using ManualPeopleApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage rejected = await client.PostAsJsonAsync(
                $"/api/collections/photos/{seeded.SecondRevisionId}/people",
                new PhotoPersonMutationRequest(seeded.BobPersonId));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            SmartCollectionPageResponse targetPage = await QueryPeopleAsync(
                client,
                [seeded.AdaPersonId],
                "all");
            Assert.Contains(targetPage.Items, photo => photo.RevisionId == seeded.FirstRevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SmartCollectionPageResponse> QueryPeopleAsync(
        HttpClient client,
        string[] people,
        string peopleMatch)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/smart-collections/query",
            new
            {
                people,
                peopleMatch,
                tags = Array.Empty<string>(),
                tagMatch = "all",
                location = (object?)null,
                taken = (string?)null,
                offset = 0,
                limit = 40,
            });
        response.EnsureSuccessStatusCode();
        return Assert.IsType<SmartCollectionPageResponse>(
            await response.Content.ReadFromJsonAsync<SmartCollectionPageResponse>());
    }

    private static async Task AssertFaceEvidenceCountsAsync(SqliteCatalogueDatabase database, int expected)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        foreach (string table in new[]
                 {
                     "face_occurrences",
                     "face_observations",
                     "face_crops",
                     "embeddings",
                     "review_actions",
                     "identity_suggestions",
                 })
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(expected, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
    }

    private static async Task<SeededCatalogue> SeedCatalogueAsync(
        SqliteCatalogueDatabase database,
        string directory)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string firstAssetId = Guid.NewGuid().ToString("D");
        string secondAssetId = Guid.NewGuid().ToString("D");
        string firstRevisionId = Guid.NewGuid().ToString("D");
        string secondRevisionId = Guid.NewGuid().ToString("D");
        string adaPersonId = Guid.NewGuid().ToString("D");
        string bobPersonId = Guid.NewGuid().ToString("D");
        string sourceRoot = Path.Combine(directory, "originals-do-not-exist");
        string now = new DateTimeOffset(2026, 8, 16, 21, 0, 0, TimeSpan.Zero).ToString("O");

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
                    ($first_asset_id, $source_id, 'archive/first.jpg', $now, $now),
                    ($second_asset_id, $source_id, 'archive/second.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES
                    ($first_revision_id, $first_asset_id, $first_hash, 100, $now, 'image/jpeg', 1000, 800),
                    ($second_revision_id, $second_asset_id, $second_hash, 100, $now, 'image/jpeg', 1000, 800);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$source_root", sourceRoot);
        command.Parameters.AddWithValue("$first_asset_id", firstAssetId);
        command.Parameters.AddWithValue("$second_asset_id", secondAssetId);
        command.Parameters.AddWithValue("$first_revision_id", firstRevisionId);
        command.Parameters.AddWithValue("$second_revision_id", secondRevisionId);
        command.Parameters.AddWithValue("$ada_person_id", adaPersonId);
        command.Parameters.AddWithValue("$bob_person_id", bobPersonId);
        command.Parameters.AddWithValue("$first_hash", new string('a', 64));
        command.Parameters.AddWithValue("$second_hash", new string('b', 64));
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync();

        Assert.False(Directory.Exists(sourceRoot));
        return new SeededCatalogue(firstRevisionId, secondRevisionId, adaPersonId, bobPersonId);
    }

    private static async Task SeedConfirmedFaceAsync(
        SqliteCatalogueDatabase database,
        string revisionId,
        string personId)
    {
        string faceId = Guid.NewGuid().ToString("D");
        string now = new DateTimeOffset(2026, 8, 16, 21, 5, 0, TimeSpan.Zero).ToString("O");

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, 0, $now);
            INSERT INTO person_labels (
                person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES ($person_id, $face_id, 'manual', 'manual-people:test', $now);
            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, created_at_utc, reversed_at_utc, reverses_action_id)
                SELECT
                    $face_id,
                    'assign',
                    $person_id,
                    id,
                    'manual-people:test',
                    $now,
                    NULL,
                    NULL
                FROM person_labels
                WHERE person_id = $person_id
                  AND face_occurrence_id = $face_id;
            """;
        command.Parameters.AddWithValue("$face_id", faceId);
        command.Parameters.AddWithValue("$revision_id", revisionId);
        command.Parameters.AddWithValue("$person_id", personId);
        command.Parameters.AddWithValue("$now", now);
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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record SeededCatalogue(
        string FirstRevisionId,
        string SecondRevisionId,
        string AdaPersonId,
        string BobPersonId);

    private sealed class ManualPeopleApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public ManualPeopleApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
