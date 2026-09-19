using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ImageEmbeddingEvaluationCommandTests
{
    [Fact]
    public void Options_parse_bounded_defaults_and_multiple_queries()
    {
        Guid collection = Guid.Parse("11111111-1111-1111-1111-111111111111");

        ImageEmbeddingEvaluationCommandOptions options =
            ImageEmbeddingEvaluationCommandOptions.Parse(
            [
                "--postgres-connection-env", "PHOTOIDENTITY_TEST",
                "--collection", collection.ToString("D"),
                "--proxy-root", ".",
                "--proxy-profile", "jpeg-1600-q78",
                "--model", "clip.onnx",
                "--tokenizer-vocab", "vocab.json",
                "--tokenizer-merges", "merges.txt",
                "--query", "people outdoors",
                "--query", "food on a table",
            ]);

        Assert.Equal("PHOTOIDENTITY_TEST", options.PostgresConnectionEnvironment);
        Assert.Equal(collection, options.CollectionId.Value);
        Assert.Equal(["people outdoors", "food on a table"], options.Queries);
        Assert.Equal(50, options.TargetCount);
        Assert.Equal(30, options.MomentGapMinutes);
        Assert.Equal(200, options.MaximumCandidates);
        Assert.Equal(8, options.RetrievalCount);
        Assert.Equal(4, options.SimilarSeedCount);
        Assert.Equal(5, options.NeighborsPerSeed);
        Assert.Null(options.ReportPath);
        Assert.Null(options.ReviewOutputDirectory);
    }

    [Fact]
    public void Options_require_query_and_bound_review_sizes()
    {
        string[] required =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--collection", "11111111-1111-1111-1111-111111111111",
            "--proxy-root", ".",
            "--proxy-profile", "jpeg-1600-q78",
            "--model", "clip.onnx",
            "--tokenizer-vocab", "vocab.json",
            "--tokenizer-merges", "merges.txt",
        ];

        Assert.Throws<ArgumentException>(() =>
            ImageEmbeddingEvaluationCommandOptions.Parse(required));
        Assert.Throws<ArgumentException>(() =>
            ImageEmbeddingEvaluationCommandOptions.Parse(
                [.. required, "--query", "x", "--max-candidates", "501"]));
        Assert.Throws<ArgumentException>(() =>
            ImageEmbeddingEvaluationCommandOptions.Parse(
                [.. required, "--query", "x", "--retrieval-count", "21"]));
        Assert.Throws<ArgumentException>(() =>
            ImageEmbeddingEvaluationCommandOptions.Parse(
                [.. required, "--query", "x", "--similar-seeds", "11"]));
        Assert.Throws<ArgumentException>(() =>
            ImageEmbeddingEvaluationCommandOptions.Parse(
                [.. required, "--query", "x", "--neighbors-per-seed", "11"]));

        ImageEmbeddingEvaluationCommandOptions options =
            ImageEmbeddingEvaluationCommandOptions.Parse(
            [
                .. required,
                "--query", "x",
                "--report", "report.json",
                "--review-output", "review",
            ]);
        Assert.Equal(Path.GetFullPath("report.json"), options.ReportPath);
        Assert.Equal(Path.GetFullPath("review"), options.ReviewOutputDirectory);
    }
}
