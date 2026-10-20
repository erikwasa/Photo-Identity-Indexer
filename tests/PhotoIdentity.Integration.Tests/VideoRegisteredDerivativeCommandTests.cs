using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoRegisteredDerivativeCommandTests
{
    private static string[] Base(string root) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_TEST",
        "--database", "photoidentity_catalogue",
        "--cache-root", root
    ];

    [Fact]
    public void Maintain_is_bounded_and_dry_run_default()
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "registered-video-derivative-test"));
        VideoRegisteredDerivativeOptions option =
            VideoRegisteredDerivativeOptions.Parse("maintain", Base(root));
        Assert.False(option.Apply);
        Assert.False(option.VerifyHashes);
        Assert.Null(option.QuotaMiB);
        Assert.Equal(16, option.MaximumRecords);
        Assert.Equal(TimeSpan.FromHours(48), option.MinimumQuotaAge);
        Assert.Equal(root, option.CacheRoot);

        VideoRegisteredDerivativeOptions preview = VideoRegisteredDerivativeOptions.Parse(
            "maintain", [.. Base(root), "--quota-mib", "512",
                "--max-records", "12", "--min-age-hours", "72",
                "--verify-hashes", "--apply"]);
        Assert.True(preview.Apply);
        Assert.True(preview.VerifyHashes);
        Assert.Equal(512, preview.QuotaMiB);
        Assert.Equal(12, preview.MaximumRecords);
        Assert.Equal(TimeSpan.FromHours(72), preview.MinimumQuotaAge);
    }

    [Fact]
    public void Maintain_refuses_unsafe_repeated_and_unbounded_options()
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "registered-video-derivative-test"));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain", []));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("oops", Base(root)));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--cache-root", root]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--apply", "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--verify-hashes", "--verify-hashes"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--job-id", Guid.NewGuid().ToString()]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--quota-mib", "63"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--quota-mib", "16385"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--max-records", "33"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                [.. Base(root), "--min-age-hours", "23"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("maintain",
                ["--postgres-connection-env", "UNSAFE-NAME", "--database", "db",
                 "--cache-root", root]));
    }

    [Fact]
    public void Requeue_requires_exact_job_and_never_accepts_bulk_options()
    {
        Guid id = Guid.NewGuid();
        string[] args =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--database", "photoidentity_catalogue",
            "--job-id", id.ToString()
        ];
        VideoRegisteredDerivativeOptions dry =
            VideoRegisteredDerivativeOptions.Parse("requeue", args);
        Assert.False(dry.Apply);
        Assert.Equal(id, dry.JobId);
        Assert.Null(dry.CacheRoot);
        Assert.True(VideoRegisteredDerivativeOptions.Parse("requeue",
            [.. args, "--apply"]).Apply);

        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("requeue",
                [.. args, "--verify-hashes"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("requeue",
                [.. args, "--quota-mib", "100"]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("requeue",
                [.. args, "--cache-root", Path.GetTempPath()]));
        Assert.Throws<ArgumentException>(() =>
            VideoRegisteredDerivativeOptions.Parse("requeue",
                ["--postgres-connection-env", "PHOTOIDENTITY_TEST",
                 "--database", "db", "--job-id", Guid.Empty.ToString()]));
    }

    [Fact]
    public async Task Missing_connection_refuses_before_catalogue_access()
    {
        const string env = "PHOTOIDENTITY_WI0188_REGISTERED_MISSING_TEST";
        string? previous = Environment.GetEnvironmentVariable(env);
        try
        {
            Environment.SetEnvironmentVariable(env, null);
            var option = VideoRegisteredDerivativeOptions.Parse("requeue", [
                "--postgres-connection-env", env,
                "--database", "photoidentity_catalogue",
                "--job-id", Guid.NewGuid().ToString()
            ]);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoRegisteredDerivativeCommandRunner.RunAsync(option, TextWriter.Null));
        }
        finally { Environment.SetEnvironmentVariable(env, previous); }
    }
}
