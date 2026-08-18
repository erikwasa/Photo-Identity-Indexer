using System.Net;
using System.Net.Http.Json;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PersonSmartCollectionVisibilityApplicationTests
{
    [Fact]
    public async Task Hidden_state_persists_without_removing_people_from_review_and_can_be_reversed()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 18, 16, 0, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson ada = await reviewRepository.CreatePersonAsync("Ada", now);
            CatalogueReviewPerson grace = await reviewRepository.CreatePersonAsync("Grace", now.AddMinutes(1));

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            await SetHiddenAsync(client, grace.Id.ToString(), hidden: true);

            ReviewPersonResponse[] reviewPeople =
                await client.GetFromJsonAsync<ReviewPersonResponse[]>("/api/review/people") ?? [];
            Assert.Equal(["Ada", "Grace"], reviewPeople.Select(person => person.DisplayName));
            Assert.False(Assert.Single(reviewPeople, person => person.Id == ada.Id.ToString()).HiddenFromSmartCollections);
            Assert.True(Assert.Single(reviewPeople, person => person.Id == grace.Id.ToString()).HiddenFromSmartCollections);

            PersonMaintenancePersonResponse[] maintenancePeople =
                await client.GetFromJsonAsync<PersonMaintenancePersonResponse[]>(
                    "/api/review/people/maintenance") ?? [];
            Assert.False(Assert.Single(maintenancePeople, person => person.Id == ada.Id.ToString()).HiddenFromSmartCollections);
            Assert.True(Assert.Single(maintenancePeople, person => person.Id == grace.Id.ToString()).HiddenFromSmartCollections);

            IReadOnlySet<PhotoIdentity.Core.Identifiers.PersonId> persisted =
                await new SqlitePersonSmartCollectionVisibilityRepository(new SqliteCatalogueDatabase(databasePath))
                    .GetHiddenPersonIdsAsync();
            Assert.Contains(grace.Id, persisted);
            Assert.DoesNotContain(ada.Id, persisted);

            await SetHiddenAsync(client, grace.Id.ToString(), hidden: false);

            reviewPeople = await client.GetFromJsonAsync<ReviewPersonResponse[]>("/api/review/people") ?? [];
            Assert.False(Assert.Single(reviewPeople, person => person.Id == grace.Id.ToString()).HiddenFromSmartCollections);

            maintenancePeople = await client.GetFromJsonAsync<PersonMaintenancePersonResponse[]>(
                "/api/review/people/maintenance") ?? [];
            Assert.False(Assert.Single(maintenancePeople, person => person.Id == grace.Id.ToString()).HiddenFromSmartCollections);

            persisted = await new SqlitePersonSmartCollectionVisibilityRepository(new SqliteCatalogueDatabase(databasePath))
                .GetHiddenPersonIdsAsync();
            Assert.DoesNotContain(grace.Id, persisted);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Merge_preserves_the_surviving_person_visibility_and_discards_the_retired_source_preference()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 18, 16, 30, 0, TimeSpan.Zero);
            SqliteReviewRepository reviewRepository = new(database);

            CatalogueReviewPerson hiddenSource = await reviewRepository.CreatePersonAsync("Hidden source", now);
            CatalogueReviewPerson visibleTarget = await reviewRepository.CreatePersonAsync("Visible target", now.AddMinutes(1));
            CatalogueReviewPerson visibleSource = await reviewRepository.CreatePersonAsync("Visible source", now.AddMinutes(2));
            CatalogueReviewPerson hiddenTarget = await reviewRepository.CreatePersonAsync("Hidden target", now.AddMinutes(3));

            SqlitePersonSmartCollectionVisibilityRepository visibility = new(database);
            await visibility.SetHiddenAsync(hiddenSource.Id, true, now.AddMinutes(4));
            await visibility.SetHiddenAsync(hiddenTarget.Id, true, now.AddMinutes(5));

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            await MergeAsync(client, hiddenSource.Id.ToString(), visibleTarget.Id.ToString());
            await MergeAsync(client, visibleSource.Id.ToString(), hiddenTarget.Id.ToString());

            PersonMaintenancePersonResponse[] active =
                await client.GetFromJsonAsync<PersonMaintenancePersonResponse[]>(
                    "/api/review/people/maintenance") ?? [];
            Assert.False(Assert.Single(active, person => person.Id == visibleTarget.Id.ToString()).HiddenFromSmartCollections);
            Assert.True(Assert.Single(active, person => person.Id == hiddenTarget.Id.ToString()).HiddenFromSmartCollections);

            IReadOnlySet<PhotoIdentity.Core.Identifiers.PersonId> hidden = await visibility.GetHiddenPersonIdsAsync();
            Assert.Contains(hiddenTarget.Id, hidden);
            Assert.DoesNotContain(visibleTarget.Id, hidden);
            Assert.DoesNotContain(hiddenSource.Id, hidden);
            Assert.DoesNotContain(visibleSource.Id, hidden);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Visibility_endpoint_rejects_unknown_people()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await new SqliteCatalogueDatabase(databasePath).InitializeAsync();

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PutAsJsonAsync(
                $"/api/review/people/{Guid.NewGuid():D}/smart-collection-visibility",
                new SetPersonSmartCollectionVisibilityRequest(true));

            await response.EnsureStatusCodeWithDiagnosticBodyAsync(
                HttpStatusCode.NotFound,
                "unknown-person smart-collection visibility update");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task SetHiddenAsync(HttpClient client, string personId, bool hidden)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/review/people/{personId}/smart-collection-visibility",
            new SetPersonSmartCollectionVisibilityRequest(hidden));
        await response.EnsureSuccessWithDiagnosticBodyAsync("smart-collection visibility update");
    }

    private static async Task MergeAsync(HttpClient client, string sourcePersonId, string targetPersonId)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/review/people/{sourcePersonId}/merge",
            new MergePersonRequest(targetPersonId, true, "local-reviewer"));
        await response.EnsureSuccessWithDiagnosticBodyAsync("person merge");
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
}
