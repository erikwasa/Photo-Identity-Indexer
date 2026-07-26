using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteProcessingRepositoryTests
{
    [Fact]
    public async Task Create_run_round_trips_and_deduplicates_jobs_by_idempotency_key()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions = await SeedRevisionsAsync(database, 2);
            DateTimeOffset now = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob[] jobs =
            [
                CreateJob(run.Id, revisions[0].Id, now),
                CreateJob(run.Id, revisions[1].Id, now.AddMinutes(1)),
            ];
            SqliteProcessingRepository repository = new(database);

            CatalogueProcessingBatch persisted = await repository.CreateRunAsync(run, jobs);
            CatalogueProcessingJob duplicate = new(
                ProcessingJobId.New(),
                run.Id,
                revisions[0].Id,
                ProcessingJobStatus.Queued,
                0,
                now.AddMinutes(5),
                idempotencyKey: jobs[0].IdempotencyKey);
            CatalogueProcessingBatch duplicateResult = await repository.CreateRunAsync(run, [duplicate]);

            Assert.Equal(run, persisted.Run);
            Assert.Equal(2, persisted.Jobs.Count);
            Assert.Equal(2, duplicateResult.Jobs.Count);
            Assert.DoesNotContain(duplicateResult.Jobs, job => job.Id == duplicate.Id);
            Assert.Equal(2, duplicateResult.Jobs.Select(job => job.IdempotencyKey).Distinct().Count());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Checkpoint_extends_lease_and_expired_job_is_reclaimed_with_new_token()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueAssetRevision revision = (await SeedRevisionsAsync(database, 1))[0];
            DateTimeOffset now = new(2026, 7, 26, 10, 10, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob job = CreateJob(run.Id, revision.Id, now);
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, [job]);

            CatalogueProcessingJob firstClaim = Assert.IsType<CatalogueProcessingJob>(
                await repository.ClaimNextJobAsync(run.Id, now, TimeSpan.FromMinutes(1)));
            ProcessingLeaseToken firstToken = Assert.IsType<ProcessingLeaseToken>(firstClaim.LeaseToken);
            CatalogueProcessingJob checkpointed = await repository.SaveCheckpointAsync(
                job.Id,
                firstToken,
                """{"stage":"decoded"}""",
                now.AddSeconds(30),
                TimeSpan.FromMinutes(1));

            Assert.Equal(now.AddSeconds(90), checkpointed.LeasedUntilUtc);
            Assert.Null(await repository.ClaimNextJobAsync(
                run.Id,
                now.AddSeconds(89),
                TimeSpan.FromMinutes(1)));

            CatalogueProcessingJob reclaimed = Assert.IsType<CatalogueProcessingJob>(
                await repository.ClaimNextJobAsync(
                    run.Id,
                    now.AddSeconds(91),
                    TimeSpan.FromMinutes(1)));
            ProcessingLeaseToken secondToken = Assert.IsType<ProcessingLeaseToken>(reclaimed.LeaseToken);
            Assert.NotEqual(firstToken, secondToken);
            Assert.Equal(2, reclaimed.AttemptCount);
            Assert.Equal("""{"stage":"decoded"}""", reclaimed.CheckpointJson);

            await Assert.ThrowsAsync<ProcessingLeaseLostException>(
                () => repository.CompleteJobAsync(job.Id, firstToken, now.AddSeconds(92)));
            CatalogueProcessingJob completed = await repository.CompleteJobAsync(
                job.Id,
                secondToken,
                now.AddSeconds(92));
            Assert.Equal(ProcessingJobStatus.Succeeded, completed.Status);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Classified_failures_preserve_attempts_and_summary_counts()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueAssetRevision revision = (await SeedRevisionsAsync(database, 1))[0];
            DateTimeOffset now = new(2026, 7, 26, 10, 20, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob job = CreateJob(run.Id, revision.Id, now);
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, [job]);

            CatalogueProcessingJob firstClaim = Assert.IsType<CatalogueProcessingJob>(
                await repository.ClaimNextJobAsync(run.Id, now, TimeSpan.FromMinutes(2)));
            DateTimeOffset retryAt = now.AddMinutes(5);
            CatalogueProcessingJob queued = await repository.FailJobAsync(
                job.Id,
                firstClaim.LeaseToken!.Value,
                ProcessingFailureKind.Transient,
                "temporary decoder failure",
                now.AddMinutes(1),
                retryAt);

            Assert.Equal(ProcessingJobStatus.Queued, queued.Status);
            Assert.Equal(ProcessingFailureKind.Transient, queued.LastFailureKind);
            Assert.Equal(1, queued.AttemptCount);
            Assert.Null(await repository.ClaimNextJobAsync(
                run.Id,
                retryAt.AddTicks(-1),
                TimeSpan.FromMinutes(2)));

            CatalogueProcessingJob secondClaim = Assert.IsType<CatalogueProcessingJob>(
                await repository.ClaimNextJobAsync(run.Id, retryAt, TimeSpan.FromMinutes(2)));
            await repository.FailJobAsync(
                job.Id,
                secondClaim.LeaseToken!.Value,
                ProcessingFailureKind.Permanent,
                "corrupt media",
                retryAt.AddMinutes(1));
            await repository.CompleteRunAsync(run.Id, retryAt.AddMinutes(2));

            ProcessingRunSummary summary = await repository.GetRunSummaryAsync(run.Id);
            CatalogueProcessingJob persisted = Assert.IsType<CatalogueProcessingJob>(
                await repository.GetJobAsync(job.Id));
            Assert.Equal(ProcessingRunStatus.Failed, summary.Status);
            Assert.Equal(1, summary.FailedJobs);
            Assert.Equal(2, summary.AttemptCount);
            Assert.Equal(ProcessingFailureKind.Permanent, persisted.LastFailureKind);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Cancellation_invalidates_active_lease_and_cancels_unfinished_jobs()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions = await SeedRevisionsAsync(database, 2);
            DateTimeOffset now = new(2026, 7, 26, 10, 30, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob[] jobs =
            [
                CreateJob(run.Id, revisions[0].Id, now),
                CreateJob(run.Id, revisions[1].Id, now),
            ];
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, jobs);
            CatalogueProcessingJob active = Assert.IsType<CatalogueProcessingJob>(
                await repository.ClaimNextJobAsync(run.Id, now, TimeSpan.FromMinutes(5)));

            CatalogueProcessingRun cancelled = await repository.RequestCancellationAsync(
                run.Id,
                now.AddMinutes(1));
            ProcessingRunSummary summary = await repository.GetRunSummaryAsync(run.Id);

            Assert.Equal(ProcessingRunStatus.Cancelled, cancelled.Status);
            Assert.Equal(2, summary.CancelledJobs);
            Assert.Null(await repository.ClaimNextJobAsync(
                run.Id,
                now.AddMinutes(10),
                TimeSpan.FromMinutes(5)));
            await Assert.ThrowsAsync<ProcessingLeaseLostException>(
                () => repository.CompleteJobAsync(
                    active.Id,
                    active.LeaseToken!.Value,
                    now.AddMinutes(2)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Concurrent_workers_claim_distinct_due_jobs()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions = await SeedRevisionsAsync(database, 2);
            DateTimeOffset now = new(2026, 7, 26, 10, 40, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(
                run,
                revisions.Select(revision => CreateJob(run.Id, revision.Id, now)).ToArray());

            CatalogueProcessingJob?[] claimed = await Task.WhenAll(
                repository.ClaimNextJobAsync(run.Id, now, TimeSpan.FromMinutes(5)),
                repository.ClaimNextJobAsync(run.Id, now, TimeSpan.FromMinutes(5)));

            Assert.All(claimed, job => Assert.NotNull(job));
            Assert.Equal(2, claimed.Select(job => job!.Id).Distinct().Count());
            Assert.Equal(2, claimed.Select(job => job!.LeaseToken).Distinct().Count());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    internal static CatalogueProcessingRun CreateRun(DateTimeOffset now) =>
        new(
            ProcessingRunId.New(),
            ProcessingRunStatus.Pending,
            """{"detector":"yunet","embedder":"sface"}""",
            now);

    internal static CatalogueProcessingJob CreateJob(
        ProcessingRunId runId,
        AssetRevisionId revisionId,
        DateTimeOffset availableAtUtc) =>
        new(
            ProcessingJobId.New(),
            runId,
            revisionId,
            ProcessingJobStatus.Queued,
            attemptCount: 0,
            availableAtUtc);

    internal static async Task<IReadOnlyList<CatalogueAssetRevision>> SeedRevisionsAsync(
        SqliteCatalogueDatabase database,
        int count)
    {
        DateTimeOffset now = new(2026, 7, 26, 9, 55, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(Path.GetTempPath(), sourceId.ToString()),
            now);
        CatalogueAsset asset = new(assetId, sourceId, "photo.jpg", now);
        SqliteAssetCatalogueRepository repository = new(database);
        List<CatalogueAssetRevision> revisions = [];

        for (int index = 0; index < count; index++)
        {
            string hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index)))
                .ToLowerInvariant();
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(hash),
                1234 + index,
                now.AddTicks(index),
                "image/jpeg",
                640,
                480);
            revisions.Add(await repository.SaveRevisionAsync(source, asset, revision));
        }

        return revisions;
    }

    internal static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
