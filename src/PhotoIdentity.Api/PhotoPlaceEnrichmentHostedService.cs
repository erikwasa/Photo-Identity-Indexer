namespace PhotoIdentity.Api;

/// <summary>
/// Controls automatic GeoNames enrichment independently of browser requests and archive analysis.
/// A configured GeoNames username opts the application into automatic enrichment unless this
/// worker is explicitly disabled. Thirty seconds is the conservative default request interval;
/// an operator may explicitly choose a lower non-negative value.
/// </summary>
public sealed record GeoNamesAutomaticEnrichmentConfiguration
{
    public const int DefaultMinimumRequestIntervalMilliseconds = 30_000;
    public const int DefaultIdlePollIntervalMilliseconds = 5_000;

    public GeoNamesAutomaticEnrichmentConfiguration(
        bool? enabled,
        int? minimumRequestIntervalMilliseconds,
        int? idlePollIntervalMilliseconds)
    {
        Enabled = enabled ?? true;

        MinimumRequestIntervalMilliseconds = minimumRequestIntervalMilliseconds
            ?? DefaultMinimumRequestIntervalMilliseconds;
        if (MinimumRequestIntervalMilliseconds is < 0 or > 600_000)
        {
            throw new InvalidOperationException(
                "GeoNames automatic enrichment request pacing must be between 0 and 600000 milliseconds.");
        }

        IdlePollIntervalMilliseconds = idlePollIntervalMilliseconds
            ?? DefaultIdlePollIntervalMilliseconds;
        if (IdlePollIntervalMilliseconds is < 1_000 or > 600_000)
        {
            throw new InvalidOperationException(
                "GeoNames automatic enrichment idle polling must be between 1000 and 600000 milliseconds.");
        }
    }

    public bool Enabled { get; }

    public int MinimumRequestIntervalMilliseconds { get; }

    public int IdlePollIntervalMilliseconds { get; }
}

public sealed record PhotoPlaceEnrichmentWorkerSnapshot(
    string State,
    string Message,
    DateTimeOffset? LastActivityAtUtc,
    DateTimeOffset? NextAttemptAtUtc);

public sealed class PhotoPlaceEnrichmentWorkerState
{
    private readonly object _gate = new();
    private PhotoPlaceEnrichmentWorkerSnapshot _snapshot = new(
        "starting",
        "Automatic place enrichment is starting.",
        LastActivityAtUtc: null,
        NextAttemptAtUtc: null);

    public PhotoPlaceEnrichmentWorkerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public void Update(
        string state,
        string message,
        DateTimeOffset? lastActivityAtUtc,
        DateTimeOffset? nextAttemptAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        lock (_gate)
        {
            _snapshot = new PhotoPlaceEnrichmentWorkerSnapshot(
                state,
                message,
                lastActivityAtUtc,
                nextAttemptAtUtc);
        }
    }
}

public sealed record PhotoPlaceEnrichmentWorkerCycleResult(
    TimeSpan Delay,
    PhotoPlaceEnrichmentReport? Report = null);

/// <summary>
/// Continuously drains the existing persisted-GPS enrichment queue one revision at a time.
/// The queue is durable behind the enrichment-state persistence boundary: successful/no-result/manual-protected revisions are
/// terminal for the current provider contract, while failed/deferred/unattempted revisions remain
/// eligible. Newly persisted GPS metadata therefore becomes eligible without an archive-specific hook.
/// </summary>
public sealed class PhotoPlaceEnrichmentHostedService : BackgroundService
{
    private static readonly TimeSpan FastContinuationDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultFailureDelay = TimeSpan.FromMinutes(5);

    private readonly GeoNamesReverseGeocodingConfiguration _geoNames;
    private readonly GeoNamesAutomaticEnrichmentConfiguration _automatic;
    private readonly PhotoPlaceEnrichmentService _service;
    private readonly PhotoPlaceEnrichmentWorkerState _state;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PhotoPlaceEnrichmentHostedService> _logger;

