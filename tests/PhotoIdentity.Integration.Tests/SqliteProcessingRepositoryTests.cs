using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteProcessingRepositoryTests
{
    [Fact]
    public async Task Create_run_round_trips_and_deduplicates_jobs_by_revision()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions = await SeedRevisionsAsync(database, 2);
            DateTimeOffset now = new(2026, 7, 25, 22, 0, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob[] jobs =
            [
                CreateJob(run.Id, revisions[0].Id, now),
                CreateJob(run.Id, revisions[1].Id, now.AddMinutes(1)),
            ];
            SqliteProcessingRepository repository = new(database);

            CatalogueProcessingBatch persisted = await repository.CreateRunAsync(run, jobs);

            Assert.Equal(run, persisted.Run);
            Assert.Equal(2, persisted.Jobs.Count);
            Assert.Equal(run, await repository.GetRunAsync(run.Id));
            Assert.Equal(
                jobs.Select(job => job.Id).OrderBy(id => id.ToString()),
                persisted.Jobs.Select(job => job.Id).OrderBy(id => id.ToString()));

            CatalogueProcessingJob duplicate = CreateJob(
                run.Id,
                revisions[0].Id,
                now.AddMinutes(5));
            CatalogueProcessingBatch duplicateResult = await repository.CreateRunAsync(run, [duplicate]);

            Assert.Equal(2, duplicateResult.Jobs.Count);
            Assert.DoesNotContain(duplicateResult.Jobs, job => job.Id == duplicate.Id);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await CountAsync(connection, "processing_runs"));
            Assert.Equal(2, await CountAsync(connection, "processing_jobs"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Claim_retry_and_completion_preserve_attempt_history()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueAssetRevision revision = (await SeedRevisionsAsync(database, 1))[0];
            DateTimeOffset now = new(2026, 7, 25, 22, 10, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob job = CreateJob(run.Id, revision.Id, now);
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, [job]);

            CatalogueProcessingJob firstClaim = Assert.IsType<CatalogueProcessingJob>(
                await repository.ClaimNextJobAsync(run.Id, now));
            Assert.Equal(ProcessingJobStatus.Running, firstClaim.Status);
            Assert.Equal(1, firstClaim.AttemptCount);
            Assert.Equal(now, firstClaim.StartedAtUtc);
            Assert.Equal(ProcessingRunStatus.Running, (await repository.GetRunAsync(run.Id))!.Status);

            DateTimeOffset retryAt = now.AddMinutes(10);
            CatalogueProcessingJob queued = await repository.FailJobAsync(
                job.Id,
                "transient decoder failure",
                now.AddMinutes(1),
                retryAt);
            Assert.Equal(ProcessingJobStatus.Queued, queued.Status);
            Assert.Equal(1, queued.AttemptCount);
            Assert.Equal(retryAt, queued.AvailableAtUtc);
            Assert.Equal("transient decoder failure", queued.Error);
            Assert.Null(queued.StartedAtUtc);
            Assert.Null(await repository.ClaimNextJobAsync(run.Id, retryAt.AddTicks(-1)));

            CatalogueProcessingJob secondClaim = Assert.IsType<CatalogueProcessingJob>(
                await repository.ClaimNextJobAsync(run.Id, retryAt));
            Assert.Equal(2, secondClaim.AttemptCount);
            Assert.Null(secondClaim.Error);

            CatalogueProcessingJob succeeded = await repository.CompleteJobAsync(
                job.Id,
                retryAt.AddMinutes(1));
            Assert.Equal(ProcessingJobStatus.Succeeded, succeeded.Status);
            Assert.Equal(2, succeeded.AttemptCount);

            CatalogueProcessingRun completed = await repository.CompleteRunAsync(
                run.Id,
                retryAt.AddMinutes(2));
            Assert.Equal(ProcessingRunStatus.Completed, completed.Status);
            Assert.Null(completed.Error);
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
            DateTimeOffset now = new(2026, 7, 25, 22, 20, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob[] jobs =
            [
                CreateJob(run.Id, revisions[0].Id, now),
                CreateJob(run.Id, revisions[1].Id, now),
            ];
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, jobs);

            CatalogueProcessingJob?[] claimed = await Task.WhenAll(
                repository.ClaimNextJobAsync(run.Id, now),
                repository.ClaimNextJobAsync(run.Id, now));

            Assert.All(claimed, job => Assert.NotNull(job));
            Assert.Equal(2, claimed.Select(job => job!.Id).Distinct().Count());
            Assert.Null(await repository.ClaimNextJobAsync(run.Id, now));

            IReadOnlyList<CatalogueProcessingJob> persisted = await repository.GetJobsAsync(run.Id);
            Assert.All(persisted, job =>
            {
                Assert.Equal(ProcessingJobStatus.Running, job.Status);
                Assert.Equal(1, job.AttemptCount);
            });
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Complete_run_rejects_unfinished_jobs_and_reflects_terminal_failure()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueAssetRevision revision = (await SeedRevisionsAsync(database, 1))[0];
            DateTimeOffset now = new(2026, 7, 25, 22, 30, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob job = CreateJob(run.Id, revision.Id, now);
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, [job]);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.CompleteRunAsync(run.Id, now.AddMinutes(1)));

            await repository.ClaimNextJobAsync(run.Id, now);
            await repository.FailJobAsync(job.Id, "corrupt image", now.AddMinutes(2));
            CatalogueProcessingRun failed = await repository.CompleteRunAsync(
                run.Id,
                now.AddMinutes(3));

            Assert.Equal(ProcessingRunStatus.Failed, failed.Status);
            Assert.Equal("1 processing job(s) failed.", failed.Error);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Create_run_rolls_back_when_an_asset_revision_is_missing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 7, 25, 22, 40, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = CreateRun(now);
            CatalogueProcessingJob job = CreateJob(run.Id, AssetRevisionId.New(), now);
            SqliteProcessingRepository repository = new(database);

            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => repository.CreateRunAsync(run, [job]));

            Assert.Equal(19, exception.SqliteErrorCode);
            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(0, await CountAsync(connection, "processing_runs"));
            Assert.Equal(0, await CountAsync(connection, "processing_jobs"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static CatalogueProcessingRun CreateRun(DateTimeOffset now) =>
        new(
            ProcessingRunId.New(),
            ProcessingRunStatus.Pending,
            """{"detector":"yunet","embedder":"sface"}""",
            now);

    private static CatalogueProcessingJob CreateJob(
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

    private static async Task<IReadOnlyList<CatalogueAssetRevision>> SeedRevisionsAsync(
        SqliteCatalogueDatabase database,
        int count)
    {
        DateTimeOffset now = new(2026, 7, 25, 21, 55, 0, TimeSpan.Zero);
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
            char hashCharacter = (char)('a' + index);
            CatalogueAssetRevision revision = new(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(new string(hashCharacter, 64)),
                1234 + index,
                now.AddMinutes(index),
                "image/jpeg",
                640,
                480);
            revisions.Add(await repository.SaveRevisionAsync(source, asset, revision));
        }

        return revisions;
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
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
