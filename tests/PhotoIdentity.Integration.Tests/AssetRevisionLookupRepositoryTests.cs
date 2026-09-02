using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class AssetRevisionLookupRepositoryTests
{
    [Fact]
    public async Task Sqlite_adapter_preserves_revision_and_source_location_through_neutral_lookup()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 9, 2, 18, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(
                SourceId.New(),
                "local-folder",
                Path.Combine(directory, "source"),
                now);
            CatalogueAsset asset = new(
                AssetId.New(),
                source.Id,
                "family/photo.jpg",
                now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                1234,
                now,
                "image/jpeg",
                1200,
                800);
            await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            IAssetRevisionLookupRepository repository = new SqliteLocalBatchRepository(database);

            AssetRevisionLookup? byId = await repository.GetRevisionAsync(revision.Id);
            AssetRevisionLookup? bySourceIdentity = await repository.FindRevisionAsync(
                asset.SourceKey,
                revision.ContentHash);

            AssetRevisionLookup expected = new(
                revision.Id,
                asset.Id,
                source.Id,
                source.Kind,
                source.RootLocator,
                asset.SourceKey,
                revision.ContentHash,
                revision.SizeBytes,
                revision.MediaType);
            Assert.Equal(expected, byId);
            Assert.Equal(expected, bySourceIdentity);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Sqlite_adapter_returns_null_when_revision_lookup_does_not_exist()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IAssetRevisionLookupRepository repository = new SqliteLocalBatchRepository(database);

            Assert.Null(await repository.GetRevisionAsync(AssetRevisionId.New()));
            Assert.Null(await repository.FindRevisionAsync(
                "missing/photo.jpg",
                new Sha256Digest(new string('f', 64))));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "photo-identity-tests", Guid.NewGuid().ToString("N"));
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
