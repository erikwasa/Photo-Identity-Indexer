using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoPlaceFoundationApplicationTests
{
    [Fact]
    public void Place_path_hides_reserved_root_and_preserves_full_canonical_identity()
    {
        PhotoPlacePath place = PhotoPlacePath.Parse(" Places / Sweden / Stockholm region / Norrtälje ");

        Assert.Equal("Places/Sweden/Stockholm region/Norrtälje", place.CanonicalDisplayValue);
        Assert.Equal("places/sweden/stockholm region/norrtälje", place.CanonicalNormalizedValue);
        Assert.Equal("Sweden/Stockholm region/Norrtälje", place.DisplayValue);
        Assert.Equal("Norrtälje", place.Name);
        Assert.Equal("Sweden/Stockholm region", place.ParentDisplayValue);
        Assert.Throws<ArgumentException>(() => PhotoPlacePath.Parse("Places"));
    }

    [Fact]
    public async Task Generic_tag_api_reserves_places_and_hides_legacy_place_assignments()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SeededRevision seeded = await CreateRevisionAsync(databasePath, directory);
            SqliteCatalogueDatabase database = new(databasePath);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            await tags.AddManualTagAsync(seeded.RevisionId, "Places/Sweden/Stockholm", "legacy:test");

            await using PlaceApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            PhotoTagDefinitionResponse[] definitions =
                await client.GetFromJsonAsync<PhotoTagDefinitionResponse[]>("/api/tags") ?? [];
            Assert.DoesNotContain(definitions, tag => tag.Value.StartsWith("Places", StringComparison.OrdinalIgnoreCase));

            PhotoTagResponse[] activeTags = await client.GetFromJsonAsync<PhotoTagResponse[]>(
                $"/api/collections/photos/{seeded.RevisionId}/tags") ?? [];
            Assert.Empty(activeTags);

            using HttpResponseMessage rejectedAdd = await client.PostAsJsonAsync(
                $"/api/collections/photos/{seeded.RevisionId}/tags",
                new PhotoTagMutationRequest("places/Sweden/Gotland"));
            Assert.Equal(HttpStatusCode.BadRequest, rejectedAdd.StatusCode);

            using HttpResponseMessage rejectedRemove = await client.DeleteAsync(
                $"/api/collections/photos/{seeded.RevisionId}/tags?name={Uri.EscapeDataString("PLACES/Sweden/Stockholm")}");
            Assert.Equal(HttpStatusCode.BadRequest, rejectedRemove.StatusCode);

            using HttpResponseMessage normalAdd = await client.PostAsJsonAsync(
                $"/api/collections/photos/{seeded.RevisionId}/tags",
                new PhotoTagMutationRequest("Family/Travel"));
            normalAdd.EnsureSuccessStatusCode();
            PhotoTagResponse normal = Assert.Single(
                await normalAdd.Content.ReadFromJsonAsync<PhotoTagResponse[]>() ?? []);
            Assert.Equal("Family/Travel", normal.Value);

            PhotoPlaceStateResponse placeState = Assert.IsType<PhotoPlaceStateResponse>(
                await client.GetFromJsonAsync<PhotoPlaceStateResponse>(
                    $"/api/collections/photos/{seeded.RevisionId}/place"));
            Assert.NotNull(placeState.Place);
            Assert.Equal("Sweden/Stockholm", placeState.Place.Value);
            Assert.Equal("migration", placeState.Place.Source);
            Assert.False(Directory.Exists(seeded.SourceRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Place_set_replace_and_clear_keep_one_effective_place_with_append_only_history()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SeededRevision seeded = await CreateRevisionAsync(databasePath, directory);

            await using PlaceApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage firstSet = await client.PutAsJsonAsync(
                $"/api/collections/photos/{seeded.RevisionId}/place",
                new PhotoPlaceMutationRequest("Sweden/Stockholm region/Norrtälje"));
            firstSet.EnsureSuccessStatusCode();
            PhotoPlaceStateResponse firstState = Assert.IsType<PhotoPlaceStateResponse>(
                await firstSet.Content.ReadFromJsonAsync<PhotoPlaceStateResponse>());
            Assert.NotNull(firstState.Place);
            Assert.Equal("Sweden/Stockholm region/Norrtälje", firstState.Place.Value);
            Assert.Equal("manual", firstState.Place.Source);

            using HttpResponseMessage replacement = await client.PutAsJsonAsync(
                $"/api/collections/photos/{seeded.RevisionId}/place",
                new PhotoPlaceMutationRequest("Sweden/Gotland/Visby"));
            replacement.EnsureSuccessStatusCode();
            PhotoPlaceStateResponse replacedState = Assert.IsType<PhotoPlaceStateResponse>(
                await replacement.Content.ReadFromJsonAsync<PhotoPlaceStateResponse>());
            Assert.NotNull(replacedState.Place);
            Assert.Equal("Sweden/Gotland/Visby", replacedState.Place.Value);

            PhotoPlaceDefinitionResponse[] definitions =
                await client.GetFromJsonAsync<PhotoPlaceDefinitionResponse[]>("/api/places") ?? [];
            Assert.DoesNotContain(definitions, definition =>
                definition.Value.StartsWith("Places/", StringComparison.OrdinalIgnoreCase));
            PhotoPlaceDefinitionResponse sweden = Assert.Single(definitions, definition => definition.Value == "Sweden");
            PhotoPlaceDefinitionResponse gotland = Assert.Single(definitions, definition => definition.Value == "Sweden/Gotland");
            PhotoPlaceDefinitionResponse visby = Assert.Single(definitions, definition => definition.Value == "Sweden/Gotland/Visby");
            Assert.Null(sweden.ParentId);
            Assert.Equal(sweden.Id, gotland.ParentId);
            Assert.Equal(gotland.Id, visby.ParentId);

            using HttpResponseMessage clear = await client.DeleteAsync(
                $"/api/collections/photos/{seeded.RevisionId}/place");
            clear.EnsureSuccessStatusCode();
            PhotoPlaceStateResponse cleared = Assert.IsType<PhotoPlaceStateResponse>(
                await clear.Content.ReadFromJsonAsync<PhotoPlaceStateResponse>());
            Assert.Null(cleared.Place);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(3, await ReadCountAsync(
                connection,
                "SELECT COUNT(*) FROM photo_place_actions WHERE asset_revision_id = $revision_id;",
                seeded.RevisionId.ToString()));
            Assert.Equal(0, await ReadCountAsync(
                connection,
                "SELECT COUNT(*) FROM photo_tag_actions WHERE asset_revision_id = $revision_id;",
                seeded.RevisionId.ToString()));
            Assert.False(Directory.Exists(seeded.SourceRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Legacy_migration_uses_deepest_chain_but_surfaces_divergent_places_for_review()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SeededRevision coherent = await CreateRevisionAsync(databasePath, directory, "coherent.jpg");
            SeededRevision divergent = await CreateRevisionAsync(databasePath, directory, "divergent.jpg");
            SqliteCatalogueDatabase database = new(databasePath);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);

            await tags.AddManualTagAsync(coherent.RevisionId, "Places/Sweden", "legacy:test");
            await tags.AddManualTagAsync(coherent.RevisionId, "Places/Sweden/Stockholm", "legacy:test");
            await tags.AddManualTagAsync(divergent.RevisionId, "Places/Sweden/Stockholm", "legacy:test");
            await tags.AddManualTagAsync(divergent.RevisionId, "Places/USA/Stockholm", "legacy:test");

            await SqlitePhotoPlaceSchema.EnsureAndMigrateAsync(database);
            SqlitePhotoPlaceRepository places = new(database, TimeProvider.System);

            CataloguePhotoPlaceState coherentState = await places.GetStateAsync(coherent.RevisionId);
            Assert.NotNull(coherentState.Place);
            Assert.Equal("Sweden/Stockholm", coherentState.Place.Value);
            Assert.Equal("migration", coherentState.Place.SourceKind);
            Assert.Null(coherentState.MigrationConflict);

            CataloguePhotoPlaceState divergentState = await places.GetStateAsync(divergent.RevisionId);
            Assert.Null(divergentState.Place);
            Assert.NotNull(divergentState.MigrationConflict);
            Assert.Equal(2, divergentState.MigrationConflict.CandidateValues.Count);
            Assert.Contains("Sweden/Stockholm", divergentState.MigrationConflict.CandidateValues);
            Assert.Contains("USA/Stockholm", divergentState.MigrationConflict.CandidateValues);

            CataloguePlaceMigrationConflict conflict = Assert.Single(
                await places.GetMigrationConflictsAsync(),
                item => item.RevisionId == divergent.RevisionId);
            Assert.Equal(2, conflict.CandidateValues.Count);

            CataloguePhotoPlaceState resolved = await places.SetManualPlaceAsync(
                divergent.RevisionId,
                "Sweden/Stockholm",
                "test-maintainer");
            Assert.NotNull(resolved.Place);
            Assert.Equal("manual", resolved.Place.SourceKind);
            Assert.Null(resolved.MigrationConflict);
            Assert.DoesNotContain(
                await places.GetMigrationConflictsAsync(),
                item => item.RevisionId == divergent.RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SeededRevision> CreateRevisionAsync(
        string databasePath,
        string directory,
        string sourceKey = "photo.jpg")
    {
        SqliteCatalogueDatabase database = new(databasePath);
        await database.InitializeAsync();
        string sourceRoot = Path.Combine(
            directory,
            "originals-do-not-exist",
            Path.GetFileNameWithoutExtension(sourceKey));
        DateTimeOffset now = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, sourceKey, now);
        char hashCharacter = sourceKey.StartsWith("coherent", StringComparison.Ordinal) ? 'b'
            : sourceKey.StartsWith("divergent", StringComparison.Ordinal) ? 'c'
            : 'a';
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(new string(hashCharacter, 64)),
            123,
            now,
            "image/jpeg",
            100,
            100);
        CatalogueAssetRevision saved = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);
        return new SeededRevision(saved.Id, sourceRoot);
    }

    private static async Task<long> ReadCountAsync(
        SqliteConnection connection,
        string sql,
        string revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$revision_id", revisionId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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

    private sealed record SeededRevision(AssetRevisionId RevisionId, string SourceRoot);

    private sealed class PlaceApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public PlaceApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
