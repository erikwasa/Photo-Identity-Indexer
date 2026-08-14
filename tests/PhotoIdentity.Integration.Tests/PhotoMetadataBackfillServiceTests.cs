using System.Security.Cryptography;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoMetadataBackfillServiceTests
{
    [Fact]
    public async Task Online_only_candidate_is_deferred_without_hydration_or_read()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            await CreateRevisionForFileAsync(repository, directory, "photo.jpg", [1, 2, 3, 4]);
            FakeFilesOnDemandPlatform platform = new(AssetAvailability.OnlineOnly);
            FakeMetadataReader reader = new();
            PhotoMetadataBackfillService service = new(repository, platform, reader, TimeProvider.System);

            PhotoMetadataBackfillReport report = await service.ExecuteBatchAsync();

            Assert.Equal(0, report.Persisted);
            Assert.Equal(1, report.DeferredNonLocal);
            Assert.Equal(0, reader.ReadCount);
            Assert.Equal(0, platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Local_matching_revision_is_verified_and_metadata_is_persisted()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            AssetRevisionId revisionId = await CreateRevisionForFileAsync(
                repository,
                directory,
                "photo.jpg",
                [5, 6, 7, 8, 9]);
            FakeFilesOnDemandPlatform platform = new(AssetAvailability.Local);
            FakeMetadataReader reader = new(new PhotoCaptureMetadata(
                new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Unspecified),
                null,
                59.3293,
                18.0686));
            PhotoMetadataBackfillService service = new(repository, platform, reader, TimeProvider.System);

            PhotoMetadataBackfillReport report = await service.ExecuteBatchAsync();
            PhotoCaptureMetadata? persisted = await repository.GetPhotoMetadataAsync(revisionId);

            Assert.Equal(1, report.Persisted);
            Assert.NotNull(persisted);
            Assert.Equal(new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Unspecified), persisted.TakenAtLocal);
            Assert.Equal(59.3293, persisted.Latitude);
            Assert.Equal(18.0686, persisted.Longitude);
            Assert.Equal(1, reader.ReadCount);
            Assert.Equal(0, platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<AssetRevisionId> CreateRevisionForFileAsync(
        SqliteAssetCatalogueRepository repository,
        string root,
        string sourceKey,
        byte[] content)
    {
        string path = Path.Combine(root, sourceKey);
        await File.WriteAllBytesAsync(path, content);
        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(hash),
            content.Length,
            now,
            "image/jpeg");
        await repository.SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", root, now),
            new CatalogueAsset(assetId, sourceId, sourceKey, now),
            revision);
        return revision.Id;
    }

    private sealed class FakeMetadataReader : IPhotoMetadataReader
    {
        private readonly PhotoCaptureMetadata _metadata;

        public FakeMetadataReader(PhotoCaptureMetadata? metadata = null)
        {
            _metadata = metadata ?? new PhotoCaptureMetadata();
        }

        public int ReadCount { get; private set; }

        public Task<PhotoCaptureMetadata> ReadAsync(
            Stream content,
            string? mediaType,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(_metadata);
        }
    }

    private sealed class FakeFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
    {
        private readonly AssetAvailability _availability;

        public FakeFilesOnDemandPlatform(AssetAvailability availability)
        {
            _availability = availability;
        }

        public int HydrationRequests { get; private set; }

        public OneDriveFilesOnDemandState GetState(string path) =>
            new(_availability, IsPinned: false, IsUnpinned: false);

        public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default)
        {
            HydrationRequests++;
            throw new InvalidOperationException("Metadata backfill must never request hydration.");
        }

        public Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
