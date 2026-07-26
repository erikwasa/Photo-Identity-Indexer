using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class BatchCommandTests
{
    [Fact]
    public async Task Status_and_cancel_report_durable_run_summary()
    {
        string directory = SqliteProcessingRepositoryTests.CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            IReadOnlyList<CatalogueAssetRevision> revisions =
                await SqliteProcessingRepositoryTests.SeedRevisionsAsync(database, 2);
            DateTimeOffset now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
            CatalogueProcessingRun run = SqliteProcessingRepositoryTests.CreateRun(now);
            SqliteProcessingRepository repository = new(database);
            await repository.CreateRunAsync(
                run,
                revisions.Select(revision =>
                    SqliteProcessingRepositoryTests.CreateJob(run.Id, revision.Id, now)).ToArray());

            StringWriter statusOutput = new();
            StringWriter statusError = new();
            int statusExit = await Program.RunAsync(
                ["batch", "status", "--database", databasePath, "--run", run.Id.ToString()],
                statusOutput,
                statusError);

            Assert.Equal(0, statusExit);
            Assert.Contains("status: pending", statusOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains("total: 2", statusOutput.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, statusError.ToString());

            StringWriter cancelOutput = new();
            int cancelExit = await Program.RunAsync(
                ["batch", "cancel", "--database", databasePath, "--run", run.Id.ToString()],
                cancelOutput,
                TextWriter.Null);

            Assert.Equal(0, cancelExit);
            Assert.Contains("status: cancelled", cancelOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains("cancelled: 2", cancelOutput.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            SqliteProcessingRepositoryTests.DeleteTemporaryDirectory(directory);
        }
    }
}
