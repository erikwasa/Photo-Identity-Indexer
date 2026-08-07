using PhotoIdentity.Cli;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorRolloutCommandTests
{
    [Fact]
    public async Task Start_requires_explicit_immutable_revision_scope()
    {
        StringWriter output = new();
        StringWriter error = new();

        int exit = await Program.RunAsync(
            ["rollout", "start", "--database", "catalogue.db", "--output", "rollout-output"],
            output,
            error);

        Assert.Equal(2, exit);
        Assert.Contains("requires at least one '--revision'", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_rejects_detector_threshold_or_source_scan_overrides()
    {
        string revisionId = Guid.NewGuid().ToString("D");
        foreach (string[] unsupported in new[]
                 {
                     new[] { "--detector-model", "yunet-2023mar-fp32" },
                     new[] { "--confidence", "0.4" },
                     new[] { "--source", "C:/photos" },
                 })
        {
            List<string> args =
            [
                "rollout", "start",
                "--database", "catalogue.db",
                "--output", "rollout-output",
                "--revision", revisionId,
            ];
            args.AddRange(unsupported);
            StringWriter output = new();
            StringWriter error = new();

            int exit = await Program.RunAsync(args.ToArray(), output, error);

            Assert.Equal(2, exit);
            Assert.Contains("Unknown option", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Help_identifies_rollout_as_separate_fixed_pipeline_path()
    {
        StringWriter output = new();
        StringWriter error = new();

        int exit = await Program.RunAsync(["help"], output, error);

        Assert.Equal(0, exit);
        Assert.Contains("rollout start", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("fixed to the governed CenterFace 0.5", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("batch command is not a detector-migration mechanism", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }
}
