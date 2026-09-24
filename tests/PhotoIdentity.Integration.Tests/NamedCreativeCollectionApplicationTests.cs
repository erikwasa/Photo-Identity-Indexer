using System.Globalization;
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

public sealed class NamedCreativeCollectionApplicationTests
{
    [Fact]
    public async Task Legacy_singleton_migrates_to_named_collection_and_allows_siblings()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SmartCollectionDefinition anchor = await definitions.CreateAsync(
                "Family trips",
                new SmartCollectionFilter());

            CreativeCollectionRecipeSettings legacySettings =
                CreativeCollectionRecipeSettings.Create(
                    targetCount: 37,
                    CreativeCollectionContextPolicy.FocusedV1.Version,
                    noveltyEnabled: true);
            DateTimeOffset created = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO creative_collection_recipes (
                        anchor_collection_id,
                        target_count,
                        moment_gap_minutes,
                        moment_policy_version,
                        context_policy_version,
                        selection_policy_version,
                        ordering_policy_version,
                        novelty_enabled,
                        created_at_utc,
                        updated_at_utc)
                    VALUES (
                        $anchor,
                        $target,
                        $gap,
                        $moment,
                        $context,
                        $selection,
                        $ordering,
                        1,
                        $created,
                        $created);
                    """;
                insert.Parameters.AddWithValue("$anchor", anchor.Id.ToString());
                insert.Parameters.AddWithValue("$target", legacySettings.TargetCount);
                insert.Parameters.AddWithValue("$gap", legacySettings.MomentGapMinutes);
                insert.Parameters.AddWithValue("$moment", legacySettings.MomentPolicyVersion);
                insert.Parameters.AddWithValue("$context", legacySettings.ContextPolicyVersion);
                insert.Parameters.AddWithValue("$selection", legacySettings.SelectionPolicyVersion);
                insert.Parameters.AddWithValue("$ordering", legacySettings.OrderingPolicyVersion);
                insert.Parameters.AddWithValue("$created", created.ToString("O", CultureInfo.InvariantCulture));
                await insert.ExecuteNonQueryAsync();
            }

            SqliteCreativeCollectionRecipeRepository repository = new(database, TimeProvider.System);
            CreativeCollectionRecipe migrated = Assert.Single(await repository.ListForAnchorAsync(anchor.Id));
            Assert.Equal(anchor.Id.Value, migrated.Id.Value);
            Assert.Equal("Family trips Creative", migrated.Name);
            Assert.Equal(37, migrated.TargetCount);
            Assert.Equal(CreativeCollectionContextPolicy.FocusedV1.Version, migrated.ContextPolicyVersion);
            Assert.True(migrated.NoveltyEnabled);

            CreativeCollectionRecipe sibling = await repository.CreateAsync(
                anchor.Id,
                "Short family version",
                CreativeCollectionRecipeSettings.Create(
                    targetCount: 8,
                    CreativeCollectionContextPolicy.BalancedV1.Version));

            Assert.NotEqual(migrated.Id, sibling.Id);
            Assert.Equal(2, (await repository.ListForAnchorAsync(anchor.Id)).Count);

            CreativeCollectionRecipe renamed = await repository.UpdateAsync(
                sibling.Id,
                "Long family version",
                CreativeCollectionRecipeSettings.Create(
                    targetCount: 80,
                    CreativeCollectionContextPolicy.BroadV1.Version));
            Assert.Equal("Long family version", renamed.Name);
            Assert.Equal(80, renamed.TargetCount);

            CreativeCollectionRecipe migratedStill = Assert.IsType<CreativeCollectionRecipe>(
                await repository.GetAsync(migrated.Id));
            Assert.Equal("Family trips Creative", migratedStill.Name);
            Assert.Equal(37, migratedStill.TargetCount);

            Assert.True(await repository.DeleteAsync(migrated.Id));
            CreativeCollectionRecipe remaining = Assert.Single(await repository.ListForAnchorAsync(anchor.Id));
            Assert.Equal(sibling.Id, remaining.Id);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Named_collections_share_anchor_but_keep_independent_api_lifecycle_and_snapshot_identity()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SqliteAssetCatalogueRepository catalogue = new(database);

            SmartCollectionDefinition anchor = await definitions.CreateAsync(
                "All family photos",
                new SmartCollectionFilter());
            CatalogueAssetRevision revision = await CreateRevisionAsync(catalogue, directory);

            await using NamedCreativeFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            CreativeCollectionRecipeResponse shortVersion = await CreateAsync(
                client,
                anchor,
                "Short favorites",
                targetCount: 1,
                contextStrength: "focused");
            CreativeCollectionRecipeResponse longVersion = await CreateAsync(
                client,
                anchor,
                "Long story",
                targetCount: 12,
                contextStrength: "broad");

            Assert.NotEqual(shortVersion.Id, longVersion.Id);
            Assert.Equal(anchor.Id.ToString(), shortVersion.AnchorCollectionId);
            Assert.Equal(anchor.Id.ToString(), longVersion.AnchorCollectionId);

            CreativeCollectionRecipeResponse[] anchorList =
                await client.GetFromJsonAsync<CreativeCollectionRecipeResponse[]>(
                    $"/api/smart-collections/{anchor.Id}/creative-collections")
                ?? throw new InvalidOperationException();
            Assert.Equal(2, anchorList.Length);

            using HttpResponseMessage renameResponse = await client.PutAsJsonAsync(
                $"/api/creative-collections/{shortVersion.Id}",
                new CreativeCollectionRecipeRequest(2, "balanced", false, "Renamed favorites"));
            renameResponse.EnsureSuccessStatusCode();

            CreativeCollectionRecipeResponse unchangedLong =
                await client.GetFromJsonAsync<CreativeCollectionRecipeResponse>(
                    $"/api/creative-collections/{longVersion.Id}")
                ?? throw new InvalidOperationException();
            Assert.Equal("Long story", unchangedLong.Name);
            Assert.Equal(12, unchangedLong.TargetCount);
            Assert.Equal("broad", unchangedLong.ContextStrength);

            using HttpResponseMessage snapshotResponse = await client.PostAsync(
                $"/api/creative-collections/{shortVersion.Id}/slideshow-snapshot",
                content: null);
            snapshotResponse.EnsureSuccessStatusCode();
            SmartCollectionSlideshowSnapshotResponse snapshot =
                await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>()
                ?? throw new InvalidOperationException();
            Assert.Equal(shortVersion.Id, snapshot.CollectionId);
            Assert.Equal("Renamed favorites", snapshot.CollectionName);
            Assert.Contains(snapshot.Items, item => item.RevisionId == revision.Id.ToString());

            using HttpResponseMessage playbackCompatibility = await client.PostAsync(
                $"/api/smart-collections/{shortVersion.Id}/creative-recipe/slideshow-snapshot",
                content: null);
            playbackCompatibility.EnsureSuccessStatusCode();
            SmartCollectionSlideshowSnapshotResponse compatibleSnapshot =
                await playbackCompatibility.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>()
                ?? throw new InvalidOperationException();
            Assert.Equal(shortVersion.Id, compatibleSnapshot.CollectionId);
            Assert.Equal("Renamed favorites", compatibleSnapshot.CollectionName);

            using HttpResponseMessage delete = await client.DeleteAsync(
                $"/api/creative-collections/{longVersion.Id}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

            CreativeCollectionRecipeResponse[] all =
                await client.GetFromJsonAsync<CreativeCollectionRecipeResponse[]>("/api/creative-collections")
                ?? throw new InvalidOperationException();
            CreativeCollectionRecipeResponse remaining = Assert.Single(all);
            Assert.Equal(shortVersion.Id, remaining.Id);
            Assert.Equal("Renamed favorites", remaining.Name);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<CreativeCollectionRecipeResponse> CreateAsync(
        HttpClient client,
        SmartCollectionDefinition anchor,
        string name,
        int targetCount,
        string contextStrength)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/smart-collections/{anchor.Id}/creative-collections",
            new CreativeCollectionRecipeRequest(targetCount, contextStrength, false, name));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreativeCollectionRecipeResponse>()
            ?? throw new InvalidOperationException();
    }

    private static async Task<CatalogueAssetRevision> CreateRevisionAsync(
        SqliteAssetCatalogueRepository catalogue,
        string directory)
    {
        DateTimeOffset now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(sourceId, "local-folder", directory, now);
        CatalogueAsset asset = new(assetId, sourceId, "family.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string('a', 64)),
            1234,
            now,
            "image/jpeg",
            640,
            480);
        await catalogue.SaveRevisionAsync(source, asset, revision);
        return revision;
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.NamedCreativeTests",
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

    private sealed class NamedCreativeFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public NamedCreativeFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
