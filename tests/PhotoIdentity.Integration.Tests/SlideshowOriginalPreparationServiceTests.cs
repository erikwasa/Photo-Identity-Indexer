using System.Security.Cryptography;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowOriginalPreparationServiceTests
{
    [Fact]
    public async Task Zero_photo_snapshot_prepares_immediately_without_hydration()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            FakeFilesOnDemandPlatform platform = new();
            TestServices services = CreateServices(
                database,
                platform,
                new ArchiveHydrationPolicyConfiguration(0, 10_000, 2));

            SlideshowOriginalPreparationSnapshot started =
                await services.Preparation.StartAsync([]);
            SlideshowOriginalPreparationSnapshot ready = await WaitForStateAsync(
                services.Preparation,
                started.SessionId,
                SlideshowOriginalPreparationStates.Ready);

            Assert.Equal(0, ready.Ready);
            Assert.Equal(0, ready.Total);
            Assert.Empty(platform.HydrationRequests);

            services.Preparation.End(started.SessionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Mixed_local_and_online_snapshot_prepares_without_claiming_preexisting_local_content()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 29, 20, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);

            CatalogueProcessingAssetRevision local = await SaveRevisionAndFileAsync(
                database, source, "family/local.jpg", CreateBytes(96, 1), now);
            CatalogueProcessingAssetRevision online = await SaveRevisionAndFileAsync(
                database, source, "family/online.jpg", CreateBytes(128, 2), now.AddMinutes(1));

            FakeFilesOnDemandPlatform platform = new();
            platform.SetState(local, AssetAvailability.Local);
            platform.SetState(online, AssetAvailability.OnlineOnly);

            TestServices services = CreateServices(
                database,
                platform,
                new ArchiveHydrationPolicyConfiguration(0, 10_000, 2));

            SlideshowOriginalPreparationSnapshot started = await services.Preparation.StartAsync(
                [local.RevisionId, online.RevisionId]);
            SlideshowOriginalPreparationSnapshot ready = await WaitForStateAsync(
                services.Preparation,
                started.SessionId,
                SlideshowOriginalPreparationStates.Ready);

            Assert.Equal(2, ready.Ready);
            Assert.Equal(2, ready.Total);
            Assert.Single(platform.HydrationRequests);
            Assert.EndsWith(
                "online.jpg",
                platform.HydrationRequests[0],
                StringComparison.OrdinalIgnoreCase);

            ArchiveManagedHydrationRecord? localOwnership =
                await services.Hydrations.GetAsync(local.RevisionId);
            ArchiveManagedHydrationRecord? onlineOwnership =
                await services.Hydrations.GetAsync(online.RevisionId);
            Assert.Null(localOwnership);
            Assert.True(onlineOwnership?.IsActive);
            Assert.True(services.Leases.Contains(started.SessionId));

            VerifiedCollectionOriginal? prepared =
                await services.Preparation.OpenPreparedOriginalAsync(
                    started.SessionId,
                    online.RevisionId);
            Assert.NotNull(prepared);
            await prepared!.Stream.DisposeAsync();

            Assert.True(services.Preparation.End(started.SessionId));
            Assert.False(services.Leases.Contains(started.SessionId));

            onlineOwnership = await services.Hydrations.GetAsync(online.RevisionId);
            Assert.True(onlineOwnership?.IsActive);
            Assert.Empty(platform.ReleaseRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Full_snapshot_admission_failure_happens_before_any_hydration_request()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 29, 20, 30, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);

            CatalogueProcessingAssetRevision online = await SaveRevisionAndFileAsync(
                database,
                source,
                "private/too-large.jpg",
                CreateBytes(600, 3),
                now);

            FakeFilesOnDemandPlatform platform = new();
            platform.SetState(online, AssetAvailability.OnlineOnly);
            TestServices services = CreateServices(
                database,
                platform,
                new ArchiveHydrationPolicyConfiguration(0, 500, 2));

            SlideshowOriginalPreparationSnapshot started = await services.Preparation.StartAsync(
                [online.RevisionId]);
            SlideshowOriginalPreparationSnapshot failed = await WaitForStateAsync(
                services.Preparation,
                started.SessionId,
                SlideshowOriginalPreparationStates.Failed);

            Assert.Empty(platform.HydrationRequests);
            Assert.Equal(600, failed.RequiredAdditionalBytes);
            Assert.Equal(500, failed.AvailableManagedCapacity);
            Assert.DoesNotContain("too-large.jpg", failed.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(directory, failed.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.True(failed.CanContinueWithAvailable);

            services.Preparation.End(started.SessionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Immutable_verification_failure_blocks_best_quality_ready_state()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 29, 21, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);

            CatalogueProcessingAssetRevision revision = await SaveRevisionAndFileAsync(
                database,
                source,
                "private/hash-mismatch.jpg",
                CreateBytes(128, 4),
                now);
            await File.WriteAllBytesAsync(
                ResolvePath(revision),
                CreateBytes(128, 9));

            FakeFilesOnDemandPlatform platform = new();
            platform.SetState(revision, AssetAvailability.Local);
            TestServices services = CreateServices(
                database,
                platform,
                new ArchiveHydrationPolicyConfiguration(0, 10_000, 2));

            SlideshowOriginalPreparationSnapshot started = await services.Preparation.StartAsync(
                [revision.RevisionId]);
            SlideshowOriginalPreparationSnapshot failed = await WaitForStateAsync(
                services.Preparation,
                started.SessionId,
                SlideshowOriginalPreparationStates.Failed);

            Assert.Equal(0, failed.Ready);
            Assert.Empty(platform.HydrationRequests);
            Assert.Contains(
                "immutable catalogue revision",
                failed.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "hash-mismatch.jpg",
                failed.Message ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            services.Preparation.End(started.SessionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<SlideshowOriginalPreparationSnapshot> WaitForStateAsync(
        SlideshowOriginalPreparationService service,
        Guid sessionId,
        string expected)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            SlideshowOriginalPreparationSnapshot snapshot = service.GetStatus(sessionId)
                ?? throw new InvalidOperationException("Preparation session disappeared unexpectedly.");
            if (snapshot.State == expected)
            {
                return snapshot;
            }

            if (snapshot.State is SlideshowOriginalPreparationStates.Failed or
                SlideshowOriginalPreparationStates.Cancelled)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected preparation state '{expected}' but observed '{snapshot.State}': {snapshot.Message}");
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    private static TestServices CreateServices(
        SqliteCatalogueDatabase database,
        FakeFilesOnDemandPlatform platform,
        ArchiveHydrationPolicyConfiguration policy)
    {
        SqliteArchiveHydrationRepository hydrations = new(database);
        SlideshowOriginalLeaseRegistry leases = new(TimeProvider.System);
        ArchiveHydrationCapacityService capacity = new(
            database,
            hydrations,
            new SqliteArchiveSourceHydrationRepository(database),
            new SqliteArchiveStorageRepository(database),
            platform,
            new FixedStorageProbe(100_000),
            policy,
            new ReviewProxyServingConfiguration(null, null),
            TimeProvider.System,
            leases);
        CollectionOriginalAccessService originals = new(
            new SqliteLocalBatchRepository(database),
            hydrations,
            new SqliteArchiveAvailabilityRepository(database),
            platform,
            capacity,
            TimeProvider.System);
        SlideshowOriginalPreparationService preparation = new(
            new SqliteLocalBatchRepository(database),
            originals,
            capacity,
            policy,
            leases,
            TimeProvider.System);

        return new TestServices(
            preparation,
            hydrations,
            leases);
    }

    private static async Task<CatalogueProcessingAssetRevision> SaveRevisionAndFileAsync(
        SqliteCatalogueDatabase database,
        CatalogueSource source,
        string sourceKey,
        byte[] content,
        DateTimeOffset observedAtUtc)
    {
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

        CatalogueProcessingAssetRevision resolved =
            await new SqliteLocalBatchRepository(database).GetAssetRevisionAsync(saved.Id)
            ?? throw new InvalidOperationException("Saved revision was unavailable.");

        string path = ResolvePath(resolved);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content);
        return resolved;
    }

    private static byte[] CreateBytes(int count, byte seed) =>
        Enumerable.Range(0, count)
            .Select(index => (byte)((index + seed) % 251))
            .ToArray();

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

    private sealed record TestServices(
        SlideshowOriginalPreparationService Preparation,
        SqliteArchiveHydrationRepository Hydrations,
        SlideshowOriginalLeaseRegistry Leases);

    private sealed class FixedStorageProbe(long availableBytes) : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
    }

    private sealed class FakeFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        private readonly Dictionary<string, OneDriveFilesOnDemandState> _states =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> HydrationRequests { get; } = [];
        public List<string> ReleaseRequests { get; } = [];

        public void SetState(
            CatalogueProcessingAssetRevision revision,
            AssetAvailability availability)
        {
            _states[ResolvePath(revision)] = State(availability);
        }

        public OneDriveFilesOnDemandState GetState(string path) =>
            _states.TryGetValue(
                Path.GetFullPath(path),
                out OneDriveFilesOnDemandState? state)
                ? state
                : State(AssetAvailability.OnlineOnly);

        public Task RequestHydrationAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            HydrationRequests.Add(fullPath);
            _states[fullPath] = new OneDriveFilesOnDemandState(
                AssetAvailability.Local,
                IsPinned: true,
                IsUnpinned: false);
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            ReleaseRequests.Add(fullPath);
            _states[fullPath] = State(AssetAvailability.OnlineOnly);
            return Task.CompletedTask;
        }

        private static OneDriveFilesOnDemandState State(AssetAvailability availability) =>
            new(
                availability,
                IsPinned: availability == AssetAvailability.Downloading,
                IsUnpinned: availability == AssetAvailability.OnlineOnly);
    }
}
