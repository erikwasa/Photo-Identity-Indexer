using PhotoIdentity.Cli;
using PhotoIdentity.Core.Catalogue;
using System.Text.Json;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class NarrationEvaluationCommandTests
{
    [Fact]
    public void Options_parse_safe_local_defaults()
    {
        Guid collection = Guid.Parse("11111111-1111-1111-1111-111111111111");

        NarrationEvaluationCommandOptions options =
            NarrationEvaluationCommandOptions.Parse(
            [
                "--postgres-connection-env", "PHOTOIDENTITY_TEST",
                "--collection", collection.ToString("D"),
                "--proxy-root", ".",
                "--proxy-profile", "jpeg-1600-q78",
            ]);

        Assert.Equal("PHOTOIDENTITY_TEST", options.PostgresConnectionEnvironment);
        Assert.Equal(collection, options.CollectionId.Value);
        Assert.Equal(OllamaVisionCaptionClient.DefaultBaseUri, options.OllamaBaseUri);
        Assert.Equal(OllamaVisionCaptionClient.DefaultModel, options.Model);
        Assert.Equal("proxy", options.CaptionImageMode);
        Assert.Equal(4096, options.OllamaContextTokens);
        Assert.Equal(50, options.TargetCount);
        Assert.Equal(30, options.MomentGapMinutes);
        Assert.Equal(12, options.SampleCount);
        Assert.Equal(180, options.TimeoutSeconds);
        Assert.Null(options.ReportPath);
        Assert.Null(options.ReviewOutputDirectory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Remote_opt_in_is_order_independent_and_uses_bounded_thumbnails(bool modeFirst)
    {
        string[] mode = ["--inference-mode", "Remote"];
        string[] endpoint = ["--ollama-base-url", "https://caption.example.test/ollama"];
        NarrationEvaluationCommandOptions options = NarrationEvaluationCommandOptions.Parse(
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--collection", "11111111-1111-1111-1111-111111111111",
            "--proxy-root", ".", "--proxy-profile", "jpeg-1600-q78",
            .. (modeFirst ? mode : endpoint), .. (modeFirst ? endpoint : mode),
        ]);
        Assert.Equal(CaptionInferenceMode.Remote, options.InferenceMode);
        Assert.Equal("thumbnail", options.CaptionImageMode);
        Assert.Equal("/ollama/", options.OllamaBaseUri.AbsolutePath);
        NarrationPipelineEvidence pipeline = NarrationEvaluationCommandRunner.CreatePipelineEvidence(
            options, new("qwen2.5vl:3b", new string('a', 64), 1, null, null, null));
        Assert.Equal("remote", pipeline.InferenceMode);
        Assert.Equal("caption.example.test", pipeline.EndpointHost);
        Assert.False(pipeline.LoopbackOnly);
        Assert.DoesNotContain("/ollama", JsonSerializer.Serialize(pipeline), StringComparison.Ordinal);

    }

    [Theory]
    [InlineData("--ollama-base-url", "http://caption.example.test/")]
    [InlineData("--ollama-base-url", "relative")]
    [InlineData("--ollama-base-url", "https://user:secret@caption.example.test/")]
    [InlineData("--caption-image-mode", "proxy")]
    [InlineData("--inference-mode", "Local")]
    public void Remote_rejects_unsafe_or_contradictory_options(string option, string value)
    {
        Assert.Throws<ArgumentException>(() => NarrationEvaluationCommandOptions.Parse(
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--collection", "11111111-1111-1111-1111-111111111111",
            "--proxy-root", ".", "--proxy-profile", "jpeg-1600-q78",
            "--inference-mode", "Remote", option, value,
        ]));
    }

    [Fact]
    public void Options_reject_external_endpoint_and_bound_sample()
    {
        string[] required =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--collection", "11111111-1111-1111-1111-111111111111",
            "--proxy-root", ".",
            "--proxy-profile", "jpeg-1600-q78",
        ];

        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--ollama-base-url", "https://example.com/",
            ]));
        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--sample-count", "31",
            ]));
        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--model", "one",
                "--model", "two",
            ]));
        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--caption-image-mode", "original",
            ]));
        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--ollama-context", "128",
            ]));

        NarrationEvaluationCommandOptions options =
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--ollama-base-url", "http://localhost:11434",
                "--model", "qwen2.5vl:3b",
                "--caption-image-mode", "thumbnail",
                "--ollama-context", "1024",
                "--sample-count", "20",
                "--report", "report.json",
                "--review-output", "review",
            ]);

        Assert.True(options.OllamaBaseUri.IsLoopback);
        Assert.Equal("thumbnail", options.CaptionImageMode);
        Assert.Equal(1024, options.OllamaContextTokens);
        Assert.Equal(20, options.SampleCount);
        Assert.Equal(Path.GetFullPath("report.json"), options.ReportPath);
        Assert.Equal(Path.GetFullPath("review"), options.ReviewOutputDirectory);
    }
}
