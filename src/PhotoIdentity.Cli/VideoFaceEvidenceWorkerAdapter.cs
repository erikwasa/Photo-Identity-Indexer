using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// A testable, explicitly selected (not hosted) native face producer contract.
/// Unlike FrameEvidence, FaceEvidence requires local YuNet/SFace models and
/// never invokes CLIP or caption inference.
/// </summary>
public interface IVideoFaceEmbeddingProducer
{
    Task<VideoFaceEmbeddingPublication?> ProduceAsync(
        VideoProcessingJobLease lease, CancellationToken token);
}

/// <summary>
/// Explicitly selected one-lease FaceEvidence adapter. Keeps the video lease
/// heartbeat and photo-priority safety probe active across BOTH native source
/// analysis and the full all-photo-exemplar scorer-to-PostgreSQL transaction.
/// No hosted registration, HTTP inference route or unattended processing.
/// The only invocation is the explicit selected one-video CLI command.
/// No photo identity or video-person assignments are made.
/// </summary>
public sealed class VideoFaceEvidenceWorkerAdapter(
    PostgresCatalogueDatabase database,
    IVideoFaceEmbeddingProducer producer)
    : IVideoPublishedDerivativeAdapter
{
    public string? RequiredDerivativeKind => VideoFaceEvidenceLeaseIdentity.Kind;

    public async Task<VideoDerivativeAttemptReport> ProcessAsync(
        VideoProcessingJobLease lease, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(producer);
        if (!VideoFaceEvidenceLeaseIdentity.Matches(
                lease.DerivativeKind, lease.PolicyVersion,
                lease.PresetVersion, lease.RequestedFrames))
            return new(false, false, new("adapter-report", "not-published"));

        var safety = new PostgresVideoLeaseSafetyProbe(database);
        var queue = new PostgresVideoProcessingJobRepository(database);
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task monitor = MonitorLeaseAsync(queue, safety, lease, work);
        try
        {
            VideoExecutionPermit initial = await safety.CheckAsync(lease, work.Token);
            if (!initial.Allowed)
                return new(false, false,
                    new("lease-safety", initial.ReasonCode));

            VideoFaceEmbeddingPublication? evidence;
            try
            {
                evidence = await producer.ProduceAsync(lease, work.Token);
            }
            catch (Exception error)
            {
                VideoAttemptFailureDiagnostics.Mark(error, "tool-execution");
                throw;
            }

            work.Token.ThrowIfCancellationRequested();
            if (evidence is null)
                return new(false, false,
                    new("output-validation", "no-faces-found"));
            if (evidence.RevisionId != lease.RevisionId ||
                evidence.Observations is not { Count: >= 0 and <= 128 } ||
                evidence.Observations.Any(face =>
                    face.FrameOrdinal < 0 || face.FrameOrdinal >= lease.RequestedFrames))
                return new(false, false,
                    new("output-validation", "invalid-media-evidence"));

            VideoExecutionPermit beforeCommit = await safety.CheckAsync(lease, work.Token);
            if (!beforeCommit.Allowed)
                return new(false, false,
                    new("lease-safety", beforeCommit.ReasonCode));

            Guid? published;
            try
            {
                var scorer = new PostgresIdentityMatchRegenerationScorer(database);
                var review = new PostgresVideoFaceReviewRepository(database, TimeProvider.System);
                var publisher = new PostgresVideoFaceMatchPublisher(scorer, review);
                published = await publisher.TryPublishLeasedAsync(lease, evidence, work.Token);
            }
            catch (Exception error)
            {
                VideoAttemptFailureDiagnostics.Mark(error, "publication");
                throw;
            }

            return new(published.HasValue, false,
                published.HasValue ? null : new("publication", "not-committed"));
        }
        finally
        {
            work.Cancel();
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
            // Cooperative cancellation on normal completion or lease loss.
        }
    }
}
