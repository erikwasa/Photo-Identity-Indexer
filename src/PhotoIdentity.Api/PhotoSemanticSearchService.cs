using System.Collections.Concurrent;
using System.Security.Cryptography;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.Semantic;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record PhotoSemanticSearchConfiguration(string ModelDirectory)
{
    public const string DefaultModelFolderName = "clip-vit-base-patch32-b318363";

    public string ModelPath => Path.Combine(ModelDirectory, "model.onnx");
    public string TokenizerVocabularyPath => Path.Combine(ModelDirectory, "vocab.json");
    public string TokenizerMergesPath => Path.Combine(ModelDirectory, "merges.txt");

    public bool AssetsExist =>
        File.Exists(ModelPath) &&
        File.Exists(TokenizerVocabularyPath) &&
        File.Exists(TokenizerMergesPath);
}

public sealed record PhotoSemanticSearchModelDescriptor(
    string ModelId,
    string ModelSha256,
    string PreprocessingVersion,
    string VectorEncoding);

/// <summary>
/// Owns one local CLIP session. No model is downloaded implicitly: semantic indexing becomes
/// available only when the pinned model/tokenizer assets already exist on this machine.
/// </summary>
public sealed class PhotoSemanticSearchModel : IDisposable
{
    private readonly PhotoSemanticSearchConfiguration _configuration;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private ClipEmbeddingEncoder? _encoder;
    private PhotoSemanticSearchModelDescriptor? _descriptor;
    private bool _initializationAttempted;
    private bool _disposed;

    public PhotoSemanticSearchModel(PhotoSemanticSearchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public string ModelDirectory => _configuration.ModelDirectory;

    public async Task<PhotoSemanticSearchModelDescriptor?> GetDescriptorAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initializationAttempted)
        {
            return _descriptor;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initializationAttempted)
            {
                return _descriptor;
            }

            _initializationAttempted = true;
            if (!_configuration.AssetsExist)
            {
                return null;
            }

            string modelSha256 = await ComputeSha256Async(
                _configuration.ModelPath,
                cancellationToken);
            _encoder = new ClipEmbeddingEncoder(
                _configuration.ModelPath,
                _configuration.TokenizerVocabularyPath,
                _configuration.TokenizerMergesPath);
            _descriptor = new PhotoSemanticSearchModelDescriptor(
                ClipEmbeddingEncoder.ModelId,
                modelSha256,
                ClipZeroShotTagger.ImagePreprocessingVersion,
                PhotoEmbeddingEvidence.Float32L2NormalizedEncoding);
            return _descriptor;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<ClipEmbeddingResult?> EncodeTextAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (await GetDescriptorAsync(cancellationToken) is null)
        {
            return null;
        }

