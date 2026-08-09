using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveAnalysisHydratableSelectionTests
{
    [Fact]
    public async Task Bounded_selection_adds_online_only_and_downloading_revisions_without_changing_local_only_default()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);

            CatalogueAssetRevision local = await SaveRevisionAsync(database, source, "local.jpg", now);
            CatalogueAssetRevision online = await SaveRevisionAsync(database, source, "online.jpg", now.AddMinutes(1));
            CatalogueAssetRevision downloading = await SaveRevisionAsync(database, source, "downloading.jpg", now.AddMinutes(2));
            SqliteArchiveAvailabilityRepository availability = new(database);
            await availability.RecordAsync(local.AssetId, AssetAvailability.Local, now);
            await availability.RecordAsync(online.AssetId, AssetAvailability.OnlineOnly, now);
            await availability.RecordAsync(downloading.AssetId, AssetAvailability.Downloading, now);

            SqliteArchiveAnalysisRepository repository = new(database);
            Sha256Digest profile = new(new string('a', 64));

            IReadOnlyList<AssetRevisionId> localOnly = await repository.GetPendingCurrentRevisionIdsAsync(
                source.Id,
                profile);
            IReadOnlyList<AssetRevisionId> hydratable = await repository.GetPendingCurrentRevisionIdsAsync(
                source.Id,
                profile,
                includeHydratable: true);

            Assert.Equal([local.Id], localOnly);
            Assert.Equal(3, hydratable.Count);
            Assert.Contains(local.Id, hydratable);
            Assert.Contains(online.Id, hydratable);
            Assert.Contains(downloading.Id, hydratable);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<CatalogueAssetRevision> SaveRevisionAsync(
        SqliteCatalogueDatabase database,
        CatalogueSource source,
        string sourceKey,
        DateTimeOffset observedAtUtc)
    {
        byte[] content = System.Text.Encoding.UTF8.GetBytes(sourceKey);
        CatalogueAsset asset = new(AssetId.New(), source.Id, sourceKey, observedAtUtc);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()),
            content.LongLength,
            observedAtUtc,
            "image/jpeg",
            100,
            100);
        return await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(source, asset, revision);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PhotoIdentity.Integration.Tests", Guid.NewGuid().ToString("N"));
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
