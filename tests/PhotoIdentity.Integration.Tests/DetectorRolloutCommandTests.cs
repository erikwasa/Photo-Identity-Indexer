using PhotoIdentity.Cli;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorRolloutCommandTests
{
    [Fact]
    public async Task Rollout_requires_PostgreSQL_and_an_available_connection_variable()
    {
        string run = Guid.NewGuid().ToString();
        StringWriter absent = new();
        Assert.Equal(2, await Program.RunAsync(
            ["rollout", "status", "--run", run], new StringWriter(), absent));
        Assert.Contains("PostgreSQL is the only supported rollout catalogue", absent.ToString());

        StringWriter sqlite = new();
        Assert.Equal(2, await Program.RunAsync(
            ["rollout", "status", "--run", run, "--database", "catalogue.db"],
            new StringWriter(), sqlite));
        Assert.Contains("Unknown option '--database'", sqlite.ToString());

        StringWriter missing = new();
        Assert.Equal(2, await Program.RunAsync(
            ["rollout", "status", "--run", run, "--postgres-connection-env", $"MISSING_{Guid.NewGuid():N}"],
            new StringWriter(), missing));
        Assert.Contains("environment variable is empty or missing", missing.ToString());
    }

    [Fact]
    public async Task Status_and_apply_use_only_selected_postgres_catalogue_when_live_postgres_is_configured()
    {
        string? adminString = Environment.GetEnvironmentVariable("PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString)) return;
        string name = $"photoidentity_cli_{Guid.NewGuid():N}";
        string variable = $"PHOTOIDENTITY_CLI_{Guid.NewGuid():N}";
        await using Npgsql.NpgsqlConnection admin = new(new Npgsql.NpgsqlConnectionStringBuilder(adminString) { Pooling = false }.ConnectionString);
        await admin.OpenAsync();
        await using (Npgsql.NpgsqlCommand create = new($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            string connectionString = new Npgsql.NpgsqlConnectionStringBuilder(adminString) { Database = name, Pooling = false }.ConnectionString;
            Environment.SetEnvironmentVariable(variable, connectionString);
            await using PhotoIdentity.Persistence.Postgres.PostgresCatalogueDatabase database = new(connectionString);
            await database.InitializeAsync();
            Guid run = Guid.NewGuid();
            await using (Npgsql.NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (Npgsql.NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO processing_runs (id, status, configuration_json, started_at_utc, completed_at_utc, error)
                    VALUES (@run, 'failed', '{}', now(), now(), 'synthetic failure');
                    INSERT INTO detector_pipelines (pipeline_hash, detector_model_id, detector_model_hash, canonical_definition, recorded_at_utc)
                    VALUES (@pipeline, 'centerface-2019-fp32', @model, '{}', now());
                    INSERT INTO processing_run_detector_pipelines (processing_run_id, pipeline_hash, recorded_at_utc)
                    VALUES (@run, @pipeline, now());
                    """;
                seed.Parameters.AddWithValue("run", run);
                seed.Parameters.AddWithValue("pipeline", new string('a', 64));
                seed.Parameters.AddWithValue("model", new string('b', 64));
                await seed.ExecuteNonQueryAsync();
            }
            foreach (string action in new[] { "status", "apply" })
            {
                StringWriter output = new();
                StringWriter error = new();
                Assert.Equal(0, await Program.RunAsync(
                    ["rollout", action, "--postgres-connection-env", variable, "--run", run.ToString()], output, error));
                Assert.Contains("processing-status: failed", output.ToString());
                Assert.Contains("rollout-complete: false", output.ToString());
                if (action == "apply") Assert.Contains("reviewed-applied: 0", output.ToString());
                Assert.Empty(error.ToString());
                Assert.DoesNotContain(connectionString, output.ToString());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            await using Npgsql.NpgsqlCommand drop = new($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Start_requires_explicit_immutable_revision_scope()
    {
        StringWriter output = new();
        StringWriter error = new();

        int exit = await Program.RunAsync(
            ["rollout", "start", "--postgres-connection-env", "UNUSED", "--output", "rollout-output"],
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
                "--postgres-connection-env", "UNUSED",
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
