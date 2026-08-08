using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveReviewProxyRepositoryTests
{
    [Fact]
    public async Task Proxy_profile_and_completion_are_durable_separate_and_idempotent()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 8, 20, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            CatalogueAsset asset = new(AssetId.New(), source.Id, "1970/01/photo.jpg", now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                4_000_000,
                now,
                "image/jpeg",
                4000,
                3000);
            CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            SqliteArchiveReviewProxyRepository repository = new(database);
            ReviewProxyProfile profile = new("candidate-1600-q82", 1600, 82);
            await repository.RegisterProfileAsync(profile, now);

            IReadOnlyList<AssetRevisionId> pending = await repository.GetPendingCurrentRevisionIdsAsync(
                source.Id,
                profile.Id);
            Assert.Equal([persistedRevision.Id], pending);

            ArchiveReviewProxyRecord requested = new(
                persistedRevision.Id,
                profile.Id,
                180_000,
                new Sha256Digest(new string('b', 64)),
                1600,
                1200,
                now.AddMinutes(1),
                "review/candidate-1600-q82/aa/photo.jpg");
            ArchiveReviewProxyRecord first = await repository.RecordCompletionAsync(requested);
            ArchiveReviewProxyRecord replay = await repository.RecordCompletionAsync(
                new ArchiveReviewProxyRecord(
                    requested.AssetRevisionId,
                    requested.ProfileId,
                    requested.EncodedByteLength,
                    requested.ContentHash,
                    requested.Width,
                    requested.Height,
                    now.AddMinutes(5),
                    requested.RelativePath));

            Assert.Equal(first, replay);
            Assert.Equal(now.AddMinutes(1), replay.GeneratedAtUtc);
            Assert.Empty(await repository.GetPendingCurrentRevisionIdsAsync(source.Id, profile.Id));
            Assert.Equal(profile, await repository.GetProfileAsync(profile.Id));
            Assert.Equal(first, await repository.GetAsync(persistedRevision.Id, profile.Id));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.RecordCompletionAsync(
                    new ArchiveReviewProxyRecord(
                        requested.AssetRevisionId,
                        requested.ProfileId,
                        requested.EncodedByteLength + 1,
                        new Sha256Digest(new string('c', 64)),
                        requested.Width,
                        requested.Height,
                        now.AddMinutes(6),
                        requested.RelativePath)));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.RegisterProfileAsync(
                    new ReviewProxyProfile(profile.Id, 1600, 75),
                    now.AddMinutes(7)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
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
