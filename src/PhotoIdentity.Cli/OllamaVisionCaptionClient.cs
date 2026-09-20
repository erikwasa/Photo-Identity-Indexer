using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoIdentity.Cli;

internal sealed record LocalVisionModelDescriptor(
    string Name,
    string Digest,
    long SizeBytes,
    string? Family,
    string? ParameterSize,
    string? QuantizationLevel);

internal sealed record LocalVisionCaptionResult(
    string Content,
    double ClientMilliseconds,
    double ServerMilliseconds,
    int PromptTokenCount,
    int GeneratedTokenCount);

internal sealed class OllamaVisionCaptionClient
{
    public const string PromptVersion = "wi-0128-neutral-visible-caption-v1";
    public const string DefaultModel = "qwen2.5vl:3b";
    public static readonly Uri DefaultBaseUri = new("http://127.0.0.1:11434/");

    internal const string CaptionPrompt =
        "Caption this private family photo for a local evaluation. " +
        "Write exactly one short neutral sentence of at most 20 words describing only directly visible people, objects, setting, and actions. " +
        "Do not identify or guess any person's name, relationship, age, occupation, nationality, or emotion. " +
        "Do not name or infer exact locations, dates, years, holidays, events, ceremonies, celebrations, or occasions. " +
        "Do not infer an event type such as wedding, birthday, concert, party, graduation, or festival. " +
        "If something is uncertain, omit it. Return only the sentence.";

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly string _model;
    private readonly int _contextTokens;

    public OllamaVisionCaptionClient(
        HttpClient httpClient,
        Uri baseUri,
        string model,
        int contextTokens = 4096)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (contextTokens is < 256 or > 32768)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contextTokens),
                "Ollama context tokens must be between 256 and 32768.");
        }
        if (!baseUri.IsAbsoluteUri ||
            !baseUri.IsLoopback ||
            (baseUri.Scheme != Uri.UriSchemeHttp &&
             baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "The local vision endpoint must be an absolute loopback HTTP(S) URL.",
                nameof(baseUri));
        }

        _httpClient = httpClient;
        _baseUri = EnsureTrailingSlash(baseUri);
        _model = model.Trim();
        _contextTokens = contextTokens;
    }

    public async Task<LocalVisionModelDescriptor> GetInstalledModelAsync(
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            new Uri(_baseUri, "api/tags"),
            cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Local Ollama model inventory failed with HTTP {(int)response.StatusCode}.");
        }

        OllamaTagsResponse tags = JsonSerializer.Deserialize<OllamaTagsResponse>(
            payload,
            SerializerOptions)
            ?? throw new InvalidDataException(
                "Local Ollama model inventory response was empty.");

        OllamaModelInfo? model = tags.Models.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, _model, StringComparison.Ordinal) ||
            string.Equals(candidate.Model, _model, StringComparison.Ordinal));
        if (model is null)
        {
            throw new InvalidOperationException(
                $"Local Ollama model '{_model}' is not installed. Run 'ollama pull {_model}' explicitly before the experiment.");
        }

        string digest = NormalizeDigest(model.Digest);
        return new(
            model.Name ?? model.Model ?? _model,
            digest,
            model.Size,
            model.Details?.Family,
            model.Details?.ParameterSize,
            model.Details?.QuantizationLevel);
    }

    public async Task<LocalVisionCaptionResult> CaptionAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken cancellationToken)
    {
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException(
                "Caption input image bytes cannot be empty.",
                nameof(imageBytes));
        }

        object request = new
        {
            model = _model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = CaptionPrompt,
                    images = new[] { Convert.ToBase64String(imageBytes.Span) },
                },
            },
            stream = false,
            options = new
            {
                temperature = 0,
                seed = 0,
                num_predict = 80,
                num_ctx = _contextTokens,
            },
        };

        long started = Stopwatch.GetTimestamp();
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            new Uri(_baseUri, "api/chat"),
            request,
            SerializerOptions,
            cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        double clientMilliseconds =
            Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Local Ollama caption request failed with HTTP {(int)response.StatusCode}.");
        }

        OllamaChatResponse chat = JsonSerializer.Deserialize<OllamaChatResponse>(
            payload,
            SerializerOptions)
            ?? throw new InvalidDataException(
                "Local Ollama caption response was empty.");
        string content = chat.Message?.Content?.Trim()
            ?? throw new InvalidDataException(
                "Local Ollama caption response did not contain message content.");
        if (content.Length == 0)
        {
            throw new InvalidDataException(
                "Local Ollama caption response contained an empty caption.");
        }
        if (content.Length > 500)
        {
            throw new InvalidDataException(
                "Local Ollama caption exceeded the bounded 500-character experiment limit.");
        }

        return new(
            content,
            clientMilliseconds,
            chat.TotalDuration / 1_000_000d,
            chat.PromptEvalCount,
            chat.EvalCount);
    }

    private static Uri EnsureTrailingSlash(Uri value) =>
        value.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? value
            : new Uri(value.AbsoluteUri + "/", UriKind.Absolute);

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
        [property: JsonPropertyName("size")]
        long Size,
        [property: JsonPropertyName("digest")]
        string? Digest,
        [property: JsonPropertyName("details")]
        OllamaModelDetails? Details);

    private sealed record OllamaModelDetails(
        [property: JsonPropertyName("family")]
        string? Family,
        [property: JsonPropertyName("parameter_size")]
        string? ParameterSize,
        [property: JsonPropertyName("quantization_level")]
        string? QuantizationLevel);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")]
        OllamaMessage? Message,
        [property: JsonPropertyName("total_duration")]
        long TotalDuration,
        [property: JsonPropertyName("prompt_eval_count")]
        int PromptEvalCount,
        [property: JsonPropertyName("eval_count")]
        int EvalCount);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("content")]
        string? Content);
}
