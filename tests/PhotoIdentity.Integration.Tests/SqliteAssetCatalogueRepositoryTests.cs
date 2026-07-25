using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteAssetCatalogueRepositoryTests
{
    [Fact]
    public async Task Save_revision_round_trips_typed_catalogue_records()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            (CatalogueSource source, CatalogueAsset asset, CatalogueAssetRevision revision) = CreateRecords();

            CatalogueAssetRevision persisted = await repository.SaveRevisionAsync(source, asset, revision);

            Assert.Equal(revision, persisted);
            Assert.Equal(source, await repository.GetSourceAsync(source.Id));
            Assert.Equal(source, await repository.FindSourceAsync(source.Kind, source.RootLocator));
            Assert.Equal(asset, await repository.GetAssetAsync(asset.Id));
            Assert.Equal(asset, await repository.FindAssetAsync(source.Id, asset.SourceKey));
            Assert.Equal(revision, await repository.GetRevisionAsync(revision.Id));
            Assert.Equal(revision, await repository.GetLatestRevisionAsync(asset.Id));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_revision_is_idempotent_for_the_same_asset_and_content_hash()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            (CatalogueSource source, CatalogueAsset asset, CatalogueAssetRevision first) = CreateRecords();
            CatalogueAssetRevision duplicate = new(
                AssetRevisionId.New(),
                asset.Id,
                first.ContentHash,
                first.SizeBytes + 100,
                first.ObservedAtUtc.AddMinutes(1),
                first.MediaType,
                first.Width,
                first.Height);

            CatalogueAssetRevision initiallyPersisted = await repository.SaveRevisionAsync(source, asset, first);
            CatalogueAssetRevision duplicateResult = await repository.SaveRevisionAsync(source, asset, duplicate);

            Assert.Equal(first, initiallyPersisted);
            Assert.Equal(first, duplicateResult);
            Assert.Null(await repository.GetRevisionAsync(duplicate.Id));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await CountAsync(connection, "asset_revisions"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_revision_updates_source_and_asset_and_preserves_revision_history()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            (CatalogueSource source, CatalogueAsset asset, CatalogueAssetRevision first) = CreateRecords();
            await repository.SaveRevisionAsync(source, asset, first);

            CatalogueSource updatedSource = new(
                source.Id,
                source.Kind,
                source.RootLocator + "-moved",
                source.CreatedAtUtc);
            CatalogueAsset updatedAsset = new(
                asset.Id,
                asset.SourceId,
                "renamed-photo.jpg",
                asset.CreatedAtUtc);
            CatalogueAssetRevision second = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('b', 64)),
                5678,
                first.ObservedAtUtc.AddMinutes(2),
                "image/jpeg",
                800,
                600);

            CatalogueAssetRevision persisted = await repository.SaveRevisionAsync(
                updatedSource,
                updatedAsset,
                second);

            Assert.Equal(second, persisted);
            Assert.Equal(updatedSource, await repository.GetSourceAsync(source.Id));
            Assert.Equal(updatedAsset, await repository.GetAssetAsync(asset.Id));
            Assert.Equal(first, await repository.GetRevisionAsync(first.Id));
            Assert.Equal(second, await repository.GetLatestRevisionAsync(asset.Id));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(2, await CountAsync(connection, "asset_revisions"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_revision_rejects_mismatched_record_relationships_before_writing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            (CatalogueSource source, CatalogueAsset asset, CatalogueAssetRevision revision) = CreateRecords();
            CatalogueAsset mismatchedAsset = new(
                asset.Id,
                SourceId.New(),
                asset.SourceKey,
                asset.CreatedAtUtc);

            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.SaveRevisionAsync(source, mismatchedAsset, revision));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(0, await CountAsync(connection, "sources"));
            Assert.Equal(0, await CountAsync(connection, "assets"));
            Assert.Equal(0, await CountAsync(connection, "asset_revisions"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static (CatalogueSource Source, CatalogueAsset Asset, CatalogueAssetRevision Revision) CreateRecords()
    {
        DateTimeOffset now = new(2026, 7, 25, 20, 30, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();

        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(Path.GetTempPath(), sourceId.ToString()),
            now);
        CatalogueAsset asset = new(assetId, sourceId, "photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string('a', 64)),
            1234,
            now,
            "image/jpeg",
            640,
            480);
        return (source, asset, revision);
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
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
