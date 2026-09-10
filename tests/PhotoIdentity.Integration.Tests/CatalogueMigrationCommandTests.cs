using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity_Integration_Tests;

public sealed class CatalogueMigrationCommandTests
{
    [Fact]
    public async Task Migrate_requires_backup_and_connection_environment()
    {
        StringWriter output = new();
        StringWriter error = new();

        Assert.Equal(2, await Program.RunAsync(["catalogue", "migrate"], output, error));
        Assert.Contains("--sqlite-backup", error.ToString(), StringComparison.Ordinal);

        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-migrate-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string backup = Path.Combine(directory, "catalogue.db");
        await File.WriteAllBytesAsync(backup, []);
        try
        {
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            Assert.Equal(2, await Program.RunAsync(
                ["catalogue", "migrate", "--sqlite-backup", backup, "--postgres-connection-env", $"MISSING_{Guid.NewGuid():N}"],
                output,
                error));
            Assert.Contains("environment variable is empty or missing", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Migrate_preserves_stable_history_repairs_sequences_and_is_repeatable_WhenLivePostgresIsConfigured()
    {
        string? adminString = Environment.GetEnvironmentVariable("PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-catalogue-migrate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sqlitePath = Path.Combine(directory, "catalogue-backup.db");
        string firstReportPath = Path.Combine(directory, "migration-report-1.json");
        string secondReportPath = Path.Combine(directory, "migration-report-2.json");
        string firstDatabaseName = $"photoidentity_migrate_{Guid.NewGuid():N}";
        string secondDatabaseName = $"photoidentity_migrate_{Guid.NewGuid():N}";
        string environmentName = $"PHOTOIDENTITY_MIGRATE_{Guid.NewGuid():N}";

        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        Guid faceId = Guid.NewGuid();
        Guid cropId = Guid.NewGuid();
        Guid personId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 10, 5, 0, 0, TimeSpan.Zero);
        string hash = new('a', 64);
        string modelHash = new('b', 64);

        NpgsqlConnectionStringBuilder adminBuilder = new(adminString) { Pooling = false };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await CreateDatabaseAsync(admin, firstDatabaseName);
        await CreateDatabaseAsync(admin, secondDatabaseName);

        try
        {
            await CreateSourceCatalogueAsync(
                sqlitePath,
                sourceId,
                assetId,
                revisionId,
                faceId,
                cropId,
                personId,
                now,
                hash,
                modelHash);

            string firstTarget = BuildTargetConnectionString(adminString, firstDatabaseName);
            string secondTarget = BuildTargetConnectionString(adminString, secondDatabaseName);

            string firstOutput = await RunMigrationAsync(
                sqlitePath,
                firstTarget,
                environmentName,
                firstReportPath);
            string secondOutput = await RunMigrationAsync(
                sqlitePath,
                secondTarget,
                environmentName,
                secondReportPath);

            Assert.Contains("validation: passed", firstOutput, StringComparison.Ordinal);
            Assert.Contains("validation: passed", secondOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(firstTarget, firstOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(secondTarget, secondOutput, StringComparison.Ordinal);

            string firstReport = await File.ReadAllTextAsync(firstReportPath);
            string secondReport = await File.ReadAllTextAsync(secondReportPath);
            Assert.DoesNotContain(firstTarget, firstReport, StringComparison.Ordinal);
            Assert.DoesNotContain(secondTarget, secondReport, StringComparison.Ordinal);
            Assert.Equal(
                ReadComparableReportEvidence(firstReport),
                ReadComparableReportEvidence(secondReport));

            await VerifyMigratedStateAndSequenceAsync(
                firstTarget,
                sourceId,
                revisionId,
                cropId,
                personId,
                now);
            await VerifyMigratedStateAndSequenceAsync(
                secondTarget,
                sourceId,
                revisionId,
                cropId,
                personId,
                now);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            await DropDatabaseAsync(admin, firstDatabaseName);
            await DropDatabaseAsync(admin, secondDatabaseName);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task CreateSourceCatalogueAsync(
        string sqlitePath,
        Guid sourceId,
        Guid assetId,
        Guid revisionId,
        Guid faceId,
        Guid cropId,
        Guid personId,
        DateTimeOffset now,
        string hash,
        string modelHash)
    {
        SqliteCatalogueDatabase sqlite = new(sqlitePath);
        await sqlite.InitializeAsync();
        await using SqliteConnection connection = await sqlite.OpenConnectionAsync();
        await using SqliteCommand seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES ($source, 'local-archive', 'Kamerabilder', $now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES ($asset, $source, '2026/09/photo.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES ($revision, $asset, $hash, 12345, $now, 'image/jpeg', 1200, 800);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($face, $revision, 0, $now);
            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256, storage_path, width, height, created_at_utc)
            VALUES ($crop, $face, 'review-v1', $hash, 'faces/face.jpg', 112, 112, $now);
            INSERT INTO embeddings (
                id, face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES (41, $crop, 'sface-2021dec-fp32', $model_hash, 2, 1.0, X'0000803F00000000', $now);
            INSERT INTO people (id, display_name, created_at_utc)
            VALUES ($person, 'Migration Person', $now);
            INSERT INTO person_labels (
                id, person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc, note)
            VALUES (51, $person, $face, 'manual', 'maintainer', $now, 'migration acceptance');
            INSERT INTO review_actions (
                id, face_occurrence_id, action_kind, person_id, person_label_id, actor, note, created_at_utc)
            VALUES (61, $face, 'assign', $person, 51, 'maintainer', 'accepted', $now);
            INSERT INTO identity_suggestions (
                id, face_occurrence_id, suggested_person_id, model_id, model_hash, score, status, created_at_utc)
            VALUES (71, $face, $person, 'sface-2021dec-fp32', $model_hash, 0.91, 'pending', $now);
            """;
        seed.Parameters.AddWithValue("$source", sourceId.ToString());
        seed.Parameters.AddWithValue("$asset", assetId.ToString());
        seed.Parameters.AddWithValue("$revision", revisionId.ToString());
        seed.Parameters.AddWithValue("$face", faceId.ToString());
        seed.Parameters.AddWithValue("$crop", cropId.ToString());
        seed.Parameters.AddWithValue("$person", personId.ToString());
        seed.Parameters.AddWithValue("$hash", hash);
        seed.Parameters.AddWithValue("$model_hash", modelHash);
        seed.Parameters.AddWithValue("$now", now.ToString("O"));
        await seed.ExecuteNonQueryAsync();
    }

    private static async Task<string> RunMigrationAsync(
        string sqlitePath,
        string targetConnectionString,
        string environmentName,
        string reportPath)
    {
        Environment.SetEnvironmentVariable(environmentName, targetConnectionString);
        StringWriter output = new();
        StringWriter error = new();
        int exit = await Program.RunAsync(
            [
                "catalogue", "migrate",
                "--sqlite-backup", sqlitePath,
                "--postgres-connection-env", environmentName,
                "--report", reportPath,
            ],
            output,
            error);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(File.Exists(reportPath));
        return output.ToString();
    }

    private static string ReadComparableReportEvidence(string reportJson)
    {
        using JsonDocument document = JsonDocument.Parse(reportJson);
        JsonElement root = document.RootElement;
        return JsonSerializer.Serialize(new
        {
            SourceSha256 = root.GetProperty("SourceSha256").GetString(),
            SourceBytes = root.GetProperty("SourceBytes").GetInt64(),
            SqliteSchemaVersion = root.GetProperty("SqliteSchemaVersion").GetInt32(),
            PostgresSchemaVersion = root.GetProperty("PostgresSchemaVersion").GetInt32(),
            RowsCopied = root.GetProperty("RowsCopied").GetInt64(),
            SequencesRepaired = root.GetProperty("SequencesRepaired").GetInt32(),
            Tables = root.GetProperty("Tables").GetRawText(),
            CriticalCounts = root.GetProperty("CriticalCounts").GetRawText(),
            Validation = root.GetProperty("Validation").GetString(),
        });
    }

    private static async Task VerifyMigratedStateAndSequenceAsync(
        string targetConnectionString,
        Guid sourceId,
        Guid revisionId,
        Guid cropId,
        Guid personId,
        DateTimeOffset now)
    {
        await using NpgsqlConnection verify = new(targetConnectionString);
        await verify.OpenAsync();
        Assert.Equal(sourceId, await ScalarGuidAsync(verify, "SELECT id FROM sources LIMIT 1;"));
        Assert.Equal(revisionId, await ScalarGuidAsync(verify, "SELECT id FROM asset_revisions LIMIT 1;"));
        Assert.Equal(personId, await ScalarGuidAsync(verify, "SELECT person_id FROM review_actions WHERE id = 61;"));
        Assert.Equal(1L, await ScalarLongAsync(verify, "SELECT COUNT(*) FROM identity_suggestions WHERE id = 71 AND status = 'pending';"));

        await using NpgsqlCommand insertEmbedding = verify.CreateCommand();
        insertEmbedding.CommandText = """
            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES (@crop, 'sequence-test', @hash, 2, 1.0, @vector, @now)
            RETURNING id;
            """;
        insertEmbedding.Parameters.AddWithValue("crop", cropId);
        insertEmbedding.Parameters.AddWithValue("hash", new string('c', 64));
        insertEmbedding.Parameters.AddWithValue("vector", new byte[] { 0, 0, 128, 63, 0, 0, 0, 0 });
        insertEmbedding.Parameters.AddWithValue("now", now.AddMinutes(1));
        long generated = Convert.ToInt64(await insertEmbedding.ExecuteScalarAsync());
        Assert.True(generated > 41);
    }

    private static string BuildTargetConnectionString(string adminString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(adminString)
        {
            Database = databaseName,
            Pooling = false,
        }.ConnectionString;

    private static async Task CreateDatabaseAsync(NpgsqlConnection admin, string databaseName)
    {
        await using NpgsqlCommand command = new($"CREATE DATABASE \"{databaseName}\"", admin);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(NpgsqlConnection admin, string databaseName)
    {
        await using NpgsqlCommand command = new($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> ScalarGuidAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected GUID scalar."));
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
