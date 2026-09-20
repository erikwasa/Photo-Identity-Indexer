using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

public sealed record SlideshowCaptionGenerationConfiguration(
    string CacheRoot,
    Uri OllamaBaseUri,
    string Model,
    int ContextTokens,
    int TimeoutSeconds,
    int QueueCapacity)
{
    public const string GenerationVersion = "wi-0157-slideshow-caption-v1";
    public const string DefaultModel = "qwen2.5vl:3b";
    public const int DefaultContextTokens = 1024;
    public const int DefaultTimeoutSeconds = 600;
    public const int DefaultQueueCapacity = 8;
    public static readonly Uri DefaultOllamaBaseUri = new("http://127.0.0.1:11434/");
}

public sealed record LocalSlideshowCaption(
    string Content,
    string Model,
    string ModelDigest,
    string PromptVersion,
    double ClientMilliseconds);

public static class SlideshowCaptionLanguage
{
    public const string Swedish = "sv";
    public const string English = "en";

    public static bool TryNormalize(string? value, out string language)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? Swedish;
        if (normalized is Swedish or English)
        {
            language = normalized;
            return true;
        }

        language = string.Empty;
        return false;
    }
}

public static class SlideshowCaptionPrompt
{
    public static string VersionFor(string language) =>
        language == SlideshowCaptionLanguage.Swedish
            ? "wi-0157-neutral-visible-caption-sv-v1"
            : "wi-0157-neutral-visible-caption-en-v1";

    public static string TextFor(string language) =>
        language == SlideshowCaptionLanguage.Swedish
            ? "Beskriv det här privata familjefotot. Skriv exakt en kort neutral mening på högst 20 ord på svenska som endast beskriver direkt synliga personer, föremål, miljö och handlingar. Identifiera eller gissa inte namn, relationer, ålder, yrke, nationalitet eller känslor. Ange eller härled inte exakta platser, datum, årtal, helgdagar, händelser, ceremonier, firanden eller tillfällen. Härled inte händelsetyp som bröllop, födelsedag, konsert, fest, examen eller festival. Om något är osäkert, utelämna det. Returnera endast meningen."
            : "Caption this private family photo. Write exactly one short neutral sentence of at most 20 words describing only directly visible people, objects, setting, and actions. Do not identify or guess any person's name, relationship, age, occupation, nationality, or emotion. Do not name or infer exact locations, dates, years, holidays, events, ceremonies, celebrations, or occasions. Do not infer an event type such as wedding, birthday, concert, party, graduation, or festival. If something is uncertain, omit it. Return only the sentence.";
}

public sealed class LocalSlideshowCaptionGenerator
{
    private const string HttpClientName = "LocalSlideshowCaption";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenCvThumbnailRenderer _thumbnailRenderer;
    private readonly SlideshowCaptionGenerationConfiguration _configuration;

    public LocalSlideshowCaptionGenerator(
        IHttpClientFactory httpClientFactory,
        OpenCvThumbnailRenderer thumbnailRenderer,
        SlideshowCaptionGenerationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(thumbnailRenderer);
        ArgumentNullException.ThrowIfNull(configuration);
        _httpClientFactory = httpClientFactory;
        _thumbnailRenderer = thumbnailRenderer;
        _configuration = configuration;
    }

    public async Task<LocalSlideshowCaption> GenerateAsync(
        string proxyPath,
        string language,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyPath);
        if (!SlideshowCaptionLanguage.TryNormalize(language, out string normalizedLanguage))
        {
            throw new ArgumentException("Caption language must be 'sv' or 'en'.", nameof(language));
        }

        EncodedThumbnail? thumbnail =
            await _thumbnailRenderer.RenderAsync(proxyPath, cancellationToken);
        if (thumbnail is null)
        {
            throw new InvalidDataException("The review proxy could not be rendered as a caption thumbnail.");
        }

        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        LocalVisionModelDescriptor model =
            await GetInstalledModelAsync(http, cancellationToken);

        object request = new
        {
            model = _configuration.Model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = SlideshowCaptionPrompt.TextFor(normalizedLanguage),
                    images = new[] { Convert.ToBase64String(thumbnail.Content) },
                },
            },
            stream = false,
            keep_alive = "30m",
            options = new
            {
                temperature = 0,
                seed = 0,
                num_predict = 80,
                num_ctx = _configuration.ContextTokens,
            },
        };

        long started = Stopwatch.GetTimestamp();
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            new Uri(_configuration.OllamaBaseUri, "api/chat"),
            request,
            SerializerOptions,
            cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        double elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Local Ollama caption request failed with HTTP {(int)response.StatusCode}.");
        }

        OllamaChatResponse chat = JsonSerializer.Deserialize<OllamaChatResponse>(
            payload,
            SerializerOptions)
            ?? throw new InvalidDataException("Local Ollama caption response was empty.");
        string caption = chat.Message?.Content?.Trim()
            ?? throw new InvalidDataException(
                "Local Ollama caption response did not contain message content.");
        if (caption.Length is 0 or > 500)
        {
            throw new InvalidDataException(
                "Local Ollama caption was empty or exceeded the bounded length.");
        }

        return new LocalSlideshowCaption(
            caption,
            model.Name,
            model.Digest,
            SlideshowCaptionPrompt.VersionFor(normalizedLanguage),
            elapsedMilliseconds);
    }

    private async Task<LocalVisionModelDescriptor> GetInstalledModelAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(
            new Uri(_configuration.OllamaBaseUri, "api/tags"),
            cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Local Ollama model inventory failed with HTTP {(int)response.StatusCode}.");
        }

        OllamaTagsResponse tags = JsonSerializer.Deserialize<OllamaTagsResponse>(
            payload,
            SerializerOptions)
            ?? throw new InvalidDataException("Local Ollama model inventory response was empty.");

        OllamaModelInfo? model = tags.Models.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, _configuration.Model, StringComparison.Ordinal) ||
            string.Equals(candidate.Model, _configuration.Model, StringComparison.Ordinal));
        if (model is null)
        {
            throw new InvalidOperationException(
                $"Local Ollama model '{_configuration.Model}' is not installed.");
        }

        return new LocalVisionModelDescriptor(
            model.Name ?? model.Model ?? _configuration.Model,
            NormalizeDigest(model.Digest));
    }

    private static string NormalizeDigest(string? digest)
    {
        string normalized = (digest ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal))
        {
            normalized = normalized["sha256:".Length..];
        }

        if (normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "Local Ollama model digest must contain exactly 64 hexadecimal characters.");
        }

        return normalized;
    }

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record LocalVisionModelDescriptor(string Name, string Digest);

    private sealed record OllamaTagsResponse(
        [property: JsonPropertyName("models")]
        IReadOnlyList<OllamaModelInfo> Models);

    private sealed record OllamaModelInfo(
        [property: JsonPropertyName("name")]
        string? Name,
        [property: JsonPropertyName("model")]
        string? Model,
        [property: JsonPropertyName("digest")]
        string? Digest);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")]
        OllamaMessage? Message);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("content")]
        string? Content);
}