    public PhotoPlaceEnrichmentHostedService(
        GeoNamesReverseGeocodingConfiguration geoNames,
        GeoNamesAutomaticEnrichmentConfiguration automatic,
        PhotoPlaceEnrichmentService service,
        PhotoPlaceEnrichmentWorkerState state,
        TimeProvider timeProvider,
        ILogger<PhotoPlaceEnrichmentHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(geoNames);
        ArgumentNullException.ThrowIfNull(automatic);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _geoNames = geoNames;
        _automatic = automatic;
        _service = service;
        _state = state;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public int EffectiveMinimumRequestIntervalMilliseconds => Math.Max(
        _automatic.MinimumRequestIntervalMilliseconds,
        _geoNames.MinimumRequestIntervalMilliseconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            PhotoPlaceEnrichmentWorkerCycleResult cycle;
            try
            {
                cycle = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                _logger.LogError(exception, "Automatic GeoNames place enrichment failed unexpectedly.");
                _state.Update(
                    "waiting",
                    "Automatic place enrichment hit an unexpected local error and will retry.",
                    now,
                    now.Add(DefaultFailureDelay));
                cycle = new PhotoPlaceEnrichmentWorkerCycleResult(DefaultFailureDelay);
            }

            if (cycle.Delay > TimeSpan.Zero)
            {
                await Task.Delay(cycle.Delay, _timeProvider, stoppingToken);
            }
        }
    }

    public async Task<PhotoPlaceEnrichmentWorkerCycleResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan idleDelay = TimeSpan.FromMilliseconds(_automatic.IdlePollIntervalMilliseconds);

        if (!_automatic.Enabled)
        {
            _state.Update(
                "disabled",
                "Automatic place enrichment is disabled by local configuration.",
                now,
                now.Add(idleDelay));
            return new PhotoPlaceEnrichmentWorkerCycleResult(idleDelay);
        }

        if (!_geoNames.IsConfigured)
        {
            _state.Update(
                "disabled",
                "Automatic place enrichment is waiting for a configured GeoNames username.",
                now,
                now.Add(idleDelay));
            return new PhotoPlaceEnrichmentWorkerCycleResult(idleDelay);
        }

        PhotoPlaceEnrichmentReport report = await _service.ExecuteBatchAsync(
            limit: 1,
            refresh: false,
            cancellationToken);
        now = _timeProvider.GetUtcNow();

        if (report.Candidates == 0)
        {
            _state.Update(
                "idle",
                "No GPS photos are currently waiting for GeoNames enrichment.",
                now,
                now.Add(idleDelay));
            return new PhotoPlaceEnrichmentWorkerCycleResult(idleDelay, report);
        }

        if (report.StoppedEarly)
        {
            TimeSpan retryDelay = GetProviderBackoff(report.StopReasonCode);
            string message = report.StopReasonMessage
                ?? "GeoNames enrichment paused and will retry automatically.";
            _state.Update(
                "waiting",
                message,
                now,
                now.Add(retryDelay));
            return new PhotoPlaceEnrichmentWorkerCycleResult(retryDelay, report);
        }

        if (report.ProviderRequests > 0)
        {
            TimeSpan providerDelay = TimeSpan.FromMilliseconds(
                EffectiveMinimumRequestIntervalMilliseconds);
            _state.Update(
                "running",
                BuildProgressMessage(report),
                now,
                now.Add(providerDelay));
            return new PhotoPlaceEnrichmentWorkerCycleResult(providerDelay, report);
        }

        _state.Update(
            "running",
            BuildProgressMessage(report),
            now,
            now.Add(FastContinuationDelay));
        return new PhotoPlaceEnrichmentWorkerCycleResult(FastContinuationDelay, report);
    }

    private static string BuildProgressMessage(PhotoPlaceEnrichmentReport report)
    {
        if (report.Assigned > 0)
        {
            return "Automatic place enrichment assigned a GeoNames location and will continue in the background.";
        }

        if (report.NoResult > 0)
        {
            return "GeoNames found no nearby populated place for the processed GPS photo; the worker will continue.";
        }

        if (report.SkippedManual > 0)
        {
            return "A manually controlled place was protected; automatic enrichment will continue with other GPS photos.";
        }

        if (report.SkippedConflict > 0)
        {
            return "A place migration conflict was protected; automatic enrichment will continue with other GPS photos.";
        }

        if (report.CachedResults > 0)
        {
            return "Automatic place enrichment reused a cached coordinate result and will continue.";
        }

        return "Automatic place enrichment processed an eligible GPS photo and will continue.";
    }

    private static TimeSpan GetProviderBackoff(string? code) => code switch
    {
        "18" => TimeSpan.FromHours(1),
        "19" => TimeSpan.FromMinutes(15),
        "20" => TimeSpan.FromHours(6),
        "13" or "22" or "transport" => TimeSpan.FromMinutes(2),
        "10" => TimeSpan.FromMinutes(30),
        _ => DefaultFailureDelay,
    };
}
