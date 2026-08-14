using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionPersistenceTests
{
    [Fact]
    public async Task Repository_round_trips_canonical_definition_and_enforces_unique_name()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteSmartCollectionRepository repository = new(database, TimeProvider.System);
            PersonId firstPerson = PersonId.New();
            PersonId secondPerson = PersonId.New();

            SmartCollectionDefinition created = await repository.CreateAsync(
                "  Summer\t 2025  ",
                new SmartCollectionFilter(
                    people: [secondPerson, firstPerson],
                    peopleMatch: SmartCollectionMatchModes.Any,
                    tags: [" Trips / Italy ", " Family "],
                    tagMatch: SmartCollectionMatchModes.All,
                    location: new SmartCollectionGeoBounds(40, 10, 44, 15),
                    taken: SmartCollectionDateRange.Parse("2025/05/01-2025/05/10")));

            Assert.Equal("Summer 2025", created.Name);
            Assert.Equal(SmartCollectionMatchModes.Any, created.Filter.PeopleMatch);
            Assert.Equal(SmartCollectionMatchModes.All, created.Filter.TagMatch);
            Assert.Equal(["family", "trips/italy"], created.Filter.Tags);
            Assert.Equal(new DateOnly(2025, 5, 1), created.Filter.Taken?.From);
            Assert.Equal(new DateOnly(2025, 5, 10), created.Filter.Taken?.To);

            SmartCollectionDefinition listed = Assert.Single(await repository.ListAsync());
            Assert.Equal(created.Id, listed.Id);
            SmartCollectionDefinition? reopened = await repository.GetAsync(created.Id);
            Assert.NotNull(reopened);
            Assert.Equal(created.Filter.Tags, reopened.Filter.Tags);
            Assert.Equal(created.Filter.People.Select(person => person.ToString()), reopened.Filter.People.Select(person => person.ToString()));

            await Assert.ThrowsAsync<SmartCollectionNameConflictException>(() => repository.CreateAsync(
                "summer 2025",
                new SmartCollectionFilter()));

            SmartCollectionDefinition? updated = await repository.UpdateAsync(
                created.Id,
                " Italy archive ",
                new SmartCollectionFilter(
                    tags: ["Trips/Italy"],
                    tagMatch: SmartCollectionMatchModes.Any,
                    taken: SmartCollectionDateRange.Parse("2020-2021")));
            Assert.NotNull(updated);
            Assert.Equal("Italy archive", updated.Name);
            Assert.Equal(["trips/italy"], updated.Filter.Tags);
            Assert.Equal(new DateOnly(2020, 1, 1), updated.Filter.Taken?.From);
            Assert.Equal(new DateOnly(2021, 12, 31), updated.Filter.Taken?.To);
            Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
            Assert.True(updated.UpdatedAtUtc >= created.UpdatedAtUtc);

            Assert.True(await repository.DeleteAsync(created.Id));
            Assert.False(await repository.DeleteAsync(created.Id));
            Assert.Null(await repository.GetAsync(created.Id));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(0, await ReadCountAsync(connection, "SELECT COUNT(*) FROM smart_collections;"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Saved_definition_re_evaluates_current_catalogue_instead_of_copying_membership()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SqliteSmartCollectionQueryRepository query = new(database);

            CatalogueAssetRevision first = await CreateRevisionAsync(catalogue, directory, "first.jpg", 'a');
            await tags.AddManualTagAsync(first.Id, "Trips/Italy", "test");
            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Italy",
                new SmartCollectionFilter(tags: ["Trips/Italy"]));

            SmartCollectionDefinition reopened =
                await definitions.GetAsync(saved.Id) ?? throw new InvalidOperationException();
            SmartCollectionPhotoPage firstEvaluation = await query.QueryAsync(reopened.Filter);
            Assert.Equal(first.Id, Assert.Single(firstEvaluation.Items).RevisionId);

            CatalogueAssetRevision second = await CreateRevisionAsync(catalogue, directory, "second.jpg", 'b');
            await tags.AddManualTagAsync(second.Id, "trips/italy", "test");

            SmartCollectionDefinition reevaluatedDefinition =
                await definitions.GetAsync(saved.Id) ?? throw new InvalidOperationException();
            SmartCollectionPhotoPage secondEvaluation = await query.QueryAsync(reevaluatedDefinition.Filter);
            Assert.Equal(2, secondEvaluation.Total);
            Assert.Contains(secondEvaluation.Items, item => item.RevisionId == first.Id);
            Assert.Contains(secondEvaluation.Items, item => item.RevisionId == second.Id);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(
                0,
                await ReadCountAsync(
                    connection,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'smart_collection_memberships';"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Saved_collection_API_supports_create_list_get_update_query_and_delete()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SmartCollectionApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            SmartCollectionDefinitionRequest createRequest = new(
                "  Recent Italy ",
                Tags: [" Trips / Italy "],
                TagMatch: SmartCollectionMatchModes.Any,
                Taken: "2025/05/01-2025/05/10");
            using HttpResponseMessage create = await client.PostAsJsonAsync(
                "/api/smart-collections",
                createRequest);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            SmartCollectionDefinitionResponse created =
                await create.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException();
            Assert.Equal("Recent Italy", created.Name);
            Assert.Equal(["trips/italy"], created.Filter.Tags);
            Assert.Equal("2025-05-01", created.Filter.Taken?.From);
            Assert.Equal("2025-05-10", created.Filter.Taken?.To);

            SmartCollectionDefinitionResponse[] listed =
                await client.GetFromJsonAsync<SmartCollectionDefinitionResponse[]>("/api/smart-collections") ?? [];
            Assert.Equal(created.Id, Assert.Single(listed).Id);

            SmartCollectionDefinitionResponse reopened =
                await client.GetFromJsonAsync<SmartCollectionDefinitionResponse>(
                    $"/api/smart-collections/{created.Id}")
                ?? throw new InvalidOperationException();
            Assert.Equal(created.Id, reopened.Id);

            using HttpResponseMessage duplicate = await client.PostAsJsonAsync(
                "/api/smart-collections",
                createRequest with { Name = "recent italy" });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            SmartCollectionDefinitionRequest updateRequest = new(
                "Italy years",
                Tags: ["Trips/Italy"],
                Taken: "2020-2021");
            using HttpResponseMessage update = await client.PutAsJsonAsync(
                $"/api/smart-collections/{created.Id}",
                updateRequest);
            update.EnsureSuccessStatusCode();
            SmartCollectionDefinitionResponse updated =
                await update.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException();
            Assert.Equal("Italy years", updated.Name);
            Assert.Equal("2020-01-01", updated.Filter.Taken?.From);
            Assert.Equal("2021-12-31", updated.Filter.Taken?.To);

            SmartCollectionPageResponse page =
                await client.GetFromJsonAsync<SmartCollectionPageResponse>(
                    $"/api/smart-collections/{created.Id}/query?offset=0&limit=10")
                ?? throw new InvalidOperationException();
            Assert.Equal(created.Id, page.CollectionId);
            Assert.Equal("Italy years", page.CollectionName);
            Assert.Equal(0, page.Total);
            Assert.Equal("2020-01-01", page.Filter.Taken?.From);
            Assert.Equal("2021-12-31", page.Filter.Taken?.To);

            using HttpResponseMessage delete = await client.DeleteAsync(
                $"/api/smart-collections/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            using HttpResponseMessage missing = await client.GetAsync(
                $"/api/smart-collections/{created.Id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<CatalogueAssetRevision> CreateRevisionAsync(
        SqliteAssetCatalogueRepository catalogue,
        string root,
        string sourceKey,
        char hashCharacter)
    {
        string sourceRoot = Path.Combine(root, Path.GetFileNameWithoutExtension(sourceKey));
        Directory.CreateDirectory(sourceRoot);
        DateTimeOffset now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        return await catalogue.SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, now),
            new CatalogueAsset(assetId, sourceId, sourceKey, now),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(new string(hashCharacter, 64)),
                100,
                now,
                "image/jpeg",
                100,
                100));
    }

    private static async Task<long> ReadCountAsync(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
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
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SmartCollectionApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public SmartCollectionApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
