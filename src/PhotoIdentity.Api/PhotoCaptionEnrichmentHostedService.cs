using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record PhotoCaptionEnrichmentWorkerSnapshot(
    string State,
    string Message,
    DateTimeOffset? LastActivityAtUtc,
    DateTimeOffset? NextAttemptAtUtc);

public sealed class PhotoCaptionEnrichmentWorkerState
{
    private readonly object _gate = new();
    private PhotoCaptionEnrichmentWorkerSnapshot _snapshot = new(
        "starting",
        "Automatic photo caption enrichment is starting.",
        LastActivityAtUtc: null,
        NextAttemptAtUtc: null);

    public PhotoCaptionEnrichmentWorkerSnapshot GetSnapshot()
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
        lock (_gate)
        {
            _snapshot = new(
                state,
                message,
                lastActivityAtUtc,
                nextAttemptAtUtc);
        }
    }
}

/// <summary>
/// Drains caption enrichment independently of slideshows and Smart Collections.
/// Consumers may read generated caption evidence, but only this worker produces it.
/// </summary>
public sealed class PhotoCaptionEnrichmentHostedService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MissingProxyDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ContinueDelay = TimeSpan.FromMilliseconds(100);

    private readonly IPhotoCaptionRepository _captions;
    private readonly CollectionReviewProxyFileResolver _proxyResolver;
    private readonly ReviewProxyServingConfiguration _proxyConfiguration;
    private readonly LocalPhotoCaptionGenerator _generator;
    private readonly PhotoCaptionGenerationConfiguration _generation;
    private readonly PhotoCaptionEnrichmentWorkerState _state;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PhotoCaptionEnrichmentHostedService> _logger;

    public PhotoCaptionEnrichmentHostedService(
        IPhotoCaptionRepository captions,
        CollectionReviewProxyFileResolver proxyResolver,
        ReviewProxyServingConfiguration proxyConfiguration,
        LocalPhotoCaptionGenerator generator,
        PhotoCaptionGenerationConfiguration generation,
        PhotoCaptionEnrichmentWorkerState state,
        TimeProvider timeProvider,
        ILogger<PhotoCaptionEnrichmentHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(captions);
        ArgumentNullException.ThrowIfNull(proxyResolver);
        ArgumentNullException.ThrowIfNull(proxyConfiguration);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _captions = captions;
        _proxyResolver = proxyResolver;
        _proxyConfiguration = proxyConfiguration;
        _generator = generator;
        _generation = generation;
        _state = state;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                _logger.LogWarning(
                    exception,
                    "Automatic photo caption enrichment failed and will retry.");
                _state.Update(
                    "waiting",
                    "Caption enrichment hit a local model or storage error and will retry.",
                    now,
                    now.Add(FailureDelay));
                delay = FailureDelay;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, stoppingToken);
            }
        }
    }

    public async Task<TimeSpan> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        PhotoCaptionEnrichmentSettings settings =
            await _captions.GetSettingsAsync(cancellationToken);

        if (!settings.Enabled)
        {
            _state.Update(
                "disabled",
                "Automatic photo caption enrichment is disabled in Settings.",
                now,
                now.Add(IdleDelay));
            return IdleDelay;
        }

        if (!_proxyConfiguration.IsConfigured)
        {
            _state.Update(
                "waiting",
                "Caption enrichment is enabled but durable review proxies are not configured.",
                now,
                now.Add(MissingProxyDelay));
            return MissingProxyDelay;
        }

        string language = PhotoCaptionLanguages.Normalize(settings.Language);
        string promptVersion = PhotoCaptionPrompt.VersionFor(language);
        LocalPhotoCaptionModel model =
            await _generator.GetInstalledModelAsync(cancellationToken);
        IReadOnlyList<AssetRevisionId> candidates =
            await _captions.GetCandidatesAsync(
                _proxyConfiguration.ProfileId!,
                language,
                PhotoCaptionGenerationConfiguration.GenerationVersion,
                model.Name,
                model.Digest,
                promptVersion,
                PhotoCaptionGenerationConfiguration.ImageMode,
                _generation.ContextTokens,
                limit: 8,
                cancellationToken);

        if (candidates.Count == 0)
        {
            _state.Update(
                "idle",
                $"No current photos are waiting for {LanguageLabel(language)} caption enrichment.",
                now,
                now.Add(IdleDelay));
            return IdleDelay;
        }

        AssetRevisionId? selectedRevision = null;
        CollectionPhotoFile? proxy = null;
        foreach (AssetRevisionId candidate in candidates)
        {
            proxy = await _proxyResolver.ResolveAsync(candidate, cancellationToken);
            if (proxy is not null)
            {
                selectedRevision = candidate;
                break;
            }
        }

        if (selectedRevision is null || proxy is null)
        {
            _state.Update(
                "waiting",
                "Caption candidates exist but their configured review proxy files are not currently available.",
                now,
                now.Add(MissingProxyDelay));
            return MissingProxyDelay;
        }

        LocalPhotoCaption generated = await _generator.GenerateAsync(
            proxy.Path,
            language,
            model,
            cancellationToken);
        IReadOnlyList<string> riskFlags =
            GeneratedCreativeTextGuard.Evaluate(generated.Content);

        GeneratedCreativeTextEvidence evidence = new(
            selectedRevision.Value,
            generated.Model,
            generated.ModelDigest,
            generated.PromptVersion,
            generated.Content,
            riskFlags);

        now = _timeProvider.GetUtcNow();
        await _captions.SaveAsync(
            new PhotoGeneratedCaption(
                selectedRevision.Value,
                language,
                PhotoCaptionGenerationConfiguration.GenerationVersion,
                generated.Model,
                generated.ModelDigest,
                generated.PromptVersion,
                PhotoCaptionGenerationConfiguration.ImageMode,
                _generation.ContextTokens,
                evidence.Content,
                evidence.RiskFlags,
                generated.ClientMilliseconds,
                now),
            cancellationToken);

        _state.Update(
            "running",
            riskFlags.Count == 0
                ? $"Generated and stored one {LanguageLabel(language)} photo caption."
                : "Generated caption evidence was stored as blocked because the claim guard flagged it.",
            now,
            now.Add(ContinueDelay));
        return ContinueDelay;
    }

    private static string LanguageLabel(string language) =>
        language == PhotoCaptionLanguages.Swedish ? "Swedish" : "English";
}