        await _inferenceGate.WaitAsync(cancellationToken);
        try
        {
            return _encoder!.EncodeText(query.Trim(), cancellationToken);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public async Task<(PhotoEmbeddingEvidence Evidence, double GenerationMilliseconds)?> EncodeImageAsync(
        AssetRevisionId revisionId,
        ImageFrame image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        PhotoSemanticSearchModelDescriptor? descriptor =
            await GetDescriptorAsync(cancellationToken);
        if (descriptor is null)
        {
            return null;
        }

        await _inferenceGate.WaitAsync(cancellationToken);
        try
        {
            ClipEmbeddingResult encoded = _encoder!.EncodeImage(image, cancellationToken);
            return (
                new PhotoEmbeddingEvidence(
                    revisionId,
                    descriptor.ModelId,
                    descriptor.ModelSha256,
                    descriptor.PreprocessingVersion,
                    encoded.Vector),
                encoded.TotalDuration.TotalMilliseconds);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _encoder?.Dispose();
        _initializationGate.Dispose();
        _inferenceGate.Dispose();
        _disposed = true;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}

public sealed record PhotoSearchExecutionItem(
    AssetRevisionId RevisionId,
    double CombinedScore,
    double? SemanticScore,
    double? CaptionScore,
    string? CaptionLanguage,
    string? Caption,
    IReadOnlyList<string> Sources);

public sealed record PhotoSearchExecutionResult(
    string Query,
    string Mode,
    bool SemanticModelAvailable,
    int IndexedPhotoCount,
    int DisplayableCaptionCount,
    double SearchMilliseconds,
    IReadOnlyList<PhotoSearchExecutionItem> Items);

public sealed record PhotoSearchStatus(
    bool SemanticModelAvailable,
    string ModelId,
    string? ModelSha256,
    string ModelDirectory,
    int CurrentPhotoCount,
    int IndexedPhotoCount,
    int DisplayableCaptionCount,
    int? EmbeddingDimensions,
    long RawEmbeddingBytes,
    double? AverageEmbeddingGenerationMilliseconds,
    bool ExactVectorSearch,
    bool AnnIndexUsed);

public sealed class PhotoSearchService
{
    private readonly IPhotoSearchRepository _repository;
    private readonly PhotoSemanticSearchModel _model;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ConcurrentDictionary<AssetRevisionId, PhotoEmbeddingEvidence> _embeddings = new();
    private volatile bool _embeddingsLoaded;
    private string? _loadedFingerprint;

    public PhotoSearchService(
        IPhotoSearchRepository repository,
        PhotoSemanticSearchModel model)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(model);
        _repository = repository;
        _model = model;
    }

    public async Task<PhotoSearchExecutionResult> SearchAsync(
        string query,
        string? mode,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        string normalizedQuery = query.Trim();
        if (normalizedQuery.Length > 300)
        {
            throw new ArgumentException("Photo search query cannot exceed 300 characters.", nameof(query));
        }
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Photo search limit must be between 1 and 200.");
        }

        string normalizedMode = PhotoSearchModes.Normalize(mode);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        PhotoSemanticSearchModelDescriptor? descriptor =
            await _model.GetDescriptorAsync(cancellationToken);

        IReadOnlyList<PhotoSearchSemanticHit> semanticHits = [];
        if (normalizedMode is not PhotoSearchModes.Caption && descriptor is not null)
        {
            await EnsureEmbeddingsLoadedAsync(descriptor, cancellationToken);
            ClipEmbeddingResult? queryEmbedding =
                await _model.EncodeTextAsync(normalizedQuery, cancellationToken);
            if (queryEmbedding is not null && _embeddings.Count > 0)
            {
                semanticHits = FindSemanticHits(
                    queryEmbedding.Vector,
                    Math.Min(1000, Math.Max(limit * 4, 100)));
            }
        }

        IReadOnlyList<PhotoSearchCaptionHit> captionHits =
            normalizedMode is PhotoSearchModes.Semantic
                ? []
                : await _repository.SearchCaptionsAsync(
                    normalizedQuery,
                    Math.Min(1000, Math.Max(limit * 4, 100)),
                    cancellationToken);

        IReadOnlyList<PhotoSearchRankedHit> ranked = PhotoSearchRanker.Fuse(
            semanticHits,
            captionHits,
            normalizedMode,
            limit);

        PhotoSearchStorageStatistics statistics = descriptor is null
            ? new PhotoSearchStorageStatistics(0, 0, 0, null, 0, null)
            : await _repository.GetStatisticsAsync(
                descriptor.ModelId,
                descriptor.ModelSha256,
                descriptor.PreprocessingVersion,
                descriptor.VectorEncoding,
                cancellationToken);

        double elapsed = System.Diagnostics.Stopwatch
            .GetElapsedTime(started)
            .TotalMilliseconds;
        return new PhotoSearchExecutionResult(
            normalizedQuery,
            normalizedMode,
            descriptor is not null,
            statistics.EmbeddingCount,
            statistics.DisplayableCaptionCount,
            elapsed,
            ranked.Select(hit => new PhotoSearchExecutionItem(
                hit.RevisionId,
                hit.CombinedScore,
                hit.SemanticScore,
                hit.CaptionScore,
                hit.CaptionLanguage,
                hit.Caption,
                hit.Sources)).ToArray());
    }

    public async Task<PhotoSearchStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        PhotoSemanticSearchModelDescriptor? descriptor =
            await _model.GetDescriptorAsync(cancellationToken);
        if (descriptor is null)
        {
            return new PhotoSearchStatus(
                SemanticModelAvailable: false,
                ClipEmbeddingEncoder.ModelId,
                ModelSha256: null,
                _model.ModelDirectory,
                CurrentPhotoCount: 0,
                IndexedPhotoCount: 0,
                DisplayableCaptionCount: 0,
                EmbeddingDimensions: null,
                RawEmbeddingBytes: 0,
                AverageEmbeddingGenerationMilliseconds: null,
                ExactVectorSearch: true,
                AnnIndexUsed: false);
        }

        PhotoSearchStorageStatistics statistics = await _repository.GetStatisticsAsync(
            descriptor.ModelId,
            descriptor.ModelSha256,
            descriptor.PreprocessingVersion,
            descriptor.VectorEncoding,
            cancellationToken);
        return new PhotoSearchStatus(
            SemanticModelAvailable: true,
            descriptor.ModelId,
            descriptor.ModelSha256,
            _model.ModelDirectory,
            statistics.CurrentPhotoCount,
            statistics.EmbeddingCount,
            statistics.DisplayableCaptionCount,
            statistics.EmbeddingDimensions,
            statistics.RawEmbeddingBytes,
            statistics.AverageEmbeddingGenerationMilliseconds,
            ExactVectorSearch: true,
            AnnIndexUsed: false);
    }

