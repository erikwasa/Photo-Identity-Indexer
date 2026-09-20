using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record SlideshowCaptionRequest(string Language);

public sealed record SlideshowCaptionResponse(
    string RevisionId,
    string Language,
    string Status,
    string? Caption,
    bool Cached);

public interface ISlideshowCaptionService
{
    Task<SlideshowCaptionResponse> GetAsync(
        AssetRevisionId revisionId,
        string language,
        CancellationToken cancellationToken = default);

    Task<SlideshowCaptionResponse> RequestAsync(
        AssetRevisionId revisionId,
        string language,
        CancellationToken cancellationToken = default);
}

public sealed class SlideshowCaptionService : BackgroundService, ISlideshowCaptionService
{
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);

    private readonly CollectionReviewProxyFileResolver _proxyResolver;
    private readonly LocalSlideshowCaptionGenerator _generator;
    private readonly SlideshowCaptionGenerationConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SlideshowCaptionService> _logger;
    private readonly Channel<CaptionWorkItem> _queue;
    private readonly ConcurrentDictionary<string, byte> _pending =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failures =
        new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SlideshowCaptionService(
        CollectionReviewProxyFileResolver proxyResolver,
        LocalSlideshowCaptionGenerator generator,
        SlideshowCaptionGenerationConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<SlideshowCaptionService> logger)
    {
        ArgumentNullException.ThrowIfNull(proxyResolver);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _proxyResolver = proxyResolver;
        _generator = generator;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
        _queue = Channel.CreateBounded<CaptionWorkItem>(
            new BoundedChannelOptions(configuration.QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
    }

    public async Task<SlideshowCaptionResponse> GetAsync(
        AssetRevisionId revisionId,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (!SlideshowCaptionLanguage.TryNormalize(language, out string normalizedLanguage))
        {
            throw new ArgumentException("Caption language must be 'sv' or 'en'.", nameof(language));
        }

        string key = Key(revisionId, normalizedLanguage);
        CaptionCacheEntry? cached = await ReadCacheAsync(
            revisionId,
            normalizedLanguage,
            cancellationToken);
        if (cached is not null)
        {
            return ToResponse(cached, cached: true);
        }

        if (_pending.ContainsKey(key))
        {
            return new(
                revisionId.ToString(),
                normalizedLanguage,
                "pending",
                null,
                Cached: false);
        }

        if (_failures.TryGetValue(key, out DateTimeOffset failedAt) &&
            _timeProvider.GetUtcNow() - failedAt < FailureCooldown)
        {
            return new(
                revisionId.ToString(),
                normalizedLanguage,
                "failed",
                null,
                Cached: false);
        }

        return new(
            revisionId.ToString(),
            normalizedLanguage,
            "missing",
            null,
            Cached: false);
    }

    public async Task<SlideshowCaptionResponse> RequestAsync(
        AssetRevisionId revisionId,
        string language,
        CancellationToken cancellationToken = default)
    {
        SlideshowCaptionResponse current =
            await GetAsync(revisionId, language, cancellationToken);
        if (current.Status is "available" or "blocked" or "pending" or "failed")
        {
            return current;
        }

        string key = Key(revisionId, current.Language);
        if (!_pending.TryAdd(key, 0))
        {
            return current with { Status = "pending" };
        }

        if (!_queue.Writer.TryWrite(new CaptionWorkItem(
                revisionId,
                current.Language,
                key)))
        {
            _pending.TryRemove(key, out _);
            return current with { Status = "queue-full" };
        }

        return current with { Status = "pending" };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (CaptionWorkItem item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await GenerateAndCacheAsync(item, stoppingToken);
                _failures.TryRemove(item.Key, out _);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _failures[item.Key] = _timeProvider.GetUtcNow();
                _logger.LogWarning(
                    exception,
                    "Local slideshow caption generation failed; the item will be eligible for retry after the cooldown.");
            }
            finally
            {
                _pending.TryRemove(item.Key, out _);
            }
        }
    }

    private async Task GenerateAndCacheAsync(
        CaptionWorkItem item,
        CancellationToken cancellationToken)
    {
        CollectionPhotoFile? proxy = await _proxyResolver.ResolveAsync(
            item.RevisionId,
            cancellationToken);
        if (proxy is null)
        {
            throw new InvalidOperationException(
                "No durable review proxy is available for local caption generation.");
        }

        LocalSlideshowCaption generated = await _generator.GenerateAsync(
            proxy.Path,
            item.Language,
            cancellationToken);
        IReadOnlyList<string> riskFlags =
            GeneratedCreativeTextGuard.Evaluate(generated.Content);

        string? acceptedContent = null;
        if (riskFlags.Count == 0)
        {
            GeneratedCreativeTextEvidence evidence = new(
                item.RevisionId,
                generated.Model,
                generated.ModelDigest,
                generated.PromptVersion,
                generated.Content,
                riskFlags);
            acceptedContent = evidence.Content;
        }

        CaptionCacheEntry entry = new(
            item.RevisionId.ToString(),
            item.Language,
            SlideshowCaptionGenerationConfiguration.GenerationVersion,
            generated.Model,
            generated.ModelDigest,
            generated.PromptVersion,
            ImageMode: "thumbnail-480x320",
            _configuration.ContextTokens,
            acceptedContent,
            riskFlags.ToArray(),
            generated.ClientMilliseconds,
            _timeProvider.GetUtcNow());

        await WriteCacheAsync(entry, cancellationToken);
    }

    private async Task<CaptionCacheEntry?> ReadCacheAsync(
        AssetRevisionId revisionId,
        string language,
        CancellationToken cancellationToken)
    {
        string path = CachePath(revisionId, language);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            CaptionCacheEntry? entry =
                await JsonSerializer.DeserializeAsync<CaptionCacheEntry>(
                    stream,
                    _jsonOptions,
                    cancellationToken);
            if (entry is null ||
                !string.Equals(entry.RevisionId, revisionId.ToString(), StringComparison.Ordinal) ||
                !string.Equals(entry.Language, language, StringComparison.Ordinal) ||
                !string.Equals(
                    entry.GenerationVersion,
                    SlideshowCaptionGenerationConfiguration.GenerationVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(entry.Model, _configuration.Model, StringComparison.Ordinal) ||
                !string.Equals(
                    entry.PromptVersion,
                    SlideshowCaptionPrompt.VersionFor(language),
                    StringComparison.Ordinal) ||
                !string.Equals(entry.ImageMode, "thumbnail-480x320", StringComparison.Ordinal) ||
                entry.ContextTokens != _configuration.ContextTokens)
            {
                return null;
            }

            return entry;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(
        CaptionCacheEntry entry,
        CancellationToken cancellationToken)
    {
        string path = CachePath(
            AssetRevisionId.From(Guid.Parse(entry.RevisionId)),
            entry.Language);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    entry,
                    _jsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch
            {
            }
        }
    }

    private SlideshowCaptionResponse ToResponse(
        CaptionCacheEntry entry,
        bool cached) =>
        new(
            entry.RevisionId,
            entry.Language,
            entry.RiskFlags.Length == 0 && !string.IsNullOrWhiteSpace(entry.Content)
                ? "available"
                : "blocked",
            entry.RiskFlags.Length == 0 ? entry.Content : null,
            cached);

    private string CachePath(AssetRevisionId revisionId, string language) =>
        Path.Combine(
            _configuration.CacheRoot,
            SlideshowCaptionGenerationConfiguration.GenerationVersion,
            language,
            revisionId + ".json");

    private static string Key(AssetRevisionId revisionId, string language) =>
        revisionId + "|" + language;

    private sealed record CaptionWorkItem(
        AssetRevisionId RevisionId,
        string Language,
        string Key);

    private sealed record CaptionCacheEntry(
        string RevisionId,
        string Language,
        string GenerationVersion,
        string Model,
        string ModelDigest,
        string PromptVersion,
        string ImageMode,
        int ContextTokens,
        string? Content,
        string[] RiskFlags,
        double ClientMilliseconds,
        DateTimeOffset GeneratedAtUtc);
}
