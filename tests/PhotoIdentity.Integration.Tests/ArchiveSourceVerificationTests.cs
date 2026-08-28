using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveSourceVerificationTests
{
    [Fact]
    public async Task Placeholder_metadata_can_require_verification_without_opening_content()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset t0 = new(2026, 8, 9, 7, 0, 0, TimeSpan.Zero);
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, t0);
            MutableAssetSource source = new(catalogueSource.Id, "1970/01/a.jpg", [1, 2, 3], t0, AssetAvailability.Local);
            SqliteArchiveSourceCatalogueScanner scanner = new(database);

            ArchiveSourceCatalogueScanSummary initial = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970/01", true),
                t0);
            Assert.Equal(1, initial.NewRevisionCount);
            Assert.Equal(1, initial.VerifiedSourceCount);
            Assert.Equal(1, source.OpenCount);

            source.Set([1, 2, 3], t0, AssetAvailability.OnlineOnly);
            ArchiveSourceCatalogueScanSummary unchangedPlaceholder = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970/01", true),
                t0.AddMinutes(1));
            Assert.Equal(1, unchangedPlaceholder.VerifiedSourceCount);
            Assert.Equal(0, unchangedPlaceholder.NeedsSourceVerificationCount);
            Assert.Equal(1, source.OpenCount);

            source.Set([1, 2, 3, 4], t0.AddMinutes(2), AssetAvailability.OnlineOnly);
            ArchiveSourceCatalogueScanSummary changedPlaceholder = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970/01", true),
                t0.AddMinutes(2));
            Assert.Equal(1, changedPlaceholder.NeedsSourceVerificationCount);
            Assert.Equal(1, source.OpenCount);

            // Metadata matching a prior baseline later does not clear a divergence without a hash.
            source.Set([1, 2, 3], t0, AssetAvailability.OnlineOnly);
            ArchiveSourceCatalogueScanSummary revertedMetadata = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970/01", true),
                t0.AddMinutes(3));
            Assert.Equal(1, revertedMetadata.NeedsSourceVerificationCount);
            Assert.Equal(1, source.OpenCount);

            source.Set([1, 2, 3], t0, AssetAvailability.Local);
            ArchiveSourceCatalogueScanSummary reverified = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970/01", true),
                t0.AddMinutes(4));
            Assert.Equal(1, reverified.VerifiedSourceCount);
            Assert.Equal(0, reverified.NewRevisionCount);
            Assert.Equal(2, source.OpenCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task First_time_online_only_source_is_hydrated_hashed_and_transferred_to_revision_ownership()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string relativePath = "2026/08/new.jpg";
            string fullPath = Path.Combine(directory, "2026", "08", "new.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            byte[] bytes = [10, 20, 30, 40, 50];
            await File.WriteAllBytesAsync(fullPath, bytes);
            DateTimeOffset t0 = new(2026, 8, 9, 7, 30, 0, TimeSpan.Zero);
            File.SetLastWriteTimeUtc(fullPath, t0.UtcDateTime);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, t0);
            MutableAssetSource source = new(catalogueSource.Id, relativePath, bytes, t0, AssetAvailability.OnlineOnly);
            ArchiveSourceCatalogueScanSummary scan = await new SqliteArchiveSourceCatalogueScanner(database).ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("2026/08", true),
                t0);
            Assert.Equal(1, scan.UnverifiedSourceCount);
            Assert.Equal(0, scan.NewRevisionCount);
            Assert.Equal(0, source.OpenCount);

            SqliteArchiveSourceObservationRepository observations = new(database);
            ArchiveSourceObservation pending = Assert.IsType<ArchiveSourceObservation>(
                await observations.GetNextPendingAsync(catalogueSource.Id));
            FakeFilesOnDemandPlatform platform = new();
            platform.Set(fullPath, AssetAvailability.OnlineOnly);
            SqliteArchiveSourceHydrationRepository sourceHydrations = new(database);
            ArchiveHydrationCapacityService capacity = new(
                database,
                new SqliteArchiveHydrationRepository(database),
                sourceHydrations,
                new SqliteArchiveStorageRepository(database),
                platform,
                new FixedStorageProbe(100_000),
                new ArchiveHydrationPolicyConfiguration(0, 1_000, 1),
                new ReviewProxyServingConfiguration(null, null),
                TimeProvider.System);
            ArchiveSourceVerificationService verification = new(
                observations,
                sourceHydrations,
                new SqliteArchiveAvailabilityRepository(database),
                capacity,
                platform,
                TimeProvider.System);

            ArchiveSourceVerificationAdvanceResult hydration = await verification.AdvanceAsync(catalogueSource.Id);
            Assert.True(hydration.HadPendingSource);
            Assert.True(hydration.WaitingForLocalContent);
            Assert.False(hydration.VerificationCompleted);
            Assert.Single(platform.HydrationRequests);
            Assert.True((await sourceHydrations.GetAsync(pending.AssetId))?.IsActive);

            platform.Set(fullPath, AssetAvailability.Unavailable);
            ArchiveSourceVerificationAdvanceResult transient = await verification.AdvanceAsync(catalogueSource.Id);
            Assert.True(transient.HadPendingSource);
            Assert.True(transient.WaitingForLocalContent);
            Assert.False(transient.VerificationCompleted);
            Assert.Single(platform.HydrationRequests);
            Assert.True((await sourceHydrations.GetAsync(pending.AssetId))?.IsActive);

            platform.Set(fullPath, AssetAvailability.Local);
            ArchiveSourceVerificationAdvanceResult verified = await verification.AdvanceAsync(catalogueSource.Id);
            Assert.True(verified.VerificationCompleted);
            Assert.NotNull(verified.RevisionId);
            Assert.Null(verified.PreviousRevisionId);
            Assert.False(verified.RevisionChanged);
            Assert.True(verified.NewRevision);
            Assert.True(verified.ManagedHydrationTransferred);

            ArchiveSourceObservation final = Assert.IsType<ArchiveSourceObservation>(
                await observations.GetAsync(pending.AssetId));
            Assert.Equal(ArchiveSourceVerificationState.Verified, final.VerificationState);
            Assert.Equal(verified.RevisionId, final.VerifiedRevisionId);
            Assert.False((await sourceHydrations.GetAsync(pending.AssetId))?.IsActive);
            Assert.True((await new SqliteArchiveHydrationRepository(database)
                .GetAsync(verified.RevisionId!.Value))?.IsActive);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Unavailable_source_without_managed_hydration_still_blocks()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string relativePath = "2026/08/missing.jpg";
            string fullPath = Path.Combine(directory, "2026", "08", "missing.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            byte[] bytes = [1, 2, 3, 4];
            DateTimeOffset now = new(2026, 8, 28, 18, 45, 0, TimeSpan.Zero);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, now);
            MutableAssetSource source = new(
                catalogueSource.Id,
                relativePath,
                bytes,
                now,
                AssetAvailability.OnlineOnly);
            _ = await new SqliteArchiveSourceCatalogueScanner(database).ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("2026/08", true),
                now);

            FakeFilesOnDemandPlatform platform = new();
            platform.Set(fullPath, AssetAvailability.Unavailable);
            SqliteArchiveSourceHydrationRepository sourceHydrations = new(database);
            ArchiveHydrationCapacityService capacity = new(
                database,
                new SqliteArchiveHydrationRepository(database),
                sourceHydrations,
                new SqliteArchiveStorageRepository(database),
                platform,
                new FixedStorageProbe(100_000),
                new ArchiveHydrationPolicyConfiguration(0, 1_000, 1),
                new ReviewProxyServingConfiguration(null, null),
                TimeProvider.System);
            ArchiveSourceVerificationService verification = new(
                new SqliteArchiveSourceObservationRepository(database),
                sourceHydrations,
                new SqliteArchiveAvailabilityRepository(database),
                capacity,
                platform,
                TimeProvider.System);

            FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => verification.AdvanceAsync(catalogueSource.Id));
            Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task First_time_source_hydration_obeys_same_managed_byte_budget()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            DateTimeOffset now = new(2026, 8, 9, 8, 0, 0, TimeSpan.Zero);
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, now);
            MutableAssetSource source = new(
                catalogueSource.Id,
                "photo.jpg",
                Enumerable.Repeat((byte)1, 600).ToArray(),
                now,
                AssetAvailability.OnlineOnly);
            _ = await new SqliteArchiveSourceCatalogueScanner(database).ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                now);
            ArchiveSourceObservation pending = Assert.IsType<ArchiveSourceObservation>(
                await new SqliteArchiveSourceObservationRepository(database).GetNextPendingAsync(catalogueSource.Id));

            FakeFilesOnDemandPlatform platform = new();
            ArchiveHydrationCapacityService capacity = new(
                database,
                new SqliteArchiveHydrationRepository(database),
                new SqliteArchiveSourceHydrationRepository(database),
                new SqliteArchiveStorageRepository(database),
                platform,
                new FixedStorageProbe(100_000),
                new ArchiveHydrationPolicyConfiguration(0, 500, 1),
                new ReviewProxyServingConfiguration(null, null),
                TimeProvider.System);
            int accepted = 0;
            ArchiveHydrationAdmission admission = await capacity.ExecuteSourceHydrationAdmissionAsync(
                pending,
                () => { accepted++; return Task.CompletedTask; });

            Assert.False(admission.Allowed);
            Assert.Contains("managed byte budget", admission.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, accepted);
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

    private sealed class MutableAssetSource : IAssetSource
    {
        private readonly SourceId _sourceId;
        private readonly string _relativePath;
        private byte[] _bytes;
        private DateTimeOffset _lastWrite;
        private AssetAvailability _availability;

        public MutableAssetSource(
            SourceId sourceId,
            string relativePath,
            byte[] bytes,
            DateTimeOffset lastWrite,
            AssetAvailability availability)
        {
            _sourceId = sourceId;
            _relativePath = relativePath;
            _bytes = bytes;
            _lastWrite = lastWrite;
            _availability = availability;
        }

        public int OpenCount { get; private set; }

        public void Set(byte[] bytes, DateTimeOffset lastWrite, AssetAvailability availability)
        {
            _bytes = bytes;
            _lastWrite = lastWrite;
            _availability = availability;
        }

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceAsset(
                new SourceAssetReference(_sourceId, _relativePath),
                _relativePath,
                "image/jpeg",
                _bytes.LongLength,
                _lastWrite,
                _availability);
            await Task.CompletedTask;
        }

        public Task<AssetAvailability> GetAvailabilityAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken) => Task.FromResult(_availability);

        public Task<Stream> OpenContentAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            return Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
        }
    }

    private sealed class FakeFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        private readonly Dictionary<string, OneDriveFilesOnDemandState> _states = new(StringComparer.OrdinalIgnoreCase);
        public List<string> HydrationRequests { get; } = [];

        public void Set(string path, AssetAvailability availability) =>
            _states[Path.GetFullPath(path)] = new OneDriveFilesOnDemandState(
                availability,
                availability == AssetAvailability.Downloading,
                availability == AssetAvailability.OnlineOnly);

        public OneDriveFilesOnDemandState GetState(string path) =>
            _states.TryGetValue(Path.GetFullPath(path), out OneDriveFilesOnDemandState? state)
                ? state
                : new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            HydrationRequests.Add(fullPath);
            Set(fullPath, AssetAvailability.Downloading);
            return Task.CompletedTask;
        }

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Set(path, AssetAvailability.OnlineOnly);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedStorageProbe(long availableBytes) : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
    }
}
