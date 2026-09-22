using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

public sealed record PhotoCaptionGenerationConfiguration(
    Uri OllamaBaseUri,
    string Model,
    int ContextTokens,
    int TimeoutSeconds)
{
    public const string GenerationVersion = "wi-0128-photo-caption-v3";
    public const string SentenceAwareGenerationVersion = "wi-0128-photo-caption-v2";
    public const string LegacyGenerationVersion = "wi-0128-photo-caption-v1";
    public const string ImageMode = "thumbnail-480x320";
    public const string DefaultModel = "qwen2.5vl:3b";
    public const int DefaultContextTokens = 1024;
    public const int DefaultTimeoutSeconds = 600;
    public static readonly Uri DefaultOllamaBaseUri = new("http://127.0.0.1:11434/");
}

public sealed record LocalPhotoCaptionModel(
    string Name,
    string Digest);

public sealed record LocalPhotoCaption(
    string Content,
    string Model,
    string ModelDigest,
    string PromptVersion,
    double ClientMilliseconds);

public static class PhotoCaptionPrompt
{
    public static string VersionFor(string language) =>
        PhotoCaptionLanguages.Normalize(language) == PhotoCaptionLanguages.Swedish
            ? "wi-0128-neutral-visible-caption-sv-v1"
            : "wi-0128-neutral-visible-caption-en-v1";

    public static string TextFor(string language) =>
        PhotoCaptionLanguages.Normalize(language) == PhotoCaptionLanguages.Swedish
            ? "Beskriv det här privata familjefotot. Skriv exakt en kort neutral mening på högst 20 ord på svenska som endast beskriver direkt synliga personer, föremål, miljö och handlingar. Identifiera eller gissa inte namn, relationer, ålder, yrke, nationalitet eller känslor. Ange eller härled inte exakta platser, datum, årtal, helgdagar, händelser, ceremonier, firanden eller tillfällen. Härled inte händelsetyp som bröllop, födelsedag, konsert, fest, examen eller festival. Om något är osäkert, utelämna det. Returnera endast meningen."
            : "Caption this private family photo. Write exactly one short neutral sentence of at most 20 words describing only directly visible people, objects, setting, and actions. Do not identify or guess any person's name, relationship, age, occupation, nationality, or emotion. Do not name or infer exact locations, dates, years, holidays, events, ceremonies, celebrations, or occasions. Do not infer an event type such as wedding, birthday, concert, party, graduation, or festival. If something is uncertain, omit it. Return only the sentence.";
}

public static class PhotoCaptionOutputNormalizer
{
    public const int MaximumWords = 20;
    public const string InvalidFormatRiskCode = "caption-output-format";

    private static readonly char[] SentenceTerminators = ['.', '!', '?'];
    private static readonly char[] ClauseTerminators = [',', ';', ':', '–', '—'];

    public static bool TryNormalize(string rawContent, out string normalizedCaption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawContent);

        string compact = Regex.Replace(rawContent.Trim(), @"\s+", " ");
        int sentenceEnd = compact.IndexOfAny(SentenceTerminators);
        if (sentenceEnd < 0)
        {
            normalizedCaption = string.Empty;
            return false;
        }

        string firstSentence = compact[..(sentenceEnd + 1)].Trim();
        if (WordCount(firstSentence) <= MaximumWords)
        {
            normalizedCaption = firstSentence;
            return true;
        }

        MatchCollection words = Regex.Matches(firstSentence, @"\S+");
        int boundedEnd = words[MaximumWords - 1].Index +
            words[MaximumWords - 1].Length;
        string boundedPrefix = firstSentence[..boundedEnd];
        int clauseEnd = boundedPrefix.LastIndexOfAny(ClauseTerminators);
        if (clauseEnd <= 0)
        {
            normalizedCaption = string.Empty;
            return false;
        }

        string clause = boundedPrefix[..clauseEnd].Trim();
        if (WordCount(clause) < 5)
        {
            normalizedCaption = string.Empty;
            return false;
        }

        normalizedCaption = clause.TrimEnd('.', '!', '?') + ".";
        return true;
    }

    private static int WordCount(string content) =>
        Regex.Matches(content, @"\S+").Count;
}

public sealed class LocalPhotoCaptionGenerator
{
    public const string HttpClientName = "LocalPhotoCaption";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenCvThumbnailRenderer _thumbnailRenderer;
    private readonly PhotoCaptionGenerationConfiguration _configuration;

    public LocalPhotoCaptionGenerator(
        IHttpClientFactory httpClientFactory,
        OpenCvThumbnailRenderer thumbnailRenderer,
        PhotoCaptionGenerationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(thumbnailRenderer);
        ArgumentNullException.ThrowIfNull(configuration);
        _httpClientFactory = httpClientFactory;
        _thumbnailRenderer = thumbnailRenderer;
        _configuration = configuration;
    }

    public async Task<LocalPhotoCaptionModel> GetInstalledModelAsync(
        CancellationToken cancellationToken)
    {
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        return await GetInstalledModelAsync(http, cancellationToken);
    }

    public async Task<LocalPhotoCaption> GenerateAsync(
        string proxyPath,
        string language,
        LocalPhotoCaptionModel model,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyPath);
        string normalizedLanguage = PhotoCaptionLanguages.Normalize(language);

        EncodedThumbnail? thumbnail =
            await _thumbnailRenderer.RenderAsync(proxyPath, cancellationToken);
        if (thumbnail is null)
        {
            throw new InvalidDataException(
                "The review proxy could not be rendered as a caption thumbnail.");
        }

        ArgumentNullException.ThrowIfNull(model);
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);

        object request = new
        {
            model = _configuration.Model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = PhotoCaptionPrompt.TextFor(normalizedLanguage),
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

        return new LocalPhotoCaption(
            caption,
            model.Name,
            model.Digest,
            PhotoCaptionPrompt.VersionFor(normalizedLanguage),
            elapsedMilliseconds);
    }

    private async Task<LocalPhotoCaptionModel> GetInstalledModelAsync(
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

        return new LocalPhotoCaptionModel(
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
