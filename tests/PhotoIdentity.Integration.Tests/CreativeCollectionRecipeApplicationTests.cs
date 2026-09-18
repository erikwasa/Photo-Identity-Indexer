using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CreativeCollectionRecipeApplicationTests
{
    [Fact]
    public async Task Recipe_persists_regenerates_deterministically_and_preserves_context_provenance()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);

            CatalogueAssetRevision anchor = await CreateRevisionAsync(catalogue, directory, "anchor.jpg", 'a');
            CatalogueAssetRevision context1 = await CreateRevisionAsync(catalogue, directory, "context-1.jpg", 'b');
            CatalogueAssetRevision context2 = await CreateRevisionAsync(catalogue, directory, "context-2.jpg", 'c');
            CatalogueAssetRevision context3 = await CreateRevisionAsync(catalogue, directory, "context-3.jpg", 'd');
            await tags.AddManualTagAsync(anchor.Id, "Creative/Recipe", "test");
            await SetTakenAtAsync(database, anchor.Id, new DateTime(2026, 2, 1, 10, 0, 0));
            await SetTakenAtAsync(database, context1.Id, new DateTime(2026, 2, 1, 10, 5, 0));
            await SetTakenAtAsync(database, context2.Id, new DateTime(2026, 2, 1, 10, 10, 0));
            await SetTakenAtAsync(database, context3.Id, new DateTime(2026, 2, 1, 10, 15, 0));

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Recipe anchors",
                new SmartCollectionFilter(tags: ["Creative/Recipe"]));

            CreativeCollectionPreviewResponse firstPreview;
            await using (CreativeRecipeApiFactory factory = new(databasePath))
            {
                using HttpClient client = factory.CreateClient();
                using HttpResponseMessage saveResponse = await client.PutAsJsonAsync(
                    $"/api/smart-collections/{saved.Id}/creative-recipe",
                    new CreativeCollectionRecipeRequest(2, "focused"));
                saveResponse.EnsureSuccessStatusCode();

                CreativeCollectionRecipeResponse recipe =
                    await saveResponse.Content.ReadFromJsonAsync<CreativeCollectionRecipeResponse>()
                    ?? throw new InvalidOperationException();
                Assert.Equal(saved.Id.ToString(), recipe.AnchorCollectionId);
                Assert.Equal(2, recipe.TargetCount);
                Assert.Equal("focused", recipe.ContextStrength);
                Assert.Equal(CreativeCollectionRecipe.DefaultMomentGapMinutes, recipe.MomentGapMinutes);
                Assert.Equal(CreativeCollectionSelectionPolicy.BalancedV1.Version, recipe.SelectionPolicyVersion);

                firstPreview =
                    await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                        $"/api/smart-collections/{saved.Id}/creative-recipe/preview")
                    ?? throw new InvalidOperationException();
                Assert.Equal(1, firstPreview.DirectAnchorCount);
                Assert.Equal(2, firstPreview.AddedContextCount);
                Assert.Equal(3, firstPreview.TotalCandidateCount);
                Assert.Equal(2, firstPreview.SelectedCount);
                Assert.Contains(firstPreview.Candidates, candidate =>
                    candidate.RevisionId == anchor.Id.ToString() &&
                    candidate.Kind == CreativeCollectionCandidateKinds.DirectAnchor);
                foreach (CreativeCollectionPreviewCandidateResponse context in
                         firstPreview.Candidates.Where(candidate =>
                             candidate.Kind == CreativeCollectionCandidateKinds.ContextualAddition))
                {
                    CreativeCollectionContextReasonResponse reason = Assert.Single(context.ContextReasons);
                    Assert.Contains(anchor.Id.ToString(), reason.AnchorRevisionIds);
                }
            }

            await using (CreativeRecipeApiFactory reopenedFactory = new(databasePath))
            {
                using HttpClient client = reopenedFactory.CreateClient();
                CreativeCollectionRecipeResponse reopened =
                    await client.GetFromJsonAsync<CreativeCollectionRecipeResponse>(
                        $"/api/smart-collections/{saved.Id}/creative-recipe")
                    ?? throw new InvalidOperationException();
                Assert.Equal(2, reopened.TargetCount);
                Assert.Equal("focused", reopened.ContextStrength);

                CreativeCollectionPreviewResponse regenerated =
                    await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                        $"/api/smart-collections/{saved.Id}/creative-recipe/preview")
                    ?? throw new InvalidOperationException();
                Assert.Equal(
                    firstPreview.SelectedCandidates.Select(candidate => candidate.RevisionId),
                    regenerated.SelectedCandidates.Select(candidate => candidate.RevisionId));

                using HttpResponseMessage update = await client.PutAsJsonAsync(
                    $"/api/smart-collections/{saved.Id}/creative-recipe",
                    new CreativeCollectionRecipeRequest(10, "broad"));
                update.EnsureSuccessStatusCode();

                CreativeCollectionPreviewResponse broadened =
                    await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                        $"/api/smart-collections/{saved.Id}/creative-recipe/preview")
                    ?? throw new InvalidOperationException();
                Assert.Equal(3, broadened.AddedContextCount);
                Assert.Equal(4, broadened.TotalCandidateCount);
                Assert.Equal(4, broadened.SelectedCount);

                using HttpResponseMessage snapshotResponse = await client.PostAsync(
                    $"/api/smart-collections/{saved.Id}/creative-recipe/slideshow-snapshot",
                    content: null);
                snapshotResponse.EnsureSuccessStatusCode();
                SmartCollectionSlideshowSnapshotResponse snapshot =
                    await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>()
                    ?? throw new InvalidOperationException();
                Assert.Equal(
                    broadened.SelectedCandidates.Select(candidate => candidate.RevisionId),
                    snapshot.Items.Select(item => item.RevisionId));
            }

            SqliteSmartCollectionQueryRepository query = new(database);
            SmartCollectionPhotoPage exact = await query.QueryAsync(saved.Filter);
            Assert.Equal(anchor.Id, Assert.Single(exact.Items).RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Presentation_preferences_change_creative_selection_without_changing_exact_membership()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);

            CatalogueAssetRevision avoided = await CreateRevisionAsync(catalogue, directory, "avoid.jpg", '1');
            CatalogueAssetRevision automatic = await CreateRevisionAsync(catalogue, directory, "automatic.jpg", '2');
            CatalogueAssetRevision preferred = await CreateRevisionAsync(catalogue, directory, "prefer.jpg", '3');

            foreach (CatalogueAssetRevision revision in new[] { avoided, automatic, preferred })
            {
                await tags.AddManualTagAsync(revision.Id, "Creative/Preferences", "test");
            }

            await SetTakenAtAsync(database, avoided.Id, new DateTime(2026, 3, 1, 10, 0, 0));
            await SetTakenAtAsync(database, automatic.Id, new DateTime(2026, 4, 1, 10, 0, 0));
            await SetTakenAtAsync(database, preferred.Id, new DateTime(2026, 5, 1, 10, 0, 0));

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Preference anchors",
                new SmartCollectionFilter(tags: ["Creative/Preferences"]));

            await using CreativeRecipeApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage avoidResponse = await client.PutAsJsonAsync(
                $"/api/collections/photos/{avoided.Id}/presentation-preference",
                new PhotoPresentationPreferenceMutationRequest("avoid"));
            avoidResponse.EnsureSuccessStatusCode();

            using HttpResponseMessage preferResponse = await client.PutAsJsonAsync(
                $"/api/collections/photos/{preferred.Id}/presentation-preference",
                new PhotoPresentationPreferenceMutationRequest("prefer"));
            preferResponse.EnsureSuccessStatusCode();

            CreativeCollectionPreviewResponse preview =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-preview?targetCount=2&momentGapMinutes=30")
                ?? throw new InvalidOperationException();

            Assert.Equal(3, preview.DirectAnchorCount);
            Assert.Equal(2, preview.SelectedCount);
            Assert.DoesNotContain(
                preview.SelectedCandidates,
                item => item.RevisionId == avoided.Id.ToString());
            Assert.Contains(
                preview.SelectedCandidates,
                item => item.RevisionId == preferred.Id.ToString());
            CreativeCollectionSelectedCandidateResponse preferredSelected =
                preview.SelectedCandidates.Single(item => item.RevisionId == preferred.Id.ToString());
            Assert.Contains(
                preferredSelected.SelectionReasons,
                reason => reason.Code == CreativeCollectionSelectionReasonCodes.PresentationPrefer);

            SqliteSmartCollectionQueryRepository query = new(database);
            SmartCollectionPhotoPage exact = await query.QueryAsync(saved.Filter);
            Assert.Equal(3, exact.Total);
            Assert.Contains(exact.Items, item => item.RevisionId == avoided.Id);

            using HttpResponseMessage clearResponse = await client.DeleteAsync(
                $"/api/collections/photos/{avoided.Id}/presentation-preference");
            clearResponse.EnsureSuccessStatusCode();
            PhotoPresentationPreferenceResponse cleared =
                await clearResponse.Content.ReadFromJsonAsync<PhotoPresentationPreferenceResponse>()
                ?? throw new InvalidOperationException();

            Assert.Null(cleared.Preference);
            Assert.Equal(2, cleared.History.Count);
            Assert.Equal("clear", cleared.History[0].ActionKind);
            Assert.Equal("avoid", cleared.History[1].Preference);

            CreativeCollectionPreviewResponse restored =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-preview?targetCount=3&momentGapMinutes=30")
                ?? throw new InvalidOperationException();
            Assert.Equal(3, restored.SelectedCount);
            Assert.Contains(
                restored.SelectedCandidates,
                item => item.RevisionId == avoided.Id.ToString());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Saved_recipe_keeps_zero_anchor_collection_empty()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "No recipe matches",
                new SmartCollectionFilter(tags: ["missing/recipe/tag"]));

            await using CreativeRecipeApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage save = await client.PutAsJsonAsync(
                $"/api/smart-collections/{saved.Id}/creative-recipe",
                new CreativeCollectionRecipeRequest(50, "balanced"));
            save.EnsureSuccessStatusCode();

            CreativeCollectionPreviewResponse preview =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-recipe/preview")
                ?? throw new InvalidOperationException();

            Assert.True(preview.NoAnchors);
            Assert.Equal(0, preview.TotalCandidateCount);
            Assert.Equal(0, preview.SelectedCount);
            Assert.Empty(preview.SelectedCandidates);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Saved_recipe_reduces_large_anchor_result_to_target()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);

            for (int index = 0; index < 12; index++)
            {
                CatalogueAssetRevision revision = await CreateRevisionAsync(
                    catalogue,
                    directory,
                    $"large-{index:D2}.jpg",
                    "0123456789ab"[index]);
                await tags.AddManualTagAsync(revision.Id, "Creative/Large", "test");
                await SetTakenAtAsync(
                    database,
                    revision.Id,
                    new DateTime(2026, 1, 1, 10, 0, 0).AddDays(index * 10));
            }

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Large recipe",
                new SmartCollectionFilter(tags: ["Creative/Large"]));

            await using CreativeRecipeApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage save = await client.PutAsJsonAsync(
                $"/api/smart-collections/{saved.Id}/creative-recipe",
                new CreativeCollectionRecipeRequest(5, "balanced"));
            save.EnsureSuccessStatusCode();

            CreativeCollectionPreviewResponse preview =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-recipe/preview")
                ?? throw new InvalidOperationException();

            Assert.Equal(12, preview.DirectAnchorCount);
            Assert.Equal(12, preview.TotalCandidateCount);
            Assert.Equal(5, preview.SelectedCount);
            Assert.Equal(5, preview.SelectedCandidates.Select(candidate => candidate.RevisionId).Distinct().Count());
            Assert.True(preview.RepresentedTimePeriodCount >= 3);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Recipe_is_deleted_with_its_anchor_collection()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Cascade recipe",
                new SmartCollectionFilter());

            SqliteCreativeCollectionRecipeRepository recipes =
                new(database, TimeProvider.System);
            await recipes.UpsertAsync(
                saved.Id,
                CreativeCollectionRecipe.DefaultSettings);

            Assert.True(await definitions.DeleteAsync(saved.Id));
            Assert.Null(await recipes.GetAsync(saved.Id));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task SetTakenAtAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        DateTime takenAtLocal)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_capture_metadata (
                asset_revision_id,
                taken_at_local,
                utc_offset_minutes,
                latitude,
                longitude,
                extracted_at_utc)
            VALUES ($revision_id, $taken_at_local, NULL, NULL, NULL, $extracted_at_utc)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                taken_at_local = excluded.taken_at_local,
                extracted_at_utc = excluded.extracted_at_utc;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.Value.ToString("D"));
        command.Parameters.AddWithValue("$taken_at_local", takenAtLocal.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"));
        command.Parameters.AddWithValue("$extracted_at_utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<CatalogueAssetRevision> CreateRevisionAsync(
        SqliteAssetCatalogueRepository catalogue,
        string root,
        string sourceKey,
        char hashCharacter)
    {
        string sourceRoot = Path.Combine(root, Path.GetFileNameWithoutExtension(sourceKey));
        Directory.CreateDirectory(sourceRoot);
        DateTimeOffset now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
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

    private sealed class CreativeRecipeApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public CreativeRecipeApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
