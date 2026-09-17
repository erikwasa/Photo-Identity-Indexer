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

public sealed class CreativeCollectionPreviewApplicationTests
{
    [Fact]
    public async Task Preview_preserves_exact_anchor_membership_and_adds_bounded_same_moment_context()
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
            CatalogueAssetRevision context = await CreateRevisionAsync(catalogue, directory, "context.jpg", 'b');
            CatalogueAssetRevision unrelated = await CreateRevisionAsync(catalogue, directory, "unrelated.jpg", 'c');
            await tags.AddManualTagAsync(anchor.Id, "Creative/Anchor", "test");
            await SetTakenAtAsync(database, anchor.Id, new DateTime(2026, 1, 2, 10, 0, 0));
            await SetTakenAtAsync(database, context.Id, new DateTime(2026, 1, 2, 10, 10, 0));
            await SetTakenAtAsync(database, unrelated.Id, new DateTime(2026, 1, 2, 12, 0, 0));

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Creative anchor",
                new SmartCollectionFilter(tags: ["Creative/Anchor"]));

            SqliteSmartCollectionQueryRepository query = new(database);
            SmartCollectionPhotoPage exactBefore = await query.QueryAsync(saved.Filter);
            Assert.Equal(anchor.Id, Assert.Single(exactBefore.Items).RevisionId);

            await using CreativePreviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            CreativeCollectionPreviewResponse preview =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-preview?momentGapMinutes=30")
                ?? throw new InvalidOperationException();

            Assert.Equal(1, preview.DirectAnchorCount);
            Assert.Equal(1, preview.AddedContextCount);
            Assert.Equal(2, preview.TotalCandidateCount);
            Assert.False(preview.NoAnchors);
            Assert.Contains(preview.Candidates, candidate =>
                candidate.RevisionId == anchor.Id.ToString() &&
                candidate.Kind == CreativeCollectionCandidateKinds.DirectAnchor);
            CreativeCollectionPreviewCandidateResponse added = Assert.Single(
                preview.Candidates,
                candidate => candidate.Kind == CreativeCollectionCandidateKinds.ContextualAddition);
            Assert.Equal(context.Id.ToString(), added.RevisionId);
            Assert.DoesNotContain(preview.Candidates, candidate => candidate.RevisionId == unrelated.Id.ToString());
            CreativeCollectionContextReasonResponse reason = Assert.Single(added.ContextReasons);
            Assert.Equal([anchor.Id.ToString()], reason.AnchorRevisionIds);

            SmartCollectionPhotoPage exactAfter = await query.QueryAsync(saved.Filter);
            Assert.Equal(anchor.Id, Assert.Single(exactAfter.Items).RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Preview_reports_explicit_no_anchor_result()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "No matches",
                new SmartCollectionFilter(tags: ["does/not/exist"]));

            await using CreativePreviewApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();
            CreativeCollectionPreviewResponse preview =
                await client.GetFromJsonAsync<CreativeCollectionPreviewResponse>(
                    $"/api/smart-collections/{saved.Id}/creative-preview")
                ?? throw new InvalidOperationException();

            Assert.True(preview.NoAnchors);
            Assert.Equal(0, preview.DirectAnchorCount);
            Assert.Equal(0, preview.AddedContextCount);
            Assert.Equal(0, preview.TotalCandidateCount);
            Assert.Empty(preview.Candidates);
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

    private sealed class CreativePreviewApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public CreativePreviewApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
