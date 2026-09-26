using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class SourceCopyPurgePartialAnalysisTests
{
    [Fact]
    public async Task Purge_removes_partial_archive_analysis_directory_before_job_completion()
    {
        string root = Path.Combine(Path.GetTempPath(), $"photoidentity-purge-partial-analysis-{Guid.NewGuid():N}");
        string fallbackAnalysisRoot = Path.Combine(root, "fallback-analysis");
        string configuredAnalysisRoot = Path.Combine(root, "configured-analysis");
        string reviewRoot = Path.Combine(root, "review");
        string detectorRoot = Path.Combine(root, "detector");
        Directory.CreateDirectory(fallbackAnalysisRoot);
        Directory.CreateDirectory(configuredAnalysisRoot);
        Directory.CreateDirectory(reviewRoot);
        Directory.CreateDirectory(detectorRoot);

        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(root, "catalogue.db"));
            await database.InitializeAsync();

            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = AssetRevisionId.New();
            Guid runId = Guid.NewGuid();
            Guid jobId = Guid.NewGuid();
            DateTimeOffset now = new(2026, 9, 26, 1, 0, 0, TimeSpan.Zero);
            const string sourceKey = "Private/partial-analysis.jpg";

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    CREATE TABLE IF NOT EXISTS archive_analysis_runs (
                        processing_run_id TEXT NOT NULL PRIMARY KEY,
                        profile_hash TEXT NOT NULL,
                        registered_at_utc TEXT NOT NULL,
                        FOREIGN KEY (processing_run_id) REFERENCES processing_runs (id) ON DELETE CASCADE
                    );

                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES ($source_id, 'local-folder', 'private-root', $now);
                    INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES ($asset_id, $source_id, $source_key, $now, $now);
                    INSERT INTO asset_revisions (
                        id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES ($revision_id, $asset_id, $hash, 4, $now, 'image/jpeg');
                    INSERT INTO processing_runs (
                        id, status, configuration_json, started_at_utc, completed_at_utc, error)
                    VALUES ($run_id, 'running', $configuration_json, $now, NULL, NULL);
                    INSERT INTO archive_analysis_runs (processing_run_id, profile_hash, registered_at_utc)
                    VALUES ($run_id, $profile_hash, $now);
                    INSERT INTO processing_jobs (
                        id, processing_run_id, asset_revision_id, status,
                        attempt_count, available_at_utc, started_at_utc, completed_at_utc, error)
                    VALUES ($job_id, $run_id, $revision_id, 'running', 1, $now, $now, NULL, NULL);
                    """;
                seed.Parameters.AddWithValue("$source_id", sourceId.ToString());
                seed.Parameters.AddWithValue("$asset_id", assetId.ToString());
                seed.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                seed.Parameters.AddWithValue("$run_id", runId.ToString());
                seed.Parameters.AddWithValue("$job_id", jobId.ToString());
                seed.Parameters.AddWithValue("$source_key", sourceKey);
                seed.Parameters.AddWithValue("$hash", new string('a', 64));
                seed.Parameters.AddWithValue("$profile_hash", new string('b', 64));
                seed.Parameters.AddWithValue(
                    "$configuration_json",
                    System.Text.Json.JsonSerializer.Serialize(new { outputRoot = configuredAnalysisRoot }));
                seed.Parameters.AddWithValue("$now", Format(now));
                await seed.ExecuteNonQueryAsync();
            }

            string partialDirectory = Path.Combine(
                configuredAnalysisRoot,
                "runs",
                runId.ToString("D"),
                "assets",
                revisionId.ToString());
            Directory.CreateDirectory(partialDirectory);
            await File.WriteAllTextAsync(Path.Combine(partialDirectory, "partial.tmp"), "partial-analysis");

            SqliteSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(sourceId, sourceKey, now.AddMinutes(1));
            SourceCopyPurgeService service = new(
                exclusions,
                new SqliteSourceCopyPurgeRepository(database),
                new SourceCopyPurgeRoots(fallbackAnalysisRoot, reviewRoot, detectorRoot),
                new SourceCopyPurgeFileSystem(),
                TimeProvider.System);

            Assert.True(await service.PurgeAsync(sourceId, sourceKey));
            Assert.False(Directory.Exists(partialDirectory));

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand count = connection.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM assets;";
                Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
            }

            SourceCopyExclusionState state = Assert.IsType<SourceCopyExclusionState>(
                await exclusions.GetAsync(sourceId, sourceKey));
            Assert.Equal(SourceCopyPurgeStates.Completed, state.PurgeState);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
