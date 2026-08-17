using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Persistence.Sqlite;
using WebPhotoTagDefinitionResponse = PhotoIdentity.Web.Contracts.PhotoTagDefinitionResponse;
using WebPhotoTagMutationRequest = PhotoIdentity.Web.Contracts.PhotoTagMutationRequest;
using WebPhotoTagResponse = PhotoIdentity.Web.Contracts.PhotoTagResponse;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoTagApplicationTests
{
    [Fact]
    public void Canonical_tag_name_collapses_whitespace_and_is_case_insensitive()
    {
        PhotoTagName tag = PhotoTagName.Parse("  Watching\t  Television  ");

        Assert.Equal("Watching Television", tag.DisplayName);
        Assert.Equal("watching television", tag.NormalizedName);
        Assert.Throws<ArgumentException>(() => PhotoTagName.Parse("Beach/Day"));
    }

    [Fact]
    public void Canonical_tag_path_normalizes_each_hierarchy_segment()
    {
        PhotoTagPath tag = PhotoTagPath.Parse("  Places / Sweden  /  Stockholm ");

        Assert.Equal("Places/Sweden/Stockholm", tag.DisplayValue);
        Assert.Equal("places/sweden/stockholm", tag.NormalizedValue);
        Assert.Equal("Stockholm", tag.Name.DisplayName);
        Assert.Equal("Places/Sweden", tag.ParentDisplayValue);
        Assert.Equal("places/sweden", tag.ParentNormalizedValue);
        Assert.Throws<ArgumentException>(() => PhotoTagPath.Parse("Places//Stockholm"));
    }

    [Fact]
    public async Task Manual_tag_endpoints_are_idempotent_auditable_and_revision_scoped()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory);

            await using PhotoTagApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            WebPhotoTagResponse[] firstAdd = await PostTagAsync(client, revisionId, "  Beach / Day  ");
            Assert.Single(firstAdd);
            Assert.Equal("Beach/Day", firstAdd[0].Name);
            Assert.Equal("Beach/Day", firstAdd[0].Value);
            Assert.NotNull(firstAdd[0].ParentId);
            Assert.Equal("Beach", firstAdd[0].ParentValue);
            Assert.Equal("manual", firstAdd[0].Source);

            WebPhotoTagResponse[] secondAdd = await PostTagAsync(client, revisionId, "beach/day");
            Assert.Single(secondAdd);
            Assert.Equal("Beach/Day", secondAdd[0].Value);

            WebPhotoTagDefinitionResponse[] canonical =
                await client.GetFromJsonAsync<WebPhotoTagDefinitionResponse[]>("/api/tags") ?? [];
            Assert.Equal(2, canonical.Length);
            WebPhotoTagDefinitionResponse root = Assert.Single(canonical, tag => tag.Value == "Beach");
            WebPhotoTagDefinitionResponse leaf = Assert.Single(canonical, tag => tag.Value == "Beach/Day");
            Assert.Equal("Beach", root.Name);
            Assert.Null(root.ParentId);
            Assert.Equal("Day", leaf.Name);
            Assert.Equal(root.Id, leaf.ParentId);
            Assert.Equal("Beach", leaf.ParentValue);
            Assert.Null(leaf.Color);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                Assert.Equal(2, await ReadCountAsync(connection, "SELECT COUNT(*) FROM photo_tags;"));
                Assert.Equal(1, await ReadCountAsync(connection, "SELECT COUNT(*) FROM photo_tag_actions;"));
            }

            using HttpResponseMessage remove = await client.DeleteAsync(
                $"/api/collections/photos/{revisionId}/tags?name={Uri.EscapeDataString("BEACH/DAY")}");
            remove.EnsureSuccessStatusCode();
            WebPhotoTagResponse[] afterRemove =
                await remove.Content.ReadFromJsonAsync<WebPhotoTagResponse[]>() ?? [];
            Assert.Empty(afterRemove);

            WebPhotoTagResponse[] afterReAdd = await PostTagAsync(client, revisionId, "Beach/Day");
            Assert.Single(afterReAdd);

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                Assert.Equal(2, await ReadCountAsync(connection, "SELECT COUNT(*) FROM photo_tags;"));
                Assert.Equal(3, await ReadCountAsync(connection, "SELECT COUNT(*) FROM photo_tag_actions;"));
            }

            AssetRevisionId missing = AssetRevisionId.New();
            using HttpResponseMessage missingResponse = await client.GetAsync(
                $"/api/collections/photos/{missing}/tags");
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Hosted_photo_route_and_web_tag_contract_are_available_for_the_same_revision()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory);

            await using PhotoTagApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage page = await client.GetAsync($"/photo/{revisionId}");
            page.EnsureSuccessStatusCode();
            string html = await page.Content.ReadAsStringAsync();
            Assert.Contains("blazor.webassembly.js", html, StringComparison.OrdinalIgnoreCase);

            WebPhotoTagResponse[] added = await PostTagAsync(client, revisionId, "Family archive");
            Assert.Single(added);
            Assert.Equal("Family archive", added[0].Name);
            Assert.Equal("Family archive", added[0].Value);

            WebPhotoTagResponse[] reloaded = await client.GetFromJsonAsync<WebPhotoTagResponse[]>(
                $"/api/collections/photos/{revisionId}/tags") ?? [];
            Assert.Single(reloaded);
            Assert.Equal(added[0], reloaded[0]);

            using HttpResponseMessage removed = await client.DeleteAsync(
                $"/api/collections/photos/{revisionId}/tags?name={Uri.EscapeDataString("family archive")}");
            removed.EnsureSuccessStatusCode();
            Assert.Empty(await removed.Content.ReadFromJsonAsync<WebPhotoTagResponse[]>() ?? []);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Removing_manual_assignment_retains_canonical_vocabulary_entry()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory);
            SqliteCatalogueDatabase database = new(databasePath);
            SqlitePhotoTagRepository repository = new(database, TimeProvider.System);

            await repository.AddManualTagAsync(revisionId, "volleyball", "test-maintainer");
            await repository.RemoveManualTagAsync(revisionId, "Volleyball", "test-maintainer");

            Assert.Empty(await repository.GetManualTagsAsync(revisionId));

            await using SqliteConnection verify = await database.OpenConnectionAsync();
            Assert.Equal(1, await ReadCountAsync(verify, "SELECT COUNT(*) FROM photo_tags;"));
            Assert.Equal(2, await ReadCountAsync(verify, "SELECT COUNT(*) FROM photo_tag_actions;"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Schema_version_thirteen_migration_is_preserved_when_upgrading_to_current_schema()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand rewind = connection.CreateCommand();
                rewind.CommandText = """
                    DROP TABLE photo_tag_actions;
                    DROP TABLE photo_tags;
                    DELETE FROM schema_migrations WHERE version >= 13;
                    PRAGMA user_version = 12;
                    """;
                await rewind.ExecuteNonQueryAsync();
            }

            await database.InitializeAsync();

            await using SqliteConnection verify = await database.OpenConnectionAsync();
            Assert.Equal(SqliteCatalogueDatabase.CurrentSchemaVersion, await ReadCountAsync(verify, "PRAGMA user_version;"));
            Assert.Equal(1, await ReadCountAsync(
                verify,
                "SELECT COUNT(*) FROM schema_migrations WHERE version = 13;"));
            Assert.Equal(1, await ReadCountAsync(
                verify,
                "SELECT COUNT(*) FROM schema_migrations WHERE version = 14;"));
            Assert.Equal(1, await ReadCountAsync(
                verify,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'photo_tags';"));
            Assert.Equal(1, await ReadCountAsync(
                verify,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'photo_tag_actions';"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<WebPhotoTagResponse[]> PostTagAsync(
        HttpClient client,
        AssetRevisionId revisionId,
        string value)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/collections/photos/{revisionId}/tags",
            new WebPhotoTagMutationRequest(value));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WebPhotoTagResponse[]>() ?? [];
    }

    private static async Task<AssetRevisionId> CreateRevisionAsync(string databasePath, string sourceRoot)
    {
        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        DateTimeOffset now = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, "photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(new string('a', 64)),
            123,
            now,
            "image/jpeg",
            100,
            100);
        return (await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(source, asset, revision)).Id;
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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class PhotoTagApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public PhotoTagApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
