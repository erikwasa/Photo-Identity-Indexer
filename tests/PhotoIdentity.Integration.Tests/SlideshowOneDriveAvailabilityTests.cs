using System.Security.Cryptography;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowOneDriveAvailabilityTests
{
    [Fact]
    public async Task Local_originals_prepare_normally_when_OneDrive_client_is_unavailable()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            PostgresTestCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            CatalogueProcessingAssetRevision local = await SaveRevisionAndFileAsync(
                database,
                source,
                "family/local.jpg",
                CreateBytes(96, 1),
                now);

            AvailabilityPlatform platform = new()
            {
                ClientAvailability = OneDriveSyncClientAvailability.Unavailable,
            };
            platform.SetState(local, AssetAvailability.Local);
            SlideshowOriginalPreparationService preparation = CreatePreparation(database, platform);

            SlideshowOriginalPreparationSnapshot started =
                await preparation.StartAsync([local.RevisionId]);
            SlideshowOriginalPreparationSnapshot ready = await WaitForSnapshotAsync(
                preparation,
                started.SessionId,
                snapshot => snapshot.State == SlideshowOriginalPreparationStates.Ready);

            Assert.Equal(1, ready.Ready);
            Assert.Empty(platform.HydrationRequests);
            await preparation.EndAsync(started.SessionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Online_only_original_waits_for_actionable_retry_when_OneDrive_client_is_unavailable()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            PostgresTestCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 9, 29, 8, 30, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            CatalogueProcessingAssetRevision online = await SaveRevisionAndFileAsync(
                database,
                source,
                "family/online.jpg",
                CreateBytes(128, 2),
                now);

            AvailabilityPlatform platform = new()
            {
                ClientAvailability = OneDriveSyncClientAvailability.Unavailable,
                CompleteHydrationImmediately = true,
            };
            platform.SetState(online, AssetAvailability.OnlineOnly);
            SlideshowOriginalPreparationService preparation = CreatePreparation(database, platform);

            SlideshowOriginalPreparationSnapshot started =
                await preparation.StartAsync([online.RevisionId]);
            SlideshowOriginalPreparationSnapshot unavailable = await WaitForSnapshotAsync(
                preparation,
                started.SessionId,
                snapshot => snapshot.Phase == "onedrive-unavailable" && snapshot.CanRetry);

            Assert.Equal(SlideshowOriginalPreparationStates.Preparing, unavailable.State);
            Assert.Equal(started.SessionId, unavailable.SessionId);
            Assert.Contains("Start OneDrive", unavailable.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(platform.HydrationRequests);

            platform.ClientAvailability = OneDriveSyncClientAvailability.Available;
            SlideshowOriginalPreparationSnapshot retried =
                preparation.Retry(started.SessionId)
                ?? throw new InvalidOperationException("Retry lost the preparation session.");
            Assert.Equal(started.SessionId, retried.SessionId);

            SlideshowOriginalPreparationSnapshot ready = await WaitForSnapshotAsync(
                preparation,
                started.SessionId,
                snapshot => snapshot.State == SlideshowOriginalPreparationStates.Ready);

            Assert.Equal(1, ready.Ready);
            Assert.Single(platform.HydrationRequests);
            await preparation.EndAsync(started.SessionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Managed_downloading_original_detects_stopped_client_and_retry_reasserts_pin()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            PostgresTestCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            CatalogueProcessingAssetRevision online = await SaveRevisionAndFileAsync(
                database,
                source,
                "family/stalled.jpg",
                CreateBytes(128, 3),
                now);

            AvailabilityPlatform platform = new()
            {
                ClientAvailability = OneDriveSyncClientAvailability.Available,
                CompleteHydrationImmediately = false,
            };
            platform.SetState(online, AssetAvailability.OnlineOnly);
            SlideshowOriginalPreparationService preparation = CreatePreparation(database, platform);

            SlideshowOriginalPreparationSnapshot started =
                await preparation.StartAsync([online.RevisionId]);
            await WaitForSnapshotAsync(
                preparation,
                started.SessionId,
                snapshot => snapshot.Downloading == 1 && snapshot.HydrationRequests == 1);

            platform.ClientAvailability = OneDriveSyncClientAvailability.Unavailable;
            SlideshowOriginalPreparationSnapshot unavailable = await WaitForSnapshotAsync(
                preparation,
                started.SessionId,
                snapshot => snapshot.Phase == "onedrive-unavailable" && snapshot.CanRetry);

            Assert.Equal(started.SessionId, unavailable.SessionId);
            Assert.Single(platform.HydrationRequests);

            platform.ClientAvailability = OneDriveSyncClientAvailability.Available;
            platform.CompleteHydrationImmediately = true;
            _ = preparation.Retry(started.SessionId);

            SlideshowOriginalPreparationSnapshot ready = await WaitForSnapshotAsync(
                preparation,
                started.SessionId,
                snapshot => snapshot.State == SlideshowOriginalPreparationStates.Ready);

            Assert.Equal(2, platform.HydrationRequests.Count);
            Assert.Equal(2, ready.HydrationRequests);
            await preparation.EndAsync(started.SessionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static SlideshowOriginalPreparationService CreatePreparation(
        PostgresTestCatalogueDatabase database,
        AvailabilityPlatform platform)
    {
        TimeProvider time = TimeProvider.System;
        PostgresArchiveHydrationRepository hydrations = new(database);
        SlideshowOriginalLeaseRegistry leases = new(time);
        ArchiveHydrationPolicyConfiguration policy = new(0, 10_000, 1);
        ArchiveHydrationCapacityService capacity = new(
            hydrations,
            new PostgresArchiveSourceHydrationRepository(database),
            new PostgresArchiveCoverageRepository(database),
            new PostgresArchiveStorageRepository(database),
            new PostgresArchiveAvailabilityRepository(database),
            platform,
            new FixedStorageProbe(100_000),
            policy,
            new ReviewProxyServingConfiguration(null, null),
            time,
            leases);
        CollectionOriginalAccessService originals = new(
            new PostgresLocalBatchRepository(database),
            hydrations,
            new PostgresArchiveAvailabilityRepository(database),
            platform,
            capacity,
            time);
        return new SlideshowOriginalPreparationService(
            new PostgresLocalBatchRepository(database),
            originals,
            capacity,
            policy,
            leases,
            time);
    }

    private static async Task<SlideshowOriginalPreparationSnapshot> WaitForSnapshotAsync(
        SlideshowOriginalPreparationService service,
        Guid sessionId,
        Func<SlideshowOriginalPreparationSnapshot, bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            SlideshowOriginalPreparationSnapshot snapshot = service.GetStatus(sessionId)
                ?? throw new InvalidOperationException("Preparation session disappeared unexpectedly.");
            if (predicate(snapshot))
            {
                return snapshot;
            }

            if (snapshot.State is SlideshowOriginalPreparationStates.Failed or
                SlideshowOriginalPreparationStates.Cancelled)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Preparation became '{snapshot.State}' before expected recovery: {snapshot.Message}");
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    private static async Task<CatalogueProcessingAssetRevision> SaveRevisionAndFileAsync(
        PostgresTestCatalogueDatabase database,
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
        CatalogueAssetRevision saved = await new PostgresAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);
        CatalogueProcessingAssetRevision resolved =
            await new PostgresLocalBatchRepository(database).GetAssetRevisionAsync(saved.Id)
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

    private sealed class FixedStorageProbe(long availableBytes) : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
    }

    private sealed class AvailabilityPlatform : IOneDriveFilesOnDemandPlatform
    {
        private readonly Dictionary<string, OneDriveFilesOnDemandState> _states =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> HydrationRequests { get; } = [];
        public bool CompleteHydrationImmediately { get; set; }
        public OneDriveSyncClientAvailability ClientAvailability { get; set; } =
            OneDriveSyncClientAvailability.Unknown;

        public void SetState(
            CatalogueProcessingAssetRevision revision,
            AssetAvailability availability)
        {
            _states[ResolvePath(revision)] = State(availability);
        }

        public OneDriveFilesOnDemandState GetState(string path) =>
            _states.TryGetValue(Path.GetFullPath(path), out OneDriveFilesOnDemandState? state)
                ? state
                : State(AssetAvailability.OnlineOnly);

        public OneDriveSyncClientAvailability GetSyncClientAvailability() => ClientAvailability;

        public Task RequestHydrationAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            HydrationRequests.Add(fullPath);
            _states[fullPath] = new OneDriveFilesOnDemandState(
                CompleteHydrationImmediately
                    ? AssetAvailability.Local
                    : AssetAvailability.Downloading,
                IsPinned: true,
                IsUnpinned: false);
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _states[Path.GetFullPath(path)] = State(AssetAvailability.OnlineOnly);
            return Task.CompletedTask;
        }

        private static OneDriveFilesOnDemandState State(AssetAvailability availability) =>
            new(
                availability,
                IsPinned: availability == AssetAvailability.Downloading,
                IsUnpinned: availability == AssetAvailability.OnlineOnly);
    }
}
