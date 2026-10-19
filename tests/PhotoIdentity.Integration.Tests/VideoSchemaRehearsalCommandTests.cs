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
    public async Task Missing_restore_report_and_remote_host_are_rejected_before_opening_connection()
    {
        const string variable = "PHOTOIDENTITY_WI0151_TEST_CONN";
        string? original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable,
                "Host=127.0.0.1;Port=5432;Database=photoidentity_production;Username=example;Password=private");
            VideoSchemaRehearsalOptions options = new(variable, Path.Combine(
                Path.GetTempPath(), "photoidentity-absent-restore-" + Guid.NewGuid().ToString("N") + ".json"));
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
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

    [Fact]
    public async Task Report_selected_database_is_validated_before_attempting_any_connection()
    {
        const string variable = "PHOTOIDENTITY_WI0151_TEST_REPORT_SELECTED_CONN";
        string? original = Environment.GetEnvironmentVariable(variable);
        string reportPath = Path.Combine(Path.GetTempPath(),
            "photoidentity-wi0151-report-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Simulates the launcher supplying a production DB name. The runner
            // must derive its target from the independently verified report, not
            // reject this string or open the launcher database.
            Environment.SetEnvironmentVariable(variable,
                "Host=127.0.0.1;Database=photoidentity_production;Username=example;Password=private");
            await File.WriteAllTextAsync(reportPath, """
                {
                  "passed": true,
                  "sourceDatabase": "photoidentity_production",
                  "verificationDatabase": "photoidentity_restore_20261015223202_7da49f",
                  "restoredEvidence": { "schemaVersion": 31 }
                }
                """);

            VideoSchemaRehearsalOptions options = new(variable, reportPath);
            InvalidOperationException wrongSchema = await Assert.ThrowsAsync<InvalidOperationException>(
                () => VideoSchemaRehearsalCommandRunner.RunAsync(options, TextWriter.Null));
            Assert.Contains("schema v32", wrongSchema.Message, StringComparison.Ordinal);

            // A forged report targeting the production database cannot get as far
            // as opening a connection, regardless of embedded connection database.
            await File.WriteAllTextAsync(reportPath, """
                {
                  "passed": true,
                  "sourceDatabase": "photoidentity_production",
                  "verificationDatabase": "photoidentity_production",
                  "restoredEvidence": { "schemaVersion": 32 }
                }
                """);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                VideoSchemaRehearsalCommandRunner.RunAsync(options, TextWriter.Null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            if (File.Exists(reportPath)) File.Delete(reportPath);
        }
    }
    [Fact]
    public async Task V34_rehearsal_requires_a_verified_v33_restore_before_network_access()
    {
        const string variable = "PHOTOIDENTITY_WI0188_V34_TEST_CONN";
        string? original = Environment.GetEnvironmentVariable(variable);
        string reportPath = Path.Combine(Path.GetTempPath(),
            "photoidentity-wi0188-restore-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // The credential string might point to production, but it must
            // NEVER be the connection target, and a wrong schema fails
            // before any PostgreSQL network connection is attempted.
            Environment.SetEnvironmentVariable(variable,
                "Host=127.0.0.1;Database=production;Username=example;Password=private");
            await File.WriteAllTextAsync(reportPath, """
                {
                  "passed": true,
                  "sourceDatabase": "production",
                  "verificationDatabase": "photoidentity_restore_20261019104403_abc123",
                  "restoredEvidence": { "schemaVersion": 32 }
                }
                """);
            var options = new VideoSchemaRehearsalOptions(variable, reportPath);
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => VideoSchemaRehearsalCommandRunner.RunAsync(
                    options, TextWriter.Null, expectedRestoredSchemaVersion: 33));
            Assert.Contains("schema v33", error.Message, StringComparison.Ordinal);

            await File.WriteAllTextAsync(reportPath, """
                {
                  "passed": true,
                  "sourceDatabase": "production",
                  "verificationDatabase": "production",
                  "restoredEvidence": { "schemaVersion": 33 }
                }
                """);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                VideoSchemaRehearsalCommandRunner.RunAsync(
                    options, TextWriter.Null, expectedRestoredSchemaVersion: 33));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            if (File.Exists(reportPath)) File.Delete(reportPath);
        }
    }

}
