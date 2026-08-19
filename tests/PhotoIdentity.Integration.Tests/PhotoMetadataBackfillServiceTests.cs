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
            PhotoMetadataBackfillService service = CreateService(database, repository, platform, reader);

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
            FakeMetadataReader reader = new(RichMetadata());
            PhotoMetadataBackfillService service = CreateService(database, repository, platform, reader);

            PhotoMetadataBackfillReport report = await service.ExecuteBatchAsync();
            PhotoCaptureMetadata? persisted = await repository.GetPhotoMetadataAsync(revisionId);
            CatalogueExtendedPhotoMetadata? extended = await new SqliteExtendedPhotoMetadataRepository(database)
                .GetAsync(revisionId);
            CataloguePhotoMetadataInspection? inspection = await new SqlitePhotoMetadataInspectionRepository(database)
                .GetAsync(revisionId);

            Assert.Equal(1, report.Persisted);
            Assert.Equal(1, report.NewlyInspected);
            Assert.Equal(0, report.RefreshedStale);
            Assert.NotNull(persisted);
            Assert.Equal(new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Unspecified), persisted.TakenAtLocal);
            Assert.Equal(59.3293, persisted.Latitude);
            Assert.Equal(18.0686, persisted.Longitude);
            Assert.NotNull(extended);
            Assert.Equal("Example Camera Co.", extended.CameraMake);
            Assert.Equal("Model X", extended.CameraModel);
            Assert.Equal("35mm Prime", extended.LensModel);
            Assert.Equal("ISO 200", extended.Iso);
            Assert.Equal("42 metres", extended.GpsAltitude);
            Assert.Equal(2, extended.RawTags.Count);
            Assert.NotNull(inspection);
            Assert.Equal(PhotoMetadataExtractionContract.CurrentVersion, inspection.ExtractionContractVersion);
            Assert.Equal(1, reader.ReadCount);
            Assert.Equal(0, platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Legacy_capture_row_is_refreshed_and_gains_richer_metadata()
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
                "legacy.jpg",
                [9, 8, 7, 6]);
            await repository.SavePhotoMetadataAsync(
                revisionId,
                new PhotoCaptureMetadata(
                    new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
                    null,
                    59.0,
                    18.0),
                DateTimeOffset.UtcNow);

            Assert.Null(await new SqliteExtendedPhotoMetadataRepository(database).GetAsync(revisionId));
            Assert.Null(await new SqlitePhotoMetadataInspectionRepository(database).GetAsync(revisionId));

            FakeMetadataReader reader = new(RichMetadata());
            PhotoMetadataBackfillService service = CreateService(
                database,
                repository,
                new FakeFilesOnDemandPlatform(AssetAvailability.Local),
                reader);

            PhotoMetadataBackfillReport report = await service.ExecuteBatchAsync();
            CatalogueExtendedPhotoMetadata? extended = await new SqliteExtendedPhotoMetadataRepository(database)
                .GetAsync(revisionId);
            CataloguePhotoMetadataInspection? inspection = await new SqlitePhotoMetadataInspectionRepository(database)
                .GetAsync(revisionId);

            Assert.Equal(1, report.Persisted);
            Assert.Equal(0, report.NewlyInspected);
            Assert.Equal(1, report.RefreshedStale);
            Assert.Equal(0, report.ForcedCurrentRefresh);
            Assert.NotNull(extended);
            Assert.Equal("Model X", extended.CameraModel);
            Assert.NotNull(inspection);
            Assert.Equal(PhotoMetadataExtractionContract.CurrentVersion, inspection.ExtractionContractVersion);
            Assert.Equal(1, reader.ReadCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Current_version_row_is_skipped_by_default_and_can_be_force_refreshed()
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
                "current.jpg",
                [4, 5, 6, 7]);
            FakeFilesOnDemandPlatform platform = new(AssetAvailability.Local);
            FakeMetadataReader reader = new(RichMetadata());
            PhotoMetadataBackfillService service = CreateService(database, repository, platform, reader);

            PhotoMetadataBackfillReport initial = await service.ExecuteBatchAsync();
            PhotoMetadataBackfillReport skipped = await service.ExecuteBatchAsync();
            PhotoMetadataBackfillReport forced = await service.ExecuteBatchAsync(force: true);

            Assert.Equal(1, initial.Persisted);
            Assert.Equal(0, skipped.Candidates);
            Assert.Equal(0, skipped.Persisted);
            Assert.Equal(1, forced.Persisted);
            Assert.Equal(1, forced.ForcedCurrentRefresh);
            Assert.Equal(2, reader.ReadCount);
            Assert.Equal(0, platform.HydrationRequests);
            Assert.Equal(
                PhotoMetadataExtractionContract.CurrentVersion,
                (await new SqlitePhotoMetadataInspectionRepository(database).GetAsync(revisionId))?.ExtractionContractVersion);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Stale_online_only_row_is_deferred_without_being_marked_current()
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
                "stale-online.jpg",
                [7, 7, 7]);
            await repository.SavePhotoMetadataAsync(
                revisionId,
                new PhotoCaptureMetadata(),
                DateTimeOffset.UtcNow);
            FakeFilesOnDemandPlatform platform = new(AssetAvailability.OnlineOnly);
            FakeMetadataReader reader = new(RichMetadata());
            PhotoMetadataBackfillService service = CreateService(database, repository, platform, reader);

            PhotoMetadataBackfillReport report = await service.ExecuteBatchAsync();

            Assert.Equal(1, report.Candidates);
            Assert.Equal(0, report.Persisted);
            Assert.Equal(1, report.DeferredNonLocal);
            Assert.Null(await new SqlitePhotoMetadataInspectionRepository(database).GetAsync(revisionId));
            Assert.Equal(0, reader.ReadCount);
            Assert.Equal(0, platform.HydrationRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Offset_can_move_past_deferred_candidate_without_marking_it_complete()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository repository = new(database);
            AssetRevisionId first = await CreateRevisionForFileAsync(repository, directory, "first.jpg", [1]);
            AssetRevisionId second = await CreateRevisionForFileAsync(repository, directory, "second.jpg", [2]);
            SqlitePhotoMetadataBackfillRepository backfill = new(database);

            IReadOnlyList<PhotoMetadataBackfillCandidate> firstPage = await backfill.GetCandidatesAsync(1, 0);
            IReadOnlyList<PhotoMetadataBackfillCandidate> secondPage = await backfill.GetCandidatesAsync(1, 1);

            AssetRevisionId firstPageId = Assert.Single(firstPage).RevisionId;
            AssetRevisionId secondPageId = Assert.Single(secondPage).RevisionId;
            Assert.NotEqual(firstPageId, secondPageId);
            AssetRevisionId[] pagedIds = [firstPageId, secondPageId];
            Assert.Contains(first, pagedIds);
            Assert.Contains(second, pagedIds);
            Assert.Null(await repository.GetPhotoMetadataAsync(first));
            Assert.Null(await repository.GetPhotoMetadataAsync(second));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static PhotoCaptureMetadata RichMetadata() => new(
        new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Unspecified),
        null,
        59.3293,
        18.0686,
        cameraMake: "Example Camera Co.",
        cameraModel: "Model X",
        lensModel: "35mm Prime",
        iso: "ISO 200",
        gpsAltitude: "42 metres",
        rawTags:
        [
            new PhotoMetadataTag("Exif IFD0", "Make", "Example Camera Co."),
            new PhotoMetadataTag("GPS", "Altitude", "42 metres"),
        ]);

    private static PhotoMetadataBackfillService CreateService(
        SqliteCatalogueDatabase database,
        SqliteAssetCatalogueRepository repository,
        IOneDriveFilesOnDemandPlatform platform,
        IPhotoMetadataReader reader)
    {
        PhotoMetadataInspectionService inspection = new(
            repository,
            new SqliteExtendedPhotoMetadataRepository(database),
            new SqlitePhotoMetadataInspectionRepository(database),
            reader,
            TimeProvider.System);
        return new PhotoMetadataBackfillService(
            new SqlitePhotoMetadataBackfillRepository(database),
            platform,
            inspection);
    }

    private static async Task<AssetRevisionId> CreateRevisionForFileAsync(
        SqliteAssetCatalogueRepository repository,
        string root,
        string sourceKey,
        byte[] content)
    {
        string sourceRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceRoot);
        string itemKey = Path.GetFileName(sourceKey);
        string path = Path.Combine(sourceRoot, itemKey);
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
            new CatalogueSource(sourceId, "local-folder", sourceRoot, now),
            new CatalogueAsset(assetId, sourceId, itemKey, now),
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
