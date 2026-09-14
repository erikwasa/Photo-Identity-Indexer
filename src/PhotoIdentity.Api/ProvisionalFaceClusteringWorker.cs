using Microsoft.Extensions.Logging.Abstractions;
using PhotoIdentity.Core.Clustering;

namespace PhotoIdentity.Api;

/// <summary>
/// Advances one durable provisional-cluster cycle. The API host scheduler invokes this only when
/// identity regeneration is idle, keeping expensive exact clustering lower priority than review
/// matching. A process interruption leaves the run active and restart recomputes the same bounded
/// snapshot before any replacement is published.
/// </summary>
public sealed class ProvisionalFaceClusteringWorker
{
    private const string AutomaticActor = "system:provisional-cluster-refresh";

    private readonly IProvisionalFaceClusterRepository _repository;
    private readonly ProvisionalFaceDbscanClusterer _clusterer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public ProvisionalFaceClusteringWorker(
        IProvisionalFaceClusterRepository repository,
        ProvisionalFaceDbscanClusterer clusterer,
        TimeProvider timeProvider,
        ILogger? logger = null)
    {
        _repository = repository;
        _clusterer = clusterer;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<bool> AdvanceOnceAsync(CancellationToken cancellationToken = default)
    {
        ProvisionalFaceClusterRun? run = await _repository.GetNextActiveAsync(cancellationToken);
        if (run is null)
        {
            ProvisionalFaceClusterRun? refresh = await _repository.TryStartNextRefreshAsync(
                AutomaticActor,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            return refresh is not null;
        }

        try
        {
            IReadOnlyList<ProvisionalFaceClusterInputFace> faces =
                await _repository.ReadInputSnapshotAsync(run, cancellationToken: cancellationToken);
            ProvisionalFaceClusterRun? latest = await _repository.GetLatestAsync(
                run.ModelId,
                run.ModelHash,
                run.Policy.Version,
                run.IncludeUnknown,
                cancellationToken);
            if (latest is null || latest.Id != run.Id || !latest.IsActive)
            {
                return true;
            }

            IReadOnlyList<ProvisionalFaceNotSameConstraint> notSameConstraints =
                await _repository.ReadNotSameConstraintsAsync(run, cancellationToken: cancellationToken);

            ProvisionalFaceClusterComputation computation = await _clusterer.ComputeAsync(
                faces,
                run.Policy,
                notSameConstraints,
                (processed, token) => _repository.ReportProgressAsync(
                    run.Id,
                    processed,
                    _timeProvider.GetUtcNow(),
                    token),
                cancellationToken);

            bool published = await _repository.CompleteAsync(
                run,
                computation,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            if (published)
            {
                _logger.LogInformation(
                    "Published provisional clustering run with {TargetCount} evaluated faces, {ClusterCount} clusters and {NoiseCount} noise faces.",
                    computation.EvaluatedFaceCount,
                    computation.ClusterCount,
                    computation.NoiseCount);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                await _repository.MarkFailedAsync(
                    run.Id,
                    exception.Message,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            catch (Exception persistenceException) when (persistenceException is not OperationCanceledException)
            {
                _logger.LogError(
                    persistenceException,
                    "Could not persist provisional clustering failure state; the durable run may be retried after restart.");
                throw;
            }

            // Do not log face IDs, cluster members, paths, embeddings, or personal labels.
            _logger.LogWarning(
                "Provisional clustering run failed; operator-visible run state contains the non-sensitive failure message.");
            return true;
        }
    }
}
