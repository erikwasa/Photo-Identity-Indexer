using Microsoft.Data.Sqlite;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity_Integration_Tests;

public sealed class PostgresRuntimeApplicationTests_IdentityRegenerationMigration
{
    [Fact]
    public async Task Catalogue_migration_preserves_identity_regeneration_runs_and_targets_WhenLivePostgresIsConfigured()
    {
        string? adminString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
        {
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"photoidentity-identity-regeneration-migrate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sqlitePath = Path.Combine(directory, "catalogue-backup.db");
        string databaseName = $"photoidentity_identity_regen_migrate_{Guid.NewGuid():N}";
        string environmentName = $"PHOTOIDENTITY_IDENTITY_REGEN_MIGRATE_{Guid.NewGuid():N}";

        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        Guid faceId = Guid.NewGuid();
        Guid runId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 10, 16, 0, 0, TimeSpan.Zero);
        string hash = new('a', 64);
        string modelHash = new('b', 64);

        NpgsqlConnectionStringBuilder adminBuilder = new(adminString) { Pooling = false };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = new($"CREATE DATABASE \"{databaseName}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await CreateSourceCatalogueAsync(
                sqlitePath,
                sourceId,
                assetId,
                revisionId,
                faceId,
                runId,
                now,
                hash,
                modelHash);

            string targetConnection = new NpgsqlConnectionStringBuilder(adminString)
            {
                Database = databaseName,
                Pooling = false,
            }.ConnectionString;
            Environment.SetEnvironmentVariable(environmentName, targetConnection);

            StringWriter output = new();
            StringWriter error = new();
            int exit = await Program.RunAsync(
                [
                    "catalogue", "migrate",
                    "--sqlite-backup", sqlitePath,
                    "--postgres-connection-env", environmentName,
                ],
                output,
                error);

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains("validation: passed", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(targetConnection, output.ToString(), StringComparison.Ordinal);

            await using NpgsqlConnection verify = new(targetConnection);
            await verify.OpenAsync();
            Assert.Equal(
                1L,
                await ScalarLongAsync(
                    verify,
                    "SELECT COUNT(*) FROM identity_match_regeneration_runs WHERE id = @id;",
                    runId));
            Assert.Equal(
                1L,
                await ScalarLongAsync(
                    verify,
                    "SELECT COUNT(*) FROM identity_match_regeneration_targets WHERE run_id = @id;",
                    runId));

            await using NpgsqlCommand state = verify.CreateCommand();
            state.CommandText = """
                SELECT status, target_count, processed_target_count
                FROM identity_match_regeneration_runs
                WHERE id = @id;
                """;
            state.Parameters.AddWithValue("id", runId);
            await using NpgsqlDataReader reader = await state.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("pending", reader.GetString(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
            await using NpgsqlCommand drop = new(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                admin);
            await drop.ExecuteNonQueryAsync();
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
        Guid runId,
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

            CREATE TABLE identity_match_regeneration_runs (
                id TEXT NOT NULL PRIMARY KEY,
                model_id TEXT NOT NULL,
                model_hash TEXT NOT NULL,
                policy_version INTEGER NOT NULL CHECK (policy_version >= 1),
                status TEXT NOT NULL CHECK (status IN ('pending', 'running', 'completed', 'stale', 'failed')),
                evidence_review_action_id INTEGER NOT NULL,
                evidence_suggestion_review_action_id INTEGER NOT NULL,
                evidence_person_merge_action_id INTEGER NOT NULL,
                evidence_embedding_id INTEGER NOT NULL,
                target_count INTEGER NOT NULL CHECK (target_count >= 0),
                processed_target_count INTEGER NOT NULL CHECK (processed_target_count >= 0),
                suggested_target_count INTEGER NOT NULL CHECK (suggested_target_count >= 0),
                suggestion_count INTEGER NOT NULL CHECK (suggestion_count >= 0),
                automatically_assigned_count INTEGER NOT NULL CHECK (automatically_assigned_count >= 0),
                error_count INTEGER NOT NULL CHECK (error_count >= 0),
                requested_by TEXT NOT NULL,
                requested_at_utc TEXT NOT NULL,
                started_at_utc TEXT NULL,
                completed_at_utc TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                error TEXT NULL
            );
            CREATE TABLE identity_match_regeneration_targets (
                run_id TEXT NOT NULL,
                face_occurrence_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
                status TEXT NOT NULL CHECK (status IN ('pending', 'running', 'completed', 'error')),
                suggestion_count INTEGER NOT NULL CHECK (suggestion_count >= 0),
                error TEXT NULL,
                PRIMARY KEY (run_id, face_occurrence_id),
                UNIQUE (run_id, ordinal),
                FOREIGN KEY (run_id) REFERENCES identity_match_regeneration_runs (id) ON DELETE CASCADE,
                FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE
            );

            INSERT INTO identity_match_regeneration_runs (
                id, model_id, model_hash, policy_version, status,
                evidence_review_action_id, evidence_suggestion_review_action_id,
                evidence_person_merge_action_id, evidence_embedding_id,
                target_count, processed_target_count, suggested_target_count,
                suggestion_count, automatically_assigned_count, error_count,
                requested_by, requested_at_utc, started_at_utc, completed_at_utc,
                updated_at_utc, error)
            VALUES (
                $run, 'sface-2021dec-fp32', $model_hash, 1, 'pending',
                0, 0, 0, 0,
                1, 0, 0,
                0, 0, 0,
                'maintainer', $now, NULL, NULL, $now, NULL);
            INSERT INTO identity_match_regeneration_targets (
                run_id, face_occurrence_id, ordinal, status, suggestion_count, error)
            VALUES ($run, $face, 0, 'pending', 0, NULL);
            """;
        seed.Parameters.AddWithValue("$source", sourceId.ToString());
        seed.Parameters.AddWithValue("$asset", assetId.ToString());
        seed.Parameters.AddWithValue("$revision", revisionId.ToString());
        seed.Parameters.AddWithValue("$face", faceId.ToString());
        seed.Parameters.AddWithValue("$run", runId.ToString("D"));
        seed.Parameters.AddWithValue("$hash", hash);
        seed.Parameters.AddWithValue("$model_hash", modelHash);
        seed.Parameters.AddWithValue("$now", now.ToString("O"));
        await seed.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection connection,
        string sql,
        Guid id)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
