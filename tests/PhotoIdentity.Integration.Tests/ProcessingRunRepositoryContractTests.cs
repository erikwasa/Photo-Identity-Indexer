using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ProcessingRunRepositoryContractTests
{
    [Fact]
    public async Task Sqlite_adapter_preserves_run_creation_lookup_jobs_and_cancellation_through_contract()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 9, 2, 18, 30, 0, TimeSpan.Zero);
            CatalogueSource source = new(
                SourceId.New(),
                "local-folder",
                Path.Combine(directory, "source"),
                now);
            CatalogueAsset asset = new(
                AssetId.New(),
                source.Id,
                "photo.jpg",
                now);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                asset.Id,
                new Sha256Digest(new string('a', 64)),
                42,
                now,
                "image/jpeg");
            CatalogueAssetRevision persistedRevision = await new SqliteAssetCatalogueRepository(database)
                .SaveRevisionAsync(source, asset, revision);

            ProcessingRunId runId = ProcessingRunId.New();
            CatalogueProcessingRun run = new(
                runId,
                ProcessingRunStatus.Pending,
                """{"kind":"contract-test"}""",
                now);
            CatalogueProcessingJob job = new(
                ProcessingJobId.New(),
                runId,
                persistedRevision.Id,
                ProcessingJobStatus.Queued,
                attemptCount: 0,
                availableAtUtc: now,
                idempotencyKey: $"contract:{runId}:{persistedRevision.Id}");

            IProcessingRunRepository repository = new SqliteProcessingRepository(database);

            CatalogueProcessingBatch created = await repository.CreateRunAsync(run, [job]);
            CatalogueProcessingRun? loaded = await repository.GetRunAsync(runId);
            IReadOnlyList<CatalogueProcessingJob> jobs = await repository.GetJobsAsync(runId);
            CatalogueProcessingRun cancelled = await repository.RequestCancellationAsync(
                runId,
                now.AddMinutes(1));

            Assert.Equal(run, created.Run);
            Assert.Equal(run, loaded);
            Assert.Equal(job, Assert.Single(jobs));
            Assert.Equal(ProcessingRunStatus.Cancelled, cancelled.Status);
            Assert.Equal(now.AddMinutes(1), cancelled.CancellationRequestedAtUtc);
            Assert.Equal(now.AddMinutes(1), cancelled.CompletedAtUtc);

            CatalogueProcessingJob cancelledJob = Assert.Single(await repository.GetJobsAsync(runId));
            Assert.Equal(ProcessingJobStatus.Cancelled, cancelledJob.Status);
            Assert.Equal(now.AddMinutes(1), cancelledJob.CompletedAtUtc);
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
}
