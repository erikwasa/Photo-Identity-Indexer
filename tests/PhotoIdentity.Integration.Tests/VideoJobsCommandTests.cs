using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoJobsCommandTests
{
    [Fact]
    public void Status_requires_explicit_safe_database_and_never_accepts_apply()
    {
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("status", []));
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("unknown", []));
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("status",
                ["--postgres-connection-env", "UNSAFE-NAME", "--database", "valid"]));
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("status",
                ["--postgres-connection-env", "VALID", "--database", "production", "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("status",
                ["--postgres-connection-env", "VALID", "--database", "production",
                 "--job-id", Guid.NewGuid().ToString()]));
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("status",
                ["--postgres-connection-env", "VALID", "--database", "production",
                 "--database", "second"]));

        VideoJobsCommandOptions valid = VideoJobsCommandOptions.Parse("status",
            ["--postgres-connection-env", "PHOTOIDENTITY_TEST",
             "--database", "photoidentity_catalogue"]);
        Assert.Equal("status", valid.Action);
        Assert.Equal("photoidentity_catalogue", valid.DatabaseName);
        Assert.Null(valid.JobId);
        Assert.False(valid.Apply);
    }

    [Fact]
    public void Cancellation_is_dry_run_unless_deliberately_applied_and_requires_job_id()
    {
        Guid id = Guid.NewGuid();
        string[] argv =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--database", "photoidentity_catalogue", "--job-id", id.ToString()
        ];
        VideoJobsCommandOptions dry = VideoJobsCommandOptions.Parse("cancel", argv);
        Assert.False(dry.Apply);
        Assert.Equal(id, dry.JobId);

        VideoJobsCommandOptions applied = VideoJobsCommandOptions.Parse("cancel", [.. argv, "--apply"]);
        Assert.True(applied.Apply);

        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("cancel",
                ["--postgres-connection-env", "VALID", "--database", "production"]));
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("cancel",
                ["--postgres-connection-env", "VALID", "--database", "production",
                 "--job-id", Guid.Empty.ToString(), "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoJobsCommandOptions.Parse("cancel",
                [.. argv, "--apply", "--apply"]));
    }

    [Fact]
    public async Task Command_rejects_missing_credentials_without_opening_a_connection()
    {
        const string variable = "PHOTOIDENTITY_WI0188_MISSING_JOBS_TEST_CONNECTION";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            VideoJobsCommandOptions option = VideoJobsCommandOptions.Parse("status",
                ["--postgres-connection-env", variable, "--database", "photoidentity_catalogue"]);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoJobsCommandRunner.RunAsync(option, TextWriter.Null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
