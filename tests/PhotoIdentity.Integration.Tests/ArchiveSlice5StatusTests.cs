using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveSlice5StatusTests
{
    [Fact]
    public async Task Analyzed_revision_remains_analyzed_after_returning_online_only()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string month = Path.Combine(archiveRoot, "1970", "01");
            Directory.CreateDirectory(month);
            await File.WriteAllBytesAsync(Path.Combine(month, "photo.jpg"), [1, 2, 3, 4]);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteLocalBatchRepository assets = new(database);
            CatalogueSource sourceRecord = await assets.GetOrCreateLocalFolderSourceAsync(archiveRoot, Utc(10));
            LocalFolderAssetSource source = new(sourceRecord.Id, archiveRoot);
            await new SqliteSourceCatalogueScanner(database).ScanAsync(
                source,
                sourceRecord,
                new SourceScanOptions("1970/01", true),
                Utc(10));

            AssetRevisionId revisionId = Assert.Single(await assets.GetCurrentRevisionIdsAsync(sourceRecord.Id));
            CatalogueProcessingAssetRevision revision = await assets.GetAssetRevisionAsync(revisionId)
                ?? throw new InvalidOperationException("Test revision was unavailable.");

            AnalysisProfileDefinition profile = CreateProfile();
            Sha256Digest profileHash = profile.ComputeHash();
            ProcessingRunId runId = ProcessingRunId.New();
            CatalogueProcessingRun run = new(
                runId,
                ProcessingRunStatus.Pending,
                "{}",
                Utc(10));
            CatalogueProcessingJob job = new(
                ProcessingJobId.New(),
                runId,
                revisionId,
                ProcessingJobStatus.Queued,
                attemptCount: 0,
                availableAtUtc: Utc(10),
                idempotencyKey: $"slice5-status:{runId}:{revisionId}");
            await new SqliteProcessingRepository(database).CreateRunAsync(run, [job]);

            SqliteArchiveAnalysisRepository analysis = new(database);
            await analysis.RegisterRunAsync(runId, profile, Utc(10));
            await analysis.RecordCompletionAsync(runId, revisionId, profileHash, Utc(11));
            await new SqliteArchiveAvailabilityRepository(database).RecordAsync(
                revision.AssetId,
                AssetAvailability.OnlineOnly,
                Utc(12));

            CatalogueArchiveFolderStatus folderStatus = await new SqliteArchiveStatusRepository(database)
                .GetStatusAsync(sourceRecord.Id, "1970", profileHash);
            Assert.Equal(1, folderStatus.OnlineOnlyImages);
            Assert.Equal(1, folderStatus.AnalysedImages);

            CatalogueArchiveItemPage legacyAnalyzed = await new SqliteArchiveStatusRepository(database)
                .GetItemsAsync(sourceRecord.Id, "1970", profileHash, "analysed", 0, 50);
            CatalogueArchiveItemStatus legacyItem = Assert.Single(legacyAnalyzed.Items);
            Assert.Equal(revisionId, legacyItem.RevisionId);
            Assert.Equal("online-only", legacyItem.Availability);
            Assert.Equal("analysed", legacyItem.AnalysisState);

            CatalogueArchiveItemPage filtered = await new SqliteArchiveItemFilterRepository(database)
                .GetItemsAsync(
                    sourceRecord.Id,
                    "1970",
                    profileHash,
                    availability: "online-only",
                    verification: "verified",
                    analysis: "analysed",
                    offset: 0,
                    limit: 50);
            CatalogueArchiveItemStatus item = Assert.Single(filtered.Items);
            Assert.Equal(revisionId, item.RevisionId);
            Assert.Equal("online-only", item.Availability);
            Assert.Equal("verified", item.SourceVerificationState);
            Assert.Equal("analysed", item.AnalysisState);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Advancement_intent_survives_repository_recreation_and_can_be_paused()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            Directory.CreateDirectory(archiveRoot);
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource source = await new SqliteLocalBatchRepository(database)
                .GetOrCreateLocalFolderSourceAsync(archiveRoot, Utc(10));

            SqliteArchiveAdvancementRepository first = new(database);
            await first.RequestRunAsync(source.Id, Utc(11));

            ArchiveAdvancementState running = await new SqliteArchiveAdvancementRepository(database).GetAsync(source.Id)
                ?? throw new InvalidOperationException("Advancement state was unavailable.");
            Assert.True(running.IsRequested);
            Assert.Equal("queued", running.RuntimeState);
            Assert.True(running.SyncRequired);

            await new SqliteArchiveAdvancementRepository(database).UpdateRuntimeAsync(
                source.Id,
                "waiting",
                syncRequired: false,
                "Waiting for OneDrive.",
                Utc(12));

            ArchiveAdvancementState waiting = await new SqliteArchiveAdvancementRepository(database).GetAsync(source.Id)
                ?? throw new InvalidOperationException("Advancement state was unavailable after update.");
            Assert.True(waiting.IsRequested);
            Assert.Equal("waiting", waiting.RuntimeState);
            Assert.False(waiting.SyncRequired);

            await new SqliteArchiveAdvancementRepository(database).PauseAsync(source.Id, Utc(13));
            ArchiveAdvancementState paused = await new SqliteArchiveAdvancementRepository(database).GetAsync(source.Id)
                ?? throw new InvalidOperationException("Advancement state was unavailable after pause.");
            Assert.False(paused.IsRequested);
            Assert.Equal("paused", paused.RuntimeState);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static AnalysisProfileDefinition CreateProfile() => new(
        new Sha256Digest(new string('a', 64)),
        new ModelId("centerface-2019-fp32"),
        new Sha256Digest(new string('b', 64)),
        new ModelId("sface-2021dec-fp32"),
        new Sha256Digest(new string('c', 64)),
        new AlignmentProtocolId("sface-five-point-v1"));

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 10, hour, 0, 0, TimeSpan.Zero);

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
