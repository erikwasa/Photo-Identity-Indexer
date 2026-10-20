using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoDerivativeRecoveryCommandTests
{
    private static string[] SafeOptions(string root) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_TEST",
        "--database", "photoidentity_catalogue",
        "--cache-root", root
    ];

    [Fact]
    public void CLI_requires_explicit_safe_database_and_private_absolute_root()
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "video-derivative-dry-run-sample"));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse([]));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse(
            ["--postgres-connection-env", "TEST", "--database", "production"]));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse(
            SafeOptions(root).Concat(["--database", "other"]).ToArray()));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse(
            SafeOptions(root).Concat(["--apply", "--apply"]).ToArray()));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse(
        [
            "--postgres-connection-env", "UNSAFE-NAME",
            "--database", "db", "--cache-root", root
        ]));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse(
        [
            "--postgres-connection-env", "SAFE",
            "--database", "db", "--cache-root", "relative-cache"
        ]));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse(
            SafeOptions(root).Concat(["--max-files", "65"]).ToArray()));
        Assert.Throws<ArgumentException>(() => VideoDerivativeRecoveryOptions.Parse(
            SafeOptions(root).Concat(["--min-age-hours", "47"]).ToArray()));
    }

    [Fact]
    public void CLI_defaults_to_bounded_dry_run_and_requires_apply_for_mutation()
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "video-derivative-dry-run-sample"));
        VideoDerivativeRecoveryOptions options =
            VideoDerivativeRecoveryOptions.Parse(SafeOptions(root));
        Assert.False(options.Apply);
        Assert.Equal(TimeSpan.FromHours(72), options.MinimumAge);
        Assert.Equal(32, options.MaximumFiles);

        VideoDerivativeRecoveryOptions apply = VideoDerivativeRecoveryOptions.Parse(
            SafeOptions(root).Concat([
                "--max-files", "12", "--min-age-hours", "96", "--apply"
            ]).ToArray());
        Assert.True(apply.Apply);
        Assert.Equal(12, apply.MaximumFiles);
        Assert.Equal(TimeSpan.FromHours(96), apply.MinimumAge);
    }

    [Fact]
    public async Task Missing_connection_refuses_before_any_directory_or_catalogue_access()
    {
        const string variable = "PHOTOIDENTITY_WI0188_DERIVATIVE_MISSING_TEST";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            string root = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(), "nonexistent-recovery-root"));
            VideoDerivativeRecoveryOptions options =
                VideoDerivativeRecoveryOptions.Parse([
                    "--postgres-connection-env", variable,
                    "--database", "photoidentity_catalogue",
                    "--cache-root", root
                ]);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoDerivativeRecoveryCommandRunner.RunAsync(options, TextWriter.Null));
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }
}
