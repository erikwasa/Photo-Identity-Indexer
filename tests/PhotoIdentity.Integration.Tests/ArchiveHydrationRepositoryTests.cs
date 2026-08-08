using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveHydrationRepositoryTests
{
    [Fact]
    public async Task Managed_hydration_ownership_is_durable_idempotent_and_reclaimable_after_release()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset started = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            AssetRevisionId revisionId = await CreateRevisionAsync(database, directory, started);
            SqliteArchiveHydrationRepository repository = new(database);

            ArchiveManagedHydrationRecord claimed = await repository.ClaimAsync(revisionId, started);
            Assert.True(claimed.IsActive);
            Assert.False(claimed.IsReleaseRequested);
            Assert.Equal(started, claimed.RequestedAtUtc);

            ArchiveManagedHydrationRecord replay = await repository.ClaimAsync(
                revisionId,
                started.AddMinutes(1));
            Assert.Equal(started, replay.RequestedAtUtc);

            ArchiveManagedHydrationRecord releasing = await repository.MarkReleaseRequestedAsync(
                revisionId,
                started.AddMinutes(2));
            Assert.True(releasing.IsActive);
            Assert.True(releasing.IsReleaseRequested);

            await repository.MarkReleasedAsync(revisionId, started.AddMinutes(3));
            ArchiveManagedHydrationRecord released = Assert.IsType<ArchiveManagedHydrationRecord>(
                await repository.GetAsync(revisionId));
            Assert.False(released.IsActive);
            Assert.Equal(started.AddMinutes(3), released.ReleasedAtUtc);

            ArchiveManagedHydrationRecord reclaimed = await repository.ClaimAsync(
                revisionId,
                started.AddMinutes(4));
            Assert.True(reclaimed.IsActive);
            Assert.False(reclaimed.IsReleaseRequested);
            Assert.Equal(started.AddMinutes(4), reclaimed.RequestedAtUtc);
            Assert.Null(reclaimed.ReleasedAtUtc);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Release_request_fails_closed_without_active_PhotoIdentity_ownership()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 9, 0, 10, 0, TimeSpan.Zero);
            AssetRevisionId revisionId = await CreateRevisionAsync(database, directory, now);
            SqliteArchiveHydrationRepository repository = new(database);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.MarkReleaseRequestedAsync(revisionId, now));
            Assert.Contains("not owned", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<AssetRevisionId> CreateRevisionAsync(
        SqliteCatalogueDatabase database,
        string directory,
        DateTimeOffset now)
    {
        CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, "photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(new string('a', 64)),
            123,
            now,
            "image/jpeg",
            10,
            10);
        return (await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(
            source,
            asset,
            revision)).Id;
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
