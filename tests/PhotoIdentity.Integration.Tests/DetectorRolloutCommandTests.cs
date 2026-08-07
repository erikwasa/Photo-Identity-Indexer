using Microsoft.Data.Sqlite;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Sqlite;

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
    public async Task Status_reports_failed_processing_run_as_incomplete_even_without_candidates()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-rollout-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "catalogue.db");
        Guid runId = Guid.NewGuid();
        string pipelineHash = new('a', 64);
        string detectorHash = new('b', 64);

        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteTransaction transaction = connection.BeginTransaction();
                using (SqliteCommand run = connection.CreateCommand())
                {
                    run.Transaction = transaction;
                    run.CommandText = """
                        INSERT INTO processing_runs (
                            id, status, configuration_json, started_at_utc, completed_at_utc, error)
                        VALUES ($id, 'failed', '{}', $now, $now, 'test failure');
                        """;
                    run.Parameters.AddWithValue("$id", runId.ToString());
                    run.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                    await run.ExecuteNonQueryAsync();
                }

                using (SqliteCommand pipeline = connection.CreateCommand())
                {
                    pipeline.Transaction = transaction;
                    pipeline.CommandText = """
                        INSERT INTO detector_pipelines (
                            pipeline_hash, detector_model_id, detector_model_hash, canonical_definition, recorded_at_utc)
                        VALUES ($pipeline_hash, 'centerface-2019-fp32', $detector_hash, '{}', $now);
                        INSERT INTO processing_run_detector_pipelines (
                            processing_run_id, pipeline_hash, recorded_at_utc)
                        VALUES ($run_id, $pipeline_hash, $now);
                        """;
                    pipeline.Parameters.AddWithValue("$pipeline_hash", pipelineHash);
                    pipeline.Parameters.AddWithValue("$detector_hash", detectorHash);
                    pipeline.Parameters.AddWithValue("$run_id", runId.ToString());
                    pipeline.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                    await pipeline.ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }

            StringWriter output = new();
            StringWriter error = new();
            int exit = await Program.RunAsync(
                ["rollout", "status", "--database", databasePath, "--run", runId.ToString()],
                output,
                error);

            Assert.Equal(0, exit);
            Assert.Contains("processing-status: failed", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("rollout-complete: false", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
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
