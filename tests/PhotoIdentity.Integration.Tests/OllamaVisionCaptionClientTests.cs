using System.Net;
using System.Text;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class OllamaVisionCaptionClientTests
{
    [Fact]
    public void Client_rejects_non_loopback_endpoint()
    {
        using HttpClient http = new();

        Assert.Throws<ArgumentException>(() =>
            new OllamaVisionCaptionClient(
                http,
                new Uri("https://example.com/"),
                "qwen2.5vl:3b"));
    }

    [Fact]
    public async Task Client_reads_model_provenance_and_sends_image_only_to_loopback()
    {
        RecordingHandler handler = new();
        using HttpClient http = new(handler);
        OllamaVisionCaptionClient client = new(
            http,
            new Uri("http://127.0.0.1:11434/"),
            "qwen2.5vl:3b",
            contextTokens: 1024);

        LocalVisionModelDescriptor model =
            await client.GetInstalledModelAsync(CancellationToken.None);
        LocalVisionCaptionResult caption =
            await client.CaptionAsync(
                new byte[] { 1, 2, 3, 4 },
                CancellationToken.None);

        Assert.Equal(new string('a', 64), model.Digest);
        Assert.Equal(3_200_000_000L, model.SizeBytes);
        Assert.Equal("Several people sit around a table.", caption.Content);
        Assert.Contains(
            "Caption this private family photo",
            handler.ChatRequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            handler.ChatRequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"num_ctx\":1024",
            handler.ChatRequestBody,
            StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.True(uri.IsLoopback));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public string ChatRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == "/api/tags")
            {
                return Json("""
                    {
                      "models": [
                        {
                          "name": "qwen2.5vl:3b",
                          "model": "qwen2.5vl:3b",
                          "size": 3200000000,
                          "digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                          "details": {
                            "family": "qwen25vl",
                            "parameter_size": "3.8B",
                            "quantization_level": "Q4_K_M"
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/api/chat")
            {
                ChatRequestBody =
                    await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("""
                    {
                      "message": {
                        "role": "assistant",
                        "content": "Several people sit around a table."
                      },
                      "done": true,
                      "total_duration": 1500000000,
                      "prompt_eval_count": 42,
                      "eval_count": 9
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                value,
                Encoding.UTF8,
                "application/json"),
        };
    }
}
