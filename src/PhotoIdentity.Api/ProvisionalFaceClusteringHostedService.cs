using Microsoft.Extensions.Logging.Abstractions;
using PhotoIdentity.Core.Clustering;

namespace PhotoIdentity.Api;

/// <summary>
/// Advances durable provisional-cluster runs. A process interruption leaves the run active;
/// restart simply recomputes the same bounded immutable snapshot and publishes only after the
/// evidence version is revalidated.
/// </summary>
public sealed class ProvisionalFaceClusteringHostedService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ActiveDelay = TimeSpan.FromMilliseconds(100);
    private const string AutomaticActor = "system:provisional-cluster-refresh";

    private readonly IProvisionalFaceClusterRepository _repository;
    private readonly ProvisionalFaceDbscanClusterer _clusterer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProvisionalFaceClusteringHostedService> _logger;

    public ProvisionalFaceClusteringHostedService(
        IProvisionalFaceClusterRepository repository,
        ProvisionalFaceDbscanClusterer clusterer,
        TimeProvider timeProvider,
        ILogger<ProvisionalFaceClusteringHostedService>? logger = null)
    {
        _repository = repository;
        _clusterer = clusterer;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<ProvisionalFaceClusteringHostedService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked;
            try
            {
                worked = await AdvanceOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // No face identifiers, paths, cluster members, or embedding data are logged.
                _logger.LogError(
                    exception,
                    "Provisional face clustering cycle failed unexpectedly; durable run state will be retried.");
                worked = false;
            }

            await Task.Delay(worked ? ActiveDelay : IdleDelay, stoppingToken);
        }
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

            ProvisionalFaceClusterComputation computation = await _clusterer.ComputeAsync(
                faces,
                run.Policy,
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

            _logger.LogWarning(
                "Provisional clustering run failed; operator-visible run state contains the non-sensitive failure message.");
            return true;
        }
    }
}
