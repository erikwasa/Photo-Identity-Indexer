using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// A future reviewed producer can use only an already-claimed FrameEvidence
/// lease and cooperative cancellation to supply a complete plan/batch.
/// No source paths, arbitrary tool arguments or cloud endpoint are accepted
/// by the publication adapter. No producer is registered in the runtime.
/// </summary>
public sealed record VideoFrameEvidenceProduction(
    VideoFrameSamplingPlan Plan, VideoFrameEvidenceBatch Batch);

public interface IVideoFrameEvidenceProducer
{
    Task<VideoFrameEvidenceProduction> ProduceAsync(
        VideoProcessingJobLease lease, CancellationToken token);
}

/// <summary>
/// WI-0190 checkpoint 5: unregistered, single-lease publishing adapter.
/// The future media producer must separately verify stage/hydration isolation,
/// Windows FFmpeg containment, model digests and per-frame bounds. This class
/// enforces the coordinator's job lease and safe durable publication boundary;
/// it does NOT authorize or implement production media execution.
/// </summary>
public sealed class VideoFrameEvidenceWorkerAdapter(
    PostgresCatalogueDatabase database, IVideoFrameEvidenceProducer producer)
    : IVideoPublishedDerivativeAdapter
{
    public string? RequiredDerivativeKind => "FrameEvidence";

    public async Task<VideoDerivativeAttemptReport> ProcessAsync(
        VideoProcessingJobLease lease, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.DerivativeKind != "FrameEvidence" ||
            lease.PolicyVersion != VideoFrameSamplingPlanner.PolicyVersion ||
            lease.PresetVersion != VideoFrameExtractionProfile.Version ||
            lease.RequestedFrames is < 1 or > 4)
            return new(false, false,
                new("adapter-report", "not-published"));

        var safety = new PostgresVideoLeaseSafetyProbe(database);
        var queue = new PostgresVideoProcessingJobRepository(database);
        using CancellationTokenSource work = CancellationTokenSource
            .CreateLinkedTokenSource(token);

        // Heartbeat renews the 60-second lease and revalidates control/source.
        // Every tick also checks photo priority and operator authorization.
        // Cancellation is shared with the producer; a noncooperative producer
        // still cannot publish after the lease/source/control is revoked.
        Task monitor = MonitorLeaseAsync(queue, safety, lease, work);
        try
        {
            VideoExecutionPermit start = await safety.CheckAsync(lease, work.Token);
            if (!start.Allowed)
                return new(false, false, new("lease-safety", start.ReasonCode));

            VideoFrameEvidenceProduction result;
            try
            {
                result = await producer.ProduceAsync(lease, work.Token);
            }
            catch (Exception error)
            {
                VideoAttemptFailureDiagnostics.Mark(error, "tool-execution");
                throw;
            }

            work.Token.ThrowIfCancellationRequested();
            VideoExecutionPermit beforeCommit = await safety.CheckAsync(lease, work.Token);
            if (!beforeCommit.Allowed)
                return new(false, false,
                    new("lease-safety", beforeCommit.ReasonCode));

            if (result is null || result.Plan is null || result.Batch is null ||
                result.Batch.Provenance is null ||
                result.Plan.PolicyVersion != lease.PolicyVersion ||
                result.Batch.Provenance.RevisionId != lease.RevisionId ||
                result.Plan.Samples.Count != lease.RequestedFrames)
                return new(false, false,
                    new("output-validation", "invalid-media-evidence"));

            VideoFrameEvidenceValidation validation = VideoFrameEvidenceContract.Validate(
                result.Plan, result.Batch, result.Batch.Provenance,
                CurrentRevision: true, Excluded: false, FullyCompleted: true);
            if (!validation.Accepted)
                return new(false, false,
                    new("output-validation", "invalid-media-evidence"));

            bool published;
            try
            {
                published = await new PostgresVideoFrameEvidenceRepository(database)
                    .TryPublishAsync(lease, result.Plan, result.Batch, work.Token);
            }
            catch (Exception error)
            {
                VideoAttemptFailureDiagnostics.Mark(error, "publication");
                throw;
            }
            return new(published, false,
                published ? null : new("publication", "not-committed"));
        }
        finally
        {
            work.Cancel();
            // Monitoring is always awaited; no heartbeat may outlive the
            // adapter and update a subsequently consumed/revoked lease.
            await monitor;
        }
    }

    private static async Task MonitorLeaseAsync(
        PostgresVideoProcessingJobRepository queue,
        PostgresVideoLeaseSafetyProbe safety,
        VideoProcessingJobLease lease, CancellationTokenSource work)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(work.Token))
            {
                bool safe;
                try
                {
                    safe = await queue.TryHeartbeatAsync(
                        lease.Id, lease.LeaseToken, 0, work.Token) &&
                        (await safety.CheckAsync(lease, work.Token)).Allowed;
                }
                catch (OperationCanceledException) when (work.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    safe = false;
                }
                if (!safe)
                {
                    work.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            // Normal shutdown after successful commit or failed attempt.
        }
    }
}
