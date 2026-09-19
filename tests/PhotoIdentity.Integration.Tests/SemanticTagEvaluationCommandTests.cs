using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SemanticTagEvaluationCommandTests
{
    [Fact]
    public void Options_parse_bounded_defaults_without_touching_files()
    {
        Guid collection = Guid.Parse("11111111-1111-1111-1111-111111111111");

        SemanticTagEvaluationCommandOptions options =
            SemanticTagEvaluationCommandOptions.Parse(
            [
                "--postgres-connection-env", "PHOTOIDENTITY_TEST",
                "--collection", collection.ToString("D"),
                "--proxy-root", ".",
                "--proxy-profile", "review-v1",
                "--model", "clip.onnx",
                "--tokenizer-vocab", "vocab.json",
                "--tokenizer-merges", "merges.txt",
                "--concept-vocabulary", "concepts.json",
            ]);

        Assert.Equal("PHOTOIDENTITY_TEST", options.PostgresConnectionEnvironment);
        Assert.Equal(collection, options.CollectionId.Value);
        Assert.Equal(50, options.TargetCount);
        Assert.Equal(30, options.MomentGapMinutes);
        Assert.Equal(200, options.MaximumCandidates);
        Assert.Equal(2, options.ConceptsPerPhoto);
        Assert.Equal(0, options.OriginalComparisonCount);
        Assert.Null(options.ReviewOutputDirectory);
    }

    [Fact]
    public void Options_require_explicit_original_comparison_count_and_bound_inputs()
    {
        string[] required =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--collection", "11111111-1111-1111-1111-111111111111",
            "--proxy-root", ".",
            "--proxy-profile", "review-v1",
            "--model", "clip.onnx",
            "--tokenizer-vocab", "vocab.json",
            "--tokenizer-merges", "merges.txt",
            "--concept-vocabulary", "concepts.json",
        ];

        SemanticTagEvaluationCommandOptions options =
            SemanticTagEvaluationCommandOptions.Parse(
                [.. required, "--compare-originals", "20", "--concepts-per-photo", "3", "--review-output", "review-output"]);

        Assert.Equal(20, options.OriginalComparisonCount);
        Assert.Equal(3, options.ConceptsPerPhoto);
        Assert.Equal(Path.GetFullPath("review-output"), options.ReviewOutputDirectory);
        Assert.Throws<ArgumentException>(() =>
            SemanticTagEvaluationCommandOptions.Parse(
                [.. required, "--compare-originals", "101"]));
        Assert.Throws<ArgumentException>(() =>
            SemanticTagEvaluationCommandOptions.Parse(
                [.. required, "--max-candidates", "0"]));
        Assert.Throws<ArgumentException>(() =>
            SemanticTagEvaluationCommandOptions.Parse(
                [.. required, "--review-output", "a", "--review-output", "b"]));
    }
}
