using System.Security.Cryptography;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveHydrationCapacityServiceTests
{
    [Fact]
    public async Task Managed_hydration_is_disabled_until_all_limits_are_configured()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueProcessingAssetRevision revision = CreateDetachedRevision(directory, 400);
            FakeFilesOnDemandPlatform platform = new();
            ArchiveHydrationCapacityService service = CreateService(
                database,
                platform,
                new FixedStorageProbe(10_000),
                new ArchiveHydrationPolicyConfiguration(null, null, null));
            int accepted = 0;

            ArchiveHydrationAdmission result = await service.ExecuteHydrationAdmissionAsync(
                revision,
                () => { accepted++; return Task.CompletedTask; });

            Assert.False(result.Allowed);
            Assert.Contains("disabled", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, accepted);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Free_space_reserve_blocks_hydration_before_the_request_is_issued()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueProcessingAssetRevision revision = CreateDetachedRevision(directory, 600);
            ArchiveHydrationCapacityService service = CreateService(
                database,
                new FakeFilesOnDemandPlatform(),
                new FixedStorageProbe(1_000),
                new ArchiveHydrationPolicyConfiguration(500, 10_000, 2));
            int accepted = 0;

            ArchiveHydrationAdmission result = await service.ExecuteHydrationAdmissionAsync(
                revision,
                () => { accepted++; return Task.CompletedTask; });

            Assert.False(result.Allowed);
            Assert.Contains("free-space reserve", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, result.EvictionBytesRequested);
            Assert.Equal(0, accepted);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Concurrency_limit_counts_managed_downloading_originals()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            CatalogueProcessingAssetRevision active = await SaveRevisionAsync(database, source, "active.jpg", 300, now);
            CatalogueProcessingAssetRevision requested = await SaveRevisionAsync(database, source, "requested.jpg", 300, now.AddMinutes(1));
            SqliteArchiveHydrationRepository hydrations = new(database);
            await hydrations.ClaimAsync(active.RevisionId, now);

            FakeFilesOnDemandPlatform platform = new();
            platform.SetState(active, AssetAvailability.Downloading);
            ArchiveHydrationCapacityService service = CreateService(
                database,
                platform,
                new FixedStorageProbe(10_000),
                new ArchiveHydrationPolicyConfiguration(0, 10_000, 1));
            int accepted = 0;

            ArchiveHydrationAdmission result = await service.ExecuteHydrationAdmissionAsync(
                requested,
                () => { accepted++; return Task.CompletedTask; });

            Assert.False(result.Allowed);
            Assert.Contains("concurrency limit", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, accepted);
            Assert.Empty(platform.ReleaseRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Budget_pressure_requests_release_of_least_recently_needed_managed_original_first()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset t0 = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, t0);
            CatalogueProcessingAssetRevision oldest = await SaveRevisionAsync(database, source, "oldest.jpg", 400, t0);
            CatalogueProcessingAssetRevision recent = await SaveRevisionAsync(database, source, "recent.jpg", 400, t0.AddMinutes(1));
            CatalogueProcessingAssetRevision requested = await SaveRevisionAsync(database, source, "requested.jpg", 400, t0.AddMinutes(2));

            SqliteArchiveHydrationRepository hydrations = new(database);
            await hydrations.ClaimAsync(oldest.RevisionId, t0);
            await hydrations.ClaimAsync(recent.RevisionId, t0.AddMinutes(1));
            await hydrations.TouchAsync(recent.RevisionId, t0.AddHours(1));

            FakeFilesOnDemandPlatform platform = new();
            platform.SetState(oldest, AssetAvailability.Local);
            platform.SetState(recent, AssetAvailability.Local);
            ArchiveHydrationCapacityService service = CreateService(
                database,
                platform,
                new FixedStorageProbe(10_000),
                new ArchiveHydrationPolicyConfiguration(0, 900, 2));
            int accepted = 0;

            ArchiveHydrationAdmission result = await service.ExecuteHydrationAdmissionAsync(
                requested,
                () => { accepted++; return Task.CompletedTask; });

            Assert.False(result.Allowed);
            Assert.Contains("managed byte budget", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(400, result.EvictionBytesRequested);
            Assert.Equal(0, accepted);
            Assert.Single(platform.ReleaseRequests);
            Assert.EndsWith("oldest.jpg", platform.ReleaseRequests[0], StringComparison.OrdinalIgnoreCase);

            ArchiveManagedHydrationRecord? oldestRecord = await hydrations.GetAsync(oldest.RevisionId);
            ArchiveManagedHydrationRecord? recentRecord = await hydrations.GetAsync(recent.RevisionId);
            Assert.True(oldestRecord?.IsReleaseRequested);
            Assert.False(recentRecord?.IsReleaseRequested);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static ArchiveHydrationCapacityService CreateService(
        SqliteCatalogueDatabase database,
        FakeFilesOnDemandPlatform platform,
        IArchiveStorageProbe probe,
        ArchiveHydrationPolicyConfiguration configuration) =>
        new(
            database,
            new SqliteArchiveHydrationRepository(database),
            new SqliteArchiveStorageRepository(database),
            platform,
            probe,
            configuration,
            new ReviewProxyServingConfiguration(null, null),
            TimeProvider.System);

    private static CatalogueProcessingAssetRevision CreateDetachedRevision(string root, long sizeBytes) =>
        new(
            AssetRevisionId.New(),
            AssetId.New(),
            SourceId.New(),
            "local-folder",
            root,
            "detached.jpg",
            new Sha256Digest(new string('0', 64)),
            sizeBytes,
            "image/jpeg");

    private static async Task<CatalogueProcessingAssetRevision> SaveRevisionAsync(
        SqliteCatalogueDatabase database,
        CatalogueSource source,
        string sourceKey,
        int sizeBytes,
        DateTimeOffset observedAtUtc)
    {
        byte[] content = Enumerable.Range(0, sizeBytes).Select(index => (byte)(index % 251)).ToArray();
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
        CatalogueAssetRevision saved = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);
        return await new SqliteLocalBatchRepository(database).GetAssetRevisionAsync(saved.Id)
            ?? throw new InvalidOperationException("Saved revision was unavailable.");
    }

    private static string ResolvePath(CatalogueProcessingAssetRevision revision) =>
        Path.GetFullPath(Path.Combine(
            revision.RootLocator,
            revision.SourceKey.Replace('/', Path.DirectorySeparatorChar)));

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

    private sealed class FixedStorageProbe(long availableBytes) : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
    }

    private sealed class FakeFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        private readonly Dictionary<string, OneDriveFilesOnDemandState> _states = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ReleaseRequests { get; } = [];

        public void SetState(CatalogueProcessingAssetRevision revision, AssetAvailability availability)
        {
            string path = ResolvePath(revision);
            _states[path] = new OneDriveFilesOnDemandState(
                availability,
                availability == AssetAvailability.Downloading,
                availability == AssetAvailability.OnlineOnly);
        }

        public OneDriveFilesOnDemandState GetState(string path) =>
            _states.TryGetValue(Path.GetFullPath(path), out OneDriveFilesOnDemandState? state)
                ? state
                : new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _states[Path.GetFullPath(path)] = new OneDriveFilesOnDemandState(AssetAvailability.Downloading, true, false);
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            ReleaseRequests.Add(fullPath);
            // Keep the file local here: release is asynchronous, and capacity must continue to
            // count it until a later observation reports online-only.
            _states[fullPath] = new OneDriveFilesOnDemandState(AssetAvailability.Local, false, true);
            return Task.CompletedTask;
        }
    }
}
