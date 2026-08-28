using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveAnalysisInspectionSessionTests
{
    [Fact]
    public async Task Same_exact_key_reuses_handler_and_changed_profile_recreates_it()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            ArchiveThroughputMetrics metrics = new();
            List<FakeHandler> created = [];

            using ArchiveAnalysisInspectionSession session = new(
                database,
                metrics,
                (configuration, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FakeHandler handler = new();
                    created.Add(handler);
                    return Task.FromResult<IProcessingJobHandler>(handler);
                });

            LocalBatchConfiguration configuration = CreateConfiguration(directory);
            Sha256Digest firstProfile = new(new string('a', 64));
            Sha256Digest secondProfile = new(new string('b', 64));

            IProcessingJobHandler first;
            using (ArchiveAnalysisInspectionSession.Lease lease =
                   await session.AcquireAsync(configuration, firstProfile))
            {
                first = lease.Handler;
            }

            using (ArchiveAnalysisInspectionSession.Lease lease =
                   await session.AcquireAsync(configuration, firstProfile))
            {
                Assert.Same(first, lease.Handler);
            }

            Assert.Single(created);
            Assert.False(created[0].Disposed);

            using (ArchiveAnalysisInspectionSession.Lease lease =
                   await session.AcquireAsync(configuration, secondProfile))
            {
                Assert.NotSame(first, lease.Handler);
            }

            Assert.Equal(2, created.Count);
            Assert.True(created[0].Disposed);
            Assert.False(created[1].Disposed);

            ArchiveThroughputCounterSnapshot reuse = Assert.Single(
                metrics.GetSnapshot().Counters,
                value => value.Name == ArchiveThroughputMetricNames.ModelSessionReuses);
            Assert.Equal(1, reuse.Value);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Concurrent_acquire_waits_until_the_active_lease_is_released()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            using ArchiveAnalysisInspectionSession session = new(
                database,
                metrics: null,
                (configuration, cancellationToken) =>
                    Task.FromResult<IProcessingJobHandler>(new FakeHandler()));

            LocalBatchConfiguration configuration = CreateConfiguration(directory);
            Sha256Digest profile = new(new string('c', 64));

            ArchiveAnalysisInspectionSession.Lease first =
                await session.AcquireAsync(configuration, profile);
            Task<ArchiveAnalysisInspectionSession.Lease> secondTask =
                session.AcquireAsync(configuration, profile);

            Assert.False(secondTask.IsCompleted);

            first.Dispose();
            using ArchiveAnalysisInspectionSession.Lease second =
                await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(second.Handler);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static LocalBatchConfiguration CreateConfiguration(string directory) =>
        new(
            Path.Combine(directory, "source"),
            Path.Combine(directory, "output"),
            Path.Combine(directory, "repository"),
            Path.Combine(directory, "models"));

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

    private sealed class FakeHandler : IProcessingJobHandler, IDisposable
    {
        public bool Disposed { get; private set; }

        public Task ProcessAsync(
            ProcessingJobContext context,
            IProcessingCheckpointWriter checkpointWriter,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }
}
