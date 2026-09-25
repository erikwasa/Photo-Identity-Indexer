using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class SourceCopyExclusionMediaAccessTests
{
    [Fact]
    public async Task Captured_revision_identifier_stops_resolving_review_proxy_immediately_after_exclusion()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-exclusion-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string proxyRoot = Path.Combine(directory, "proxies");
            Directory.CreateDirectory(proxyRoot);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            CatalogueAsset asset = new(AssetId.New(), source.Id, "private/photo.jpg", now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                100,
                now,
                "image/jpeg",
                100,
                100);
            CatalogueAssetRevision persisted = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            SqliteArchiveReviewProxyRepository proxies = new(database);
            ReviewProxyProfile profile = new("wi-0089-test", 1600, 82);
            await proxies.RegisterProfileAsync(profile, now);
            string relativePath = "review/wi-0089-test/proxy.jpg";
            string physicalPath = Path.Combine(proxyRoot, "review", "wi-0089-test", "proxy.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            byte[] bytes = [1, 2, 3, 4, 5];
            await File.WriteAllBytesAsync(physicalPath, bytes);
            await proxies.RecordCompletionAsync(new ArchiveReviewProxyRecord(
                persisted.Id,
                profile.Id,
                bytes.LongLength,
                new Sha256Digest(new string('b', 64)),
                100,
                100,
                now,
                relativePath));

            SqliteSourceCopyExclusionRepository exclusions = new(database);
            CollectionReviewProxyFileResolver resolver = new(
                proxies,
                derivatives: null,
                exclusions,
                new ReviewProxyServingConfiguration(proxyRoot, profile.Id));

            AssetRevisionId capturedBeforeExclusion = persisted.Id;
            Assert.NotNull(await resolver.ResolveAsync(capturedBeforeExclusion));

            await exclusions.ExcludeAsync(source.Id, asset.SourceKey, now.AddMinutes(1));

            Assert.Null(await resolver.ResolveAsync(capturedBeforeExclusion));
            Assert.True(File.Exists(physicalPath)); // WI-0090 owns deletion; WI-0089 owns immediate denial.
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
