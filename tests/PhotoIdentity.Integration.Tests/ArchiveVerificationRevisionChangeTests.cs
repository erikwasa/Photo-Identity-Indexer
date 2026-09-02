using System.Security.Cryptography;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveVerificationRevisionChangeTests
{
    [Fact]
    public async Task Reverification_reports_previous_revision_when_identity_changes()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "photo.jpg");
            byte[] firstBytes = [1, 2, 3];
            byte[] changedBytes = [9, 8, 7, 6];
            DateTimeOffset firstObserved = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset changedObserved = firstObserved.AddMinutes(1);
            await File.WriteAllBytesAsync(path, changedBytes);
            File.SetLastWriteTimeUtc(path, changedObserved.UtcDateTime);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, firstObserved);
            SqliteArchiveSourceObservationRepository observations = new(database);

            SourceAsset first = new(
                new SourceAssetReference(source.Id, "photo.jpg"),
                "photo.jpg",
                "image/jpeg",
                firstBytes.LongLength,
                firstObserved,
                AssetAvailability.Local);
            ArchiveSourceObservationWriteResult firstWrite = await observations.RecordScanObservationAsync(
                source,
                first,
                Digest(firstBytes),
                firstObserved);
            AssetRevisionId firstRevision = Assert.IsType<AssetRevisionId>(firstWrite.RevisionId);

            SourceAsset changed = new(
                new SourceAssetReference(source.Id, "photo.jpg"),
                "photo.jpg",
                "image/jpeg",
                changedBytes.LongLength,
                changedObserved,
                AssetAvailability.OnlineOnly);
            ArchiveSourceObservationWriteResult changedWrite = await observations.RecordScanObservationAsync(
                source,
                changed,
                verifiedContentHash: null,
                changedObserved);
            Assert.Equal(ArchiveSourceVerificationState.NeedsSourceVerification, changedWrite.VerificationState);

            LocalFilesOnDemandPlatform platform = new();
            SqliteArchiveSourceHydrationRepository sourceHydrations = new(database);
            ArchiveHydrationCapacityService capacity = new(
                new SqliteArchiveHydrationRepository(database),
                sourceHydrations,
                new SqliteArchiveCoverageRepository(database),
                new SqliteArchiveStorageRepository(database),
                new SqliteArchiveAvailabilityRepository(database),
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

            ArchiveSourceVerificationAdvanceResult result = await verification.AdvanceAsync(source.Id);

            Assert.True(result.VerificationCompleted);
            Assert.Equal(firstRevision, result.PreviousRevisionId);
            Assert.True(result.RevisionChanged);
            Assert.True(result.NewRevision);
            Assert.NotNull(result.RevisionId);
            Assert.NotEqual(firstRevision, result.RevisionId!.Value);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            DeleteTemporaryDirectory(directory);
        }
    }

    private static Sha256Digest Digest(byte[] bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

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

    private sealed class LocalFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        public OneDriveFilesOnDemandState GetState(string path) =>
            new(AssetAvailability.Local, false, false);

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Local verification must not request hydration.");

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedStorageProbe(long availableBytes) : IArchiveStorageProbe
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
    }
}
