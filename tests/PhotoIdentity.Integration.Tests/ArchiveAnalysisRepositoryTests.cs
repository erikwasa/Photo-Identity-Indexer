using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveAnalysisRepositoryTests
{
    [Fact]
    public async Task Successful_profile_completion_skips_unchanged_revision_but_not_changed_content()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string month = Path.Combine(archiveRoot, "1970", "01");
            Directory.CreateDirectory(month);
            string photo = Path.Combine(month, "photo.jpg");
            await File.WriteAllBytesAsync(photo, [1, 2, 3]);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteLocalBatchRepository assets = new(database);
            CatalogueSource sourceRecord = await assets.GetOrCreateLocalFolderSourceAsync(archiveRoot, Utc(10));
            LocalFolderAssetSource source = new(sourceRecord.Id, archiveRoot);
            SqliteSourceCatalogueScanner scanner = new(database);
            await scanner.ScanAsync(
                source,
                sourceRecord,
                new SourceScanOptions(RelativeRoot: "1970/01", Recursive: true),
                Utc(10));

            AssetRevisionId firstRevision = Assert.Single(
                await assets.GetCurrentRevisionIdsAsync(sourceRecord.Id));
            AnalysisProfileDefinition profile = CreateProfile();
            Sha256Digest profileHash = profile.ComputeHash();
            SqliteArchiveAnalysisRepository analysis = new(database);

            Assert.Equal(
                [firstRevision],
                await analysis.GetPendingCurrentRevisionIdsAsync(sourceRecord.Id, profileHash));
            Assert.Equal(
                0,
                await analysis.CountCompletedCurrentRevisionsAsync(sourceRecord.Id, profileHash));

            ProcessingRunId runId = ProcessingRunId.New();
            CatalogueProcessingRun run = new(
                runId,
                ProcessingRunStatus.Pending,
                "{}",
                Utc(10));
            CatalogueProcessingJob job = new(
                ProcessingJobId.New(),
                runId,
                firstRevision,
                ProcessingJobStatus.Queued,
                attemptCount: 0,
                availableAtUtc: Utc(10),
                idempotencyKey: $"archive-analysis-test:{runId}:{firstRevision}");
            await new SqliteProcessingRepository(database).CreateRunAsync(run, [job]);
            await analysis.RegisterRunAsync(runId, profile, Utc(10));

            // Completion intentionally carries no face count: a successful zero-face image
            // is just as complete as an image with detected faces.
            await analysis.RecordCompletionAsync(runId, firstRevision, profileHash, Utc(11));

            Assert.Empty(await analysis.GetPendingCurrentRevisionIdsAsync(sourceRecord.Id, profileHash));
            Assert.Equal(
                1,
                await analysis.CountCompletedCurrentRevisionsAsync(sourceRecord.Id, profileHash));

            await File.WriteAllBytesAsync(photo, [4, 5, 6, 7]);
            await scanner.ScanAsync(
                source,
                sourceRecord,
                new SourceScanOptions(RelativeRoot: "1970/01", Recursive: true),
                Utc(12));

            AssetRevisionId changedRevision = Assert.Single(
                await assets.GetCurrentRevisionIdsAsync(sourceRecord.Id));
            Assert.NotEqual(firstRevision, changedRevision);
            Assert.Equal(
                [changedRevision],
                await analysis.GetPendingCurrentRevisionIdsAsync(sourceRecord.Id, profileHash));
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
        new(2026, 8, 8, hour, 0, 0, TimeSpan.Zero);

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
