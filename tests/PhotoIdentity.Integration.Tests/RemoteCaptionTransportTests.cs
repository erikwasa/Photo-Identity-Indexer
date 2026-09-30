using System.Net;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Api;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class RemoteCaptionTransportTests
{
    [Fact]
    public async Task Remote_evaluator_preserves_digest_and_exact_request_boundary()
    {
        RemoteCaptionRecordingHandler handler = new();
        using HttpClient http = new(handler);
        OllamaVisionCaptionClient client = new(http, new Uri("https://caption.example.test/ollama"), "qwen2.5vl:3b", 1024, CaptionInferenceMode.Remote);
        var model = await client.GetInstalledModelAsync(CancellationToken.None);
        await client.CaptionAsync(new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
        Assert.Equal(new string('a', 64), model.Digest);
        using JsonDocument body = JsonDocument.Parse(handler.ChatBody);
        Assert.Equal(new[] { "messages", "model", "options", "stream" }, Keys(body.RootElement));
        Assert.Equal(OllamaVisionCaptionClient.CaptionPrompt, body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.All(handler.Requests, uri => Assert.Equal("caption.example.test", uri.Host));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transport_failures_are_sanitized_and_do_not_fall_back(bool evaluator)
    {
        RemoteCaptionRecordingHandler handler = new() { TransportFailure = true };
        using HttpClient http = new(handler);
        HttpRequestException error;
        if (evaluator)
        {
            OllamaVisionCaptionClient client = new(http, new Uri("https://caption.example.test/"), "qwen2.5vl:3b", 1024, CaptionInferenceMode.Remote);
            error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetInstalledModelAsync(CancellationToken.None));
        }
        else
        {
            LocalPhotoCaptionGenerator client = new(new CaptionTestHttpClientFactory(http), new OpenCvThumbnailRenderer(),
                new(new Uri("https://caption.example.test/"), "qwen2.5vl:3b", 1024, 600, CaptionInferenceMode.Remote));
            error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetInstalledModelAsync(CancellationToken.None));
        }
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("SECRET", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Remote_cancellation_remains_cancellation_without_private_exception_content()
    {
        RemoteCaptionRecordingHandler handler = new() { Cancel = true };
        using HttpClient http = new(handler);
        OllamaVisionCaptionClient client = new(http, new Uri("https://caption.example.test/"), "qwen2.5vl:3b", 1024, CaptionInferenceMode.Remote);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetInstalledModelAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.DoesNotContain("SECRET", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_remote_protocol_data_is_sanitized(bool evaluator)
    {
        RemoteCaptionRecordingHandler handler = new() { InvalidJson = true };
        using HttpClient http = new(handler);
        Task ReadModel() => evaluator
            ? new OllamaVisionCaptionClient(http, new Uri("https://caption.example.test/"), "qwen2.5vl:3b", 1024, CaptionInferenceMode.Remote).GetInstalledModelAsync(CancellationToken.None)
            : new LocalPhotoCaptionGenerator(new CaptionTestHttpClientFactory(http), new OpenCvThumbnailRenderer(),
                new(new Uri("https://caption.example.test/"), "qwen2.5vl:3b", 1024, 600, CaptionInferenceMode.Remote)).GetInstalledModelAsync(CancellationToken.None);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(ReadModel);
        Assert.DoesNotContain("SECRET", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    internal static string[] Keys(JsonElement element) => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}

internal sealed class CaptionTestHttpClientFactory(HttpClient http) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => http;
}

internal sealed class RemoteCaptionRecordingHandler : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];
    public string ChatBody { get; private set; } = string.Empty;
    public string Digest { get; set; } = new('a', 64);
    public bool TransportFailure { get; set; }
    public bool Cancel { get; set; }
    public bool InvalidJson { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (TransportFailure)
        {
            throw new HttpRequestException("SECRET credentials, query, image data and private paths from remote transport.");
        }
        if (Cancel)
        {
            throw new TaskCanceledException("SECRET", null, cancellationToken);
        }
        string payload;
        if (InvalidJson)
        {
            payload = "{\"SECRET\":broken}";
        }
        else if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal))
        {
            payload = JsonSerializer.Serialize(new { models = new[] { new { name = "qwen2.5vl:3b", model = "qwen2.5vl:3b", digest = Digest } } });
        }
        else if (request.RequestUri.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal))
        {
            ChatBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            payload = """{"message":{"content":"People sit around a table."}}""";
        }
        else
        {
            return new(HttpStatusCode.NotFound);
        }
        return new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }

    public static byte[] AssertProductionPayload(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        JsonElement body = document.RootElement;
        // An exhaustive allow-list, rather than substring-only assertions, excludes all catalogue metadata.
        Assert.Equal(new[] { "keep_alive", "messages", "model", "options", "stream" }, RemoteCaptionTransportTests.Keys(body));
        Assert.Equal("qwen2.5vl:3b", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("stream").GetBoolean());
        Assert.Equal("30m", body.GetProperty("keep_alive").GetString());
        Assert.Equal(1, body.GetProperty("messages").GetArrayLength());
        JsonElement message = body.GetProperty("messages")[0];
        Assert.Equal(new[] { "content", "images", "role" }, RemoteCaptionTransportTests.Keys(message));
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal(PhotoCaptionPrompt.TextFor("en"), message.GetProperty("content").GetString());
        Assert.Equal(1, message.GetProperty("images").GetArrayLength());
        JsonElement options = body.GetProperty("options");
        Assert.Equal(new[] { "num_ctx", "num_predict", "seed", "temperature" }, RemoteCaptionTransportTests.Keys(options));
        Assert.Equal(1024, options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(80, options.GetProperty("num_predict").GetInt32());
        Assert.Equal(0, options.GetProperty("temperature").GetInt32());
        Assert.Equal(0, options.GetProperty("seed").GetInt32());
        return Convert.FromBase64String(message.GetProperty("images")[0].GetString()!);
    }
}
