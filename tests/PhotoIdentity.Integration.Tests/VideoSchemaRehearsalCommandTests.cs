using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoSchemaRehearsalCommandTests
{
    [Fact]
    public void Rehearsal_requires_named_environment_variable_and_restore_report()
    {
        Assert.Throws<ArgumentException>(() => VideoSchemaRehearsalOptions.Parse([]));
        Assert.Throws<ArgumentException>(() => VideoSchemaRehearsalOptions.Parse(
            ["--postgres-connection-env", "MISSING"]));
        Assert.Throws<ArgumentException>(() => VideoSchemaRehearsalOptions.Parse(
            ["--restore-report", "restore.json"]));
        Assert.Throws<ArgumentException>(() => VideoSchemaRehearsalOptions.Parse(
            ["--postgres-connection-env", "INVALID-NAME",
             "--restore-report", "restore.json"]));
        Assert.Throws<ArgumentException>(() => VideoSchemaRehearsalOptions.Parse(
            ["--postgres-connection-env", "SAFE_NAME",
             "--restore-report", "restore.json",
             "--unknown", "unexpected"]));
        Assert.Throws<ArgumentException>(() => VideoSchemaRehearsalOptions.Parse(
            ["--postgres-connection-env", "SAFE_NAME",
             "--restore-report", "restore.json",
             "--restore-report", "again.json"]));
    }

    [Fact]
    public void Valid_rehearsal_options_do_not_expose_or_require_database_password()
    {
        VideoSchemaRehearsalOptions options = VideoSchemaRehearsalOptions.Parse(
            ["--postgres-connection-env", "PHOTOIDENTITY_REHEARSAL",
             "--restore-report", "rehearsal.restore-verification.json"]);
        Assert.Equal("PHOTOIDENTITY_REHEARSAL", options.PostgresConnectionEnvironmentVariable);
        Assert.True(Path.IsPathFullyQualified(options.RestoreVerificationReportPath));
    }

    [Fact]
    public async Task Production_like_database_names_are_rejected_before_opening_connection()
    {
        const string variable = "PHOTOIDENTITY_WI0151_TEST_CONN";
        string? original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable,
                "Host=127.0.0.1;Port=5432;Database=photoidentity_production;Username=example;Password=private");
            VideoSchemaRehearsalOptions options = new(variable, "missing.json");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoSchemaRehearsalCommandRunner.RunAsync(options, TextWriter.Null));

            Environment.SetEnvironmentVariable(variable,
                "Host=remote.example.org;Database=photoidentity_restore_abc;Username=example;Password=private");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoSchemaRehearsalCommandRunner.RunAsync(options, TextWriter.Null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }
}
