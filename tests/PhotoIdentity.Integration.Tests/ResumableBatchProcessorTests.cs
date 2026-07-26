using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ResumableBatchProcessorTests
{
    [Fact]
    public async Task Interrupted_run_repeats_only_the_active_job_after_lease_expiry()
    {
        string directory = SqliteProcessingRepositoryTests.CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions =
                await SqliteProcessingRepositoryTests.SeedRevisionsAsync(database, 3);
            DateTimeOffset now = new(2026, 7, 26, 11, 0, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = SqliteProcessingRepositoryTests.CreateRun(now);
            CatalogueProcessingJob[] jobs =
            [
                CreateOrderedJob(1, run.Id, revisions[0].Id, now),
                CreateOrderedJob(2, run.Id, revisions[1].Id, now),
                CreateOrderedJob(3, run.Id, revisions[2].Id, now),
            ];
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, jobs);

            ManualTimeProvider time = new(now);
            using CancellationTokenSource interrupted = new();
            RecordingHandler firstHandler = new(
                interruptRevision: revisions[1].Id,
                cancellation: interrupted);
            ResumableBatchProcessor firstProcessor = new(repository, firstHandler, time);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => firstProcessor.RunUntilIdleAsync(
                    run.Id,
                    new ResumableBatchProcessorOptions(TimeSpan.FromMinutes(1)),
                    interrupted.Token));

            IReadOnlyList<CatalogueProcessingJob> afterInterruption = await repository.GetJobsAsync(run.Id);
            Assert.Equal(ProcessingJobStatus.Succeeded, afterInterruption.Single(job => job.Id == jobs[0].Id).Status);
            CatalogueProcessingJob active = afterInterruption.Single(job => job.Id == jobs[1].Id);
            Assert.Equal(ProcessingJobStatus.Running, active.Status);
            Assert.Equal("""{"stage":"halfway"}""", active.CheckpointJson);
            Assert.Equal(ProcessingJobStatus.Queued, afterInterruption.Single(job => job.Id == jobs[2].Id).Status);

            time.Advance(TimeSpan.FromMinutes(2));
            RecordingHandler resumedHandler = new();
            ResumableBatchProcessor resumedProcessor = new(repository, resumedHandler, time);
            ResumableBatchProcessorResult resumed = await resumedProcessor.RunUntilIdleAsync(
                run.Id,
                new ResumableBatchProcessorOptions(TimeSpan.FromMinutes(1)));

            Assert.Equal(ProcessingRunStatus.Completed, resumed.Summary.Status);
            IReadOnlyList<CatalogueProcessingJob> completed = await repository.GetJobsAsync(run.Id);
            Assert.Equal(1, completed.Single(job => job.Id == jobs[0].Id).AttemptCount);
            Assert.Equal(2, completed.Single(job => job.Id == jobs[1].Id).AttemptCount);
            Assert.Equal(1, completed.Single(job => job.Id == jobs[2].Id).AttemptCount);
            Assert.Equal(1, firstHandler.Calls[revisions[0].Id]);
            Assert.Equal(1, firstHandler.Calls[revisions[1].Id]);
            Assert.False(firstHandler.Calls.ContainsKey(revisions[2].Id));
            Assert.False(resumedHandler.Calls.ContainsKey(revisions[0].Id));
            Assert.Equal(1, resumedHandler.Calls[revisions[1].Id]);
            Assert.Equal(1, resumedHandler.Calls[revisions[2].Id]);
        }
        finally
        {
            SqliteProcessingRepositoryTests.DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Transient_failure_retries_but_permanent_failure_is_terminal()
    {
        string directory = SqliteProcessingRepositoryTests.CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions =
                await SqliteProcessingRepositoryTests.SeedRevisionsAsync(database, 2);
            DateTimeOffset now = new(2026, 7, 26, 11, 10, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = SqliteProcessingRepositoryTests.CreateRun(now);
            CatalogueProcessingJob[] jobs =
            [
                CreateOrderedJob(1, run.Id, revisions[0].Id, now),
                CreateOrderedJob(2, run.Id, revisions[1].Id, now),
            ];
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, jobs);

            ManualTimeProvider time = new(now);
            ClassifiedFailureHandler handler = new(revisions[0].Id, revisions[1].Id);
            ResumableBatchProcessor processor = new(repository, handler, time);
            ResumableBatchProcessorOptions options = new(
                TimeSpan.FromMinutes(1),
                new ProcessingRetryPolicy(
                    maxAttempts: 3,
                    initialDelay: TimeSpan.FromSeconds(10),
                    maximumDelay: TimeSpan.FromMinutes(1)));

            ResumableBatchProcessorResult first = await processor.RunUntilIdleAsync(run.Id, options);
            Assert.Equal(1, first.Summary.QueuedJobs);
            Assert.Equal(1, first.Summary.FailedJobs);
            Assert.Equal(2, first.Summary.AttemptCount);

            time.Advance(TimeSpan.FromSeconds(10));
            ResumableBatchProcessorResult second = await processor.RunUntilIdleAsync(run.Id, options);

            Assert.Equal(ProcessingRunStatus.Failed, second.Summary.Status);
            Assert.Equal(1, second.Summary.SucceededJobs);
            Assert.Equal(1, second.Summary.FailedJobs);
            Assert.Equal(3, second.Summary.AttemptCount);
            Assert.Equal(2, handler.Calls[revisions[0].Id]);
            Assert.Equal(1, handler.Calls[revisions[1].Id]);
        }
        finally
        {
            SqliteProcessingRepositoryTests.DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Five_hundred_job_sample_produces_complete_status_summary()
    {
        string directory = SqliteProcessingRepositoryTests.CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions =
                await SqliteProcessingRepositoryTests.SeedRevisionsAsync(database, 500);
            DateTimeOffset now = new(2026, 7, 26, 11, 20, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = SqliteProcessingRepositoryTests.CreateRun(now);
            CatalogueProcessingJob[] jobs = revisions
                .Select(revision => SqliteProcessingRepositoryTests.CreateJob(run.Id, revision.Id, now))
                .ToArray();
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(run, jobs);

            RecordingHandler handler = new();
            ResumableBatchProcessor processor = new(repository, handler, new ManualTimeProvider(now));
            ResumableBatchProcessorResult result = await processor.RunUntilIdleAsync(
                run.Id,
                new ResumableBatchProcessorOptions(
                    leaseDuration: TimeSpan.FromMinutes(1),
                    maxAttemptsPerInvocation: 600));

            Assert.Equal(ProcessingRunStatus.Completed, result.Summary.Status);
            Assert.Equal(500, result.Summary.TotalJobs);
            Assert.Equal(500, result.Summary.SucceededJobs);
            Assert.Equal(0, result.Summary.FailedJobs);
            Assert.Equal(500, result.Summary.AttemptCount);
            Assert.Equal(500, handler.Calls.Count);
            Assert.All(handler.Calls.Values, count => Assert.Equal(1, count));
            Assert.Equal(500, handler.IdempotencyKeys.Distinct().Count());
        }
        finally
        {
            SqliteProcessingRepositoryTests.DeleteTemporaryDirectory(directory);
        }
    }

    private static CatalogueProcessingJob CreateOrderedJob(
        int ordinal,
        ProcessingRunId runId,
        AssetRevisionId revisionId,
        DateTimeOffset availableAtUtc) =>
        new(
            ProcessingJobId.From(Guid.Parse($"00000000-0000-0000-0000-{ordinal:000000000000}")),
            runId,
            revisionId,
            ProcessingJobStatus.Queued,
            0,
            availableAtUtc);

    private sealed class RecordingHandler : IProcessingJobHandler
    {
        private readonly AssetRevisionId? _interruptRevision;
        private readonly CancellationTokenSource? _cancellation;

        public RecordingHandler(
            AssetRevisionId? interruptRevision = null,
            CancellationTokenSource? cancellation = null)
        {
            _interruptRevision = interruptRevision;
            _cancellation = cancellation;
        }

        public Dictionary<AssetRevisionId, int> Calls { get; } = [];
        public List<string> IdempotencyKeys { get; } = [];

        public async Task ProcessAsync(
            ProcessingJobContext context,
            IProcessingCheckpointWriter checkpointWriter,
            CancellationToken cancellationToken)
        {
            Calls[context.AssetRevisionId] = Calls.GetValueOrDefault(context.AssetRevisionId) + 1;
            IdempotencyKeys.Add(context.IdempotencyKey);
            if (_interruptRevision == context.AssetRevisionId &&
                Calls[context.AssetRevisionId] == 1 &&
                _cancellation is not null)
            {
                await checkpointWriter.WriteAsync("""{"stage":"halfway"}""", cancellationToken);
                _cancellation.Cancel();
                throw new OperationCanceledException(_cancellation.Token);
            }
        }
    }

    private sealed class ClassifiedFailureHandler : IProcessingJobHandler
    {
        private readonly AssetRevisionId _transientRevision;
        private readonly AssetRevisionId _permanentRevision;

        public ClassifiedFailureHandler(
            AssetRevisionId transientRevision,
            AssetRevisionId permanentRevision)
        {
            _transientRevision = transientRevision;
            _permanentRevision = permanentRevision;
        }

        public Dictionary<AssetRevisionId, int> Calls { get; } = [];

        public Task ProcessAsync(
            ProcessingJobContext context,
            IProcessingCheckpointWriter checkpointWriter,
            CancellationToken cancellationToken)
        {
            _ = checkpointWriter;
            _ = cancellationToken;
            Calls[context.AssetRevisionId] = Calls.GetValueOrDefault(context.AssetRevisionId) + 1;
            if (context.AssetRevisionId == _transientRevision && context.Attempt == 1)
            {
                throw new ProcessingJobFailureException(
                    ProcessingFailureKind.Transient,
                    "temporary file lock");
            }

            if (context.AssetRevisionId == _permanentRevision)
            {
                throw new ProcessingJobFailureException(
                    ProcessingFailureKind.Permanent,
                    "corrupt media");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