    public void AddEmbedding(PhotoEmbeddingEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!_embeddingsLoaded)
        {
            return;
        }
        _embeddings[evidence.RevisionId] = evidence;
    }

    private async Task EnsureEmbeddingsLoadedAsync(
        PhotoSemanticSearchModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        string fingerprint = string.Join(
            '|',
            descriptor.ModelId,
            descriptor.ModelSha256,
            descriptor.PreprocessingVersion,
            descriptor.VectorEncoding);
        if (_embeddingsLoaded && string.Equals(_loadedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (_embeddingsLoaded && string.Equals(_loadedFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            IReadOnlyList<PhotoSearchEmbeddingRecord> persisted =
                await _repository.GetCurrentEmbeddingsAsync(
                    descriptor.ModelId,
                    descriptor.ModelSha256,
                    descriptor.PreprocessingVersion,
                    descriptor.VectorEncoding,
                    cancellationToken);
            _embeddings.Clear();
            foreach (PhotoSearchEmbeddingRecord record in persisted)
            {
                _embeddings[record.Evidence.RevisionId] = record.Evidence;
            }
            _loadedFingerprint = fingerprint;
            _embeddingsLoaded = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private IReadOnlyList<PhotoSearchSemanticHit> FindSemanticHits(
        IReadOnlyList<float> queryEmbedding,
        int candidateCount)
    {
        PriorityQueue<PhotoSearchSemanticHit, double> queue = new();
        foreach ((AssetRevisionId revisionId, PhotoEmbeddingEvidence evidence) in _embeddings)
        {
            if (evidence.Dimensions != queryEmbedding.Count)
            {
                continue;
            }

            double score = PhotoEmbeddingSimilarity.Cosine(queryEmbedding, evidence.Values);
            PhotoSearchSemanticHit hit = new(revisionId, score);
            if (queue.Count < candidateCount)
            {
                queue.Enqueue(hit, score);
                continue;
            }

            if (queue.TryPeek(out _, out double lowest) && score > lowest)
            {
                queue.Dequeue();
                queue.Enqueue(hit, score);
            }
        }

        return queue.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.RevisionId.ToString(), StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed class PhotoSemanticEmbeddingHostedService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UnavailableDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromMinutes(5);

    private readonly IPhotoSearchRepository _repository;
    private readonly PhotoSemanticSearchModel _model;
    private readonly PhotoSearchService _search;
    private readonly CollectionReviewProxyFileResolver _proxyResolver;
    private readonly ReviewProxyServingConfiguration _proxyConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PhotoSemanticEmbeddingHostedService> _logger;
    private readonly OpenCvImageDecoder _decoder = new();

    public PhotoSemanticEmbeddingHostedService(
        IPhotoSearchRepository repository,
        PhotoSemanticSearchModel model,
        PhotoSearchService search,
        CollectionReviewProxyFileResolver proxyResolver,
        ReviewProxyServingConfiguration proxyConfiguration,
        TimeProvider timeProvider,
        ILogger<PhotoSemanticEmbeddingHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(proxyResolver);
        ArgumentNullException.ThrowIfNull(proxyConfiguration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _model = model;
        _search = search;
        _proxyResolver = proxyResolver;
        _proxyConfiguration = proxyConfiguration;
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
                _logger.LogWarning(exception, "Semantic photo indexing failed and will retry.");
                delay = FailureDelay;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, stoppingToken);
            }
        }
    }

    public async Task<TimeSpan> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        PhotoSemanticSearchModelDescriptor? descriptor =
            await _model.GetDescriptorAsync(cancellationToken);
        if (descriptor is null || !_proxyConfiguration.IsConfigured)
        {
            return UnavailableDelay;
        }

        IReadOnlyList<AssetRevisionId> candidates =
            await _repository.GetEmbeddingCandidatesAsync(
                _proxyConfiguration.ProfileId!,
                descriptor.ModelId,
                descriptor.ModelSha256,
                descriptor.PreprocessingVersion,
                descriptor.VectorEncoding,
                limit: 8,
                cancellationToken);
        if (candidates.Count == 0)
        {
            return IdleDelay;
        }

        foreach (AssetRevisionId candidate in candidates)
        {
            CollectionPhotoFile? proxy = await _proxyResolver.ResolveAsync(candidate, cancellationToken);
            if (proxy is null)
            {
                continue;
            }

            try
            {
                await using FileStream stream = new(
                    proxy.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                ImageFrame image = await _decoder.DecodeAsync(stream, cancellationToken);
                (PhotoEmbeddingEvidence Evidence, double GenerationMilliseconds)? encoded =
                    await _model.EncodeImageAsync(candidate, image, cancellationToken);
                if (encoded is null)
                {
                    return UnavailableDelay;
                }

                await _repository.SaveEmbeddingAsync(
                    encoded.Value.Evidence,
                    encoded.Value.GenerationMilliseconds,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                _search.AddEmbedding(encoded.Value.Evidence);
                return TimeSpan.FromMilliseconds(50);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ImageDecodingException)
            {
                _logger.LogDebug(
                    exception,
                    "Skipping unreadable semantic-search review proxy for revision {RevisionId}.",
                    candidate);
            }
        }

        return UnavailableDelay;
    }
}
