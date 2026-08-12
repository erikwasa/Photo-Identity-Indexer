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

            PhotoTagResponse[] firstAdd = await PostTagAsync(client, revisionId, "  Beach/Day  ");
            Assert.Single(firstAdd);
            Assert.Equal("Beach/Day", firstAdd[0].Name);
            Assert.Equal("manual", firstAdd[0].Source);

            PhotoTagResponse[] secondAdd = await PostTagAsync(client, revisionId, "beach/day");
            Assert.Single(secondAdd);
            Assert.Equal("Beach/Day", secondAdd[0].Name);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                Assert.Equal(1, await ReadCountAsync(connection, "SELECT COUNT(*) FROM photo_tags;"));
                Assert.Equal(1, await ReadCountAsync(connection, "SELECT COUNT(*) FROM photo_tag_actions;"));
            }

            using HttpResponseMessage remove = await client.DeleteAsync(
                $"/api/collections/photos/{revisionId}/tags?name={Uri.EscapeDataString("BEACH/DAY")}");
            remove.EnsureSuccessStatusCode();
            PhotoTagResponse[] afterRemove =
                await remove.Content.ReadFromJsonAsync<PhotoTagResponse[]>() ?? [];
            Assert.Empty(afterRemove);

            PhotoTagResponse[] afterReAdd = await PostTagAsync(client, revisionId, "Beach/Day");
            Assert.Single(afterReAdd);

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                Assert.Equal(1, await ReadCountAsync(connection, "SELECT COUNT(*) FROM photo_tags;"));
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
    public async Task Removing_manual_assignment_does_not_remove_model_evidence_extension_rows()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            AssetRevisionId revisionId = await CreateRevisionAsync(databasePath, directory);
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqlitePhotoTagRepository repository = new(database, TimeProvider.System);

            await repository.AddManualTagAsync(revisionId, "volleyball", "test-maintainer");

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand insertEvidence = connection.CreateCommand();
                insertEvidence.CommandText = """
                    INSERT INTO photo_tag_model_evidence (
                        asset_revision_id, tag_id, model_id, model_hash, score, score_kind, observed_at_utc)
                    SELECT $revision_id, id, 'candidate-model', $model_hash, 0.82, 'cosine', $observed_at_utc
                    FROM photo_tags
                    WHERE normalized_name = 'volleyball';
                    """;
                insertEvidence.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                insertEvidence.Parameters.AddWithValue("$model_hash", new string('b', 64));
                insertEvidence.Parameters.AddWithValue("$observed_at_utc", DateTimeOffset.UtcNow.ToString("O"));
                await insertEvidence.ExecuteNonQueryAsync();
            }

            await repository.RemoveManualTagAsync(revisionId, "Volleyball", "test-maintainer");
            Assert.Empty(await repository.GetManualTagsAsync(revisionId));

            await using SqliteConnection verify = await database.OpenConnectionAsync();
            Assert.Equal(1, await ReadCountAsync(verify, "SELECT COUNT(*) FROM photo_tag_model_evidence;"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<PhotoTagResponse[]> PostTagAsync(
        HttpClient client,
        AssetRevisionId revisionId,
        string name)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/collections/photos/{revisionId}/tags",
            new PhotoTagMutationRequest(name));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PhotoTagResponse[]>() ?? [];
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
