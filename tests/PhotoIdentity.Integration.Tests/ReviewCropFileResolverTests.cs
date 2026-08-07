using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewCropFileResolverTests
{
    [Theory]
    [InlineData("runs")]
    [InlineData("rollouts")]
    public async Task Resolve_supports_historical_and_rollout_run_storage(string runDirectory)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-review-crop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "catalogue.db");
        string outputRoot = Path.Combine(directory, "output");
        Guid runId = Guid.NewGuid();
        string relativePath = $"{runDirectory}/{runId:D}/assets/revision/candidates/candidate-001/aligned.png";
        string physicalPath = Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await File.WriteAllBytesAsync(physicalPath, [1, 2, 3]);

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO processing_runs (
                        id, status, configuration_json, started_at_utc)
                    VALUES ($id, 'completed', $configuration, $started_at_utc);
                    """;
                command.Parameters.AddWithValue("$id", runId.ToString());
                command.Parameters.AddWithValue(
                    "$configuration",
                    JsonSerializer.Serialize(new { outputRoot }));
                command.Parameters.AddWithValue("$started_at_utc", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            ReviewCropFileResolver resolver = new(database);
            string? resolved = await resolver.ResolveAsync(relativePath);

            Assert.Equal(Path.GetFullPath(physicalPath), resolved);
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
}
