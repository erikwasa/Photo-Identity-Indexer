using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class IdentityMatchRegenerationHostedServiceResilienceTests
{
    [Fact]
    public async Task Hosted_worker_retries_after_unexpected_repository_failure_instead_of_faulting()
    {
        FailOnceIdentityMatchRegenerationRepository runs = new();
        IdentityMatchRegenerationHostedService worker = new(
            runs,
            scorer: null!,
            policies: null!,
            autoAssignment: null!,
            evidence: null!,
            TimeProvider.System,
            new ArchiveThroughputMetrics());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            while (Volatile.Read(ref runs.GetNextActiveCalls) < 2)
            {
                await Task.Delay(25, timeout.Token);
            }

            Assert.NotNull(worker.ExecuteTask);
            Assert.False(worker.ExecuteTask!.IsFaulted);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private sealed class FailOnceIdentityMatchRegenerationRepository : IIdentityMatchRegenerationRepository
    {
        public int GetNextActiveCalls;

        public Task<ReviewIdentityMatchRegenerationRun?> GetNextActiveAsync(
            CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref GetNextActiveCalls);
            if (call == 1)
            {
                throw new InvalidOperationException("Simulated transient catalogue outage.");
            }

            return Task.FromResult<ReviewIdentityMatchRegenerationRun?>(null);
        }

        public Task<ReviewIdentityMatchRegenerationRun> StartAsync(
            ModelId modelId,
            Sha256Digest modelHash,
            int policyVersion,
            string requestedBy,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReviewIdentityMatchRegenerationRun?> GetLatestAsync(
            ModelId modelId,
            Sha256Digest modelHash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReviewIdentityMatchRegenerationTarget?> ClaimNextTargetAsync(
            Guid runId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteTargetAsync(
            Guid runId,
            FaceOccurrenceId faceOccurrenceId,
            int suggestionCount,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task FailTargetAsync(
            Guid runId,
            FaceOccurrenceId faceOccurrenceId,
            string error,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteRunAsync(
            Guid runId,
            int automaticallyAssignedCount,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkFailedAsync(
            Guid runId,
            string error,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> EvidenceStillMatchesAsync(
            ReviewIdentityMatchRegenerationRun run,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
