using System.Security.Cryptography;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class BoundedHydrationWorkingSetTests
{
    [Fact]
    public async Task Sequential_managed_processing_keeps_peak_hydration_below_budget_when_logical_total_exceeds_it()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PhotoIdentity.Integration.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);
            CatalogueProcessingAssetRevision[] revisions = new CatalogueProcessingAssetRevision[3];
            for (int index = 0; index < revisions.Length; index++)
            {
                revisions[index] = await SaveRevisionAsync(database, source, $"photo-{index}.jpg", 400, now.AddMinutes(index));
            }

            const long managedBudget = 500;
            SqliteArchiveHydrationRepository hydrations = new(database);
            ImmediateFilesOnDemandPlatform platform = new();
            ArchiveHydrationCapacityService capacity = new(
                database,
                hydrations,
                new SqliteArchiveSourceHydrationRepository(database),
                new SqliteArchiveStorageRepository(database),
                platform,
                new FixedStorageProbe(100_000),
                new ArchiveHydrationPolicyConfiguration(0, managedBudget, 1),
                new ReviewProxyServingConfiguration(null, null),
                TimeProvider.System);

            long peakReservedBytes = 0;
            long cumulativeLogicalBytes = 0;
            foreach (CatalogueProcessingAssetRevision revision in revisions)
            {
                ArchiveHydrationAdmission admission = await capacity.ExecuteHydrationAdmissionAsync(
                    revision,
                    async () =>
                    {
                        await platform.RequestHydrationAsync(ResolvePath(revision));
                        await hydrations.ClaimAsync(revision.RevisionId, now);
                    });
                Assert.True(admission.Allowed, admission.Message);

                platform.SetLocal(revision);
                ArchiveStorageSnapshot local = await capacity.GetStorageSnapshotAsync();
                peakReservedBytes = Math.Max(peakReservedBytes, local.ManagedReservedBytes);
                cumulativeLogicalBytes += revision.SizeBytes;

                await platform.RequestOnlineOnlyAsync(ResolvePath(revision));
                await hydrations.MarkReleaseRequestedAsync(revision.RevisionId, now);
                ArchiveStorageSnapshot released = await capacity.GetStorageSnapshotAsync();
                Assert.Equal(0, released.ManagedReservedBytes);
            }

            Assert.True(cumulativeLogicalBytes > managedBudget);
            Assert.Equal(1_200, cumulativeLogicalBytes);
            Assert.Equal(400, peakReservedBytes);
            Assert.True(peakReservedBytes <= managedBudget);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

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
        CatalogueAssetRevision saved = await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(source, asset, revision);
        return await new SqliteLocalBatchRepository(database).GetAssetRevisionAsync(saved.Id)
            ?? throw new InvalidOperationException("Saved revision was unavailable.");
    }

    private static string ResolvePath(CatalogueProcessingAssetRevision revision) =>
        Path.GetFullPath(Path.Combine(revision.RootLocator, revision.SourceKey));

    private sealed class FixedStorageProbe(long bytes) : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => bytes;
    }

    private sealed class ImmediateFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        private readonly Dictionary<string, OneDriveFilesOnDemandState> _states = new(StringComparer.OrdinalIgnoreCase);

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
            _states[Path.GetFullPath(path)] = new OneDriveFilesOnDemandState(AssetAvailability.OnlineOnly, false, true);
            return Task.CompletedTask;
        }

        public void SetLocal(CatalogueProcessingAssetRevision revision) =>
            _states[ResolvePath(revision)] = new OneDriveFilesOnDemandState(AssetAvailability.Local, true, false);
    }
}
