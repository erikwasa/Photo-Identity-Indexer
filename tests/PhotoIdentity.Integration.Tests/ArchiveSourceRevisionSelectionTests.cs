using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveSourceRevisionSelectionTests
{
    [Fact]
    public async Task Reverting_to_previously_seen_content_reselects_that_immutable_revision_as_current()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset t0 = new(2026, 8, 9, 8, 30, 0, TimeSpan.Zero);
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, t0);
            MutableLocalSource source = new(catalogueSource.Id, "photo.jpg", [1, 2, 3], t0);
            SqliteArchiveSourceCatalogueScanner scanner = new(database);
            SqliteArchiveSourceObservationRepository observations = new(database);

            ArchiveSourceCatalogueScanSummary first = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                t0);
            Assert.Equal(1, first.NewRevisionCount);
            ArchiveSourceObservation afterFirst = Assert.IsType<ArchiveSourceObservation>(
                await observations.GetNextPendingAsync(catalogueSource.Id) is null
                    ? await FindObservationAsync(database, catalogueSource.Id)
                    : null);
            AssetRevisionId revisionA = Assert.IsType<AssetRevisionId>(afterFirst.VerifiedRevisionId);

            source.Set([9, 8, 7, 6], t0.AddMinutes(1));
            ArchiveSourceCatalogueScanSummary second = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                t0.AddMinutes(1));
            Assert.Equal(1, second.NewRevisionCount);
            ArchiveSourceObservation afterSecond = await FindObservationAsync(database, catalogueSource.Id);
            AssetRevisionId revisionB = Assert.IsType<AssetRevisionId>(afterSecond.VerifiedRevisionId);
            Assert.NotEqual(revisionA, revisionB);

            source.Set([1, 2, 3], t0.AddMinutes(2));
            ArchiveSourceCatalogueScanSummary reverted = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                t0.AddMinutes(2));
            Assert.Equal(0, reverted.NewRevisionCount);
            Assert.Equal(1, reverted.UnchangedFileCount);
            ArchiveSourceObservation afterRevert = await FindObservationAsync(database, catalogueSource.Id);
            Assert.Equal(revisionA, afterRevert.VerifiedRevisionId);

            Sha256Digest profileHash = new(new string('a', 64));
            IReadOnlyList<AssetRevisionId> pending = await new SqliteArchiveAnalysisRepository(database)
                .GetPendingCurrentRevisionIdsAsync(catalogueSource.Id, profileHash);
            Assert.Single(pending);
            Assert.Equal(revisionA, pending[0]);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Status_exposes_source_verification_separately_from_online_only_availability()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset t0 = new(2026, 8, 9, 8, 45, 0, TimeSpan.Zero);
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, t0);
            MutableLocalSource source = new(catalogueSource.Id, "1970/01/photo.jpg", [1, 2, 3], t0);
            SqliteArchiveSourceCatalogueScanner scanner = new(database);
            _ = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970", true),
                t0);

            source.Set([1, 2, 3, 4], t0.AddMinutes(1), AssetAvailability.OnlineOnly);
            ArchiveSourceCatalogueScanSummary scan = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970", true),
                t0.AddMinutes(1));
            Assert.Equal(1, scan.OnlineOnlyFileCount);
            Assert.Equal(1, scan.NeedsSourceVerificationCount);
            Assert.Equal(1, source.OpenCount);

            Sha256Digest profileHash = new(new string('b', 64));
            SqliteArchiveStatusRepository statusRepository = new(database);
            CatalogueArchiveFolderStatus status = await statusRepository.GetStatusAsync(
                catalogueSource.Id,
                "1970",
                profileHash);
            Assert.Equal(1, status.OnlineOnlyImages);
            Assert.Equal(1, status.NeedsSourceVerificationImages);
            Assert.Equal(0, status.PendingImages);

            CatalogueArchiveItemPage page = await statusRepository.GetItemsAsync(
                catalogueSource.Id,
                "1970",
                profileHash,
                "needs-source-verification",
                0,
                50);
            CatalogueArchiveItemStatus item = Assert.Single(page.Items);
            Assert.Equal("online-only", item.Availability);
            Assert.Equal("needs-source-verification", item.SourceVerificationState);
            Assert.Equal("needs-source-verification", item.AnalysisState);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<ArchiveSourceObservation> FindObservationAsync(
        SqliteCatalogueDatabase database,
        SourceId sourceId)
    {
        await using Microsoft.Data.Sqlite.SqliteConnection connection = await database.OpenConnectionAsync();
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM assets WHERE source_id = $source_id ORDER BY source_key LIMIT 1;";
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        object? value = await command.ExecuteScalarAsync();
        AssetId assetId = value is string id
            ? AssetId.From(Guid.Parse(id))
            : throw new InvalidOperationException("Test asset was unavailable.");
        return await new SqliteArchiveSourceObservationRepository(database).GetAsync(assetId)
            ?? throw new InvalidOperationException("Source observation was unavailable.");
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

    private sealed class MutableLocalSource : IAssetSource
    {
        private readonly SourceId _sourceId;
        private readonly string _sourceKey;
        private byte[] _content;
        private DateTimeOffset _lastWriteTimeUtc;
        private AssetAvailability _availability;

        public MutableLocalSource(
            SourceId sourceId,
            string sourceKey,
            byte[] content,
            DateTimeOffset lastWriteTimeUtc,
            AssetAvailability availability = AssetAvailability.Local)
        {
            _sourceId = sourceId;
            _sourceKey = sourceKey;
            _content = content;
            _lastWriteTimeUtc = lastWriteTimeUtc;
            _availability = availability;
        }

        public int OpenCount { get; private set; }

        public void Set(
            byte[] content,
            DateTimeOffset lastWriteTimeUtc,
            AssetAvailability availability = AssetAvailability.Local)
        {
            _content = content;
            _lastWriteTimeUtc = lastWriteTimeUtc;
            _availability = availability;
        }

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceAsset(
                new SourceAssetReference(_sourceId, _sourceKey),
                _sourceKey,
                "image/jpeg",
                _content.LongLength,
                _lastWriteTimeUtc,
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
            return Task.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }
    }
}
