using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSourceCopyPurgeRepositoryTests
{
    [Fact]
    public async Task Purge_removes_revision_state_and_keeps_exclusion_tombstone()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_purge_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString) { Pooling = false };
        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await create.ExecuteNonQueryAsync();
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"photoidentity-pg-purge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };
            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            await database.InitializeAsync();

            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = AssetRevisionId.New();
            PersonId personId = PersonId.New();
            DateTimeOffset now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source_id, 'local-folder', 'private-root', @now);
                    INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES (@asset_id, @source_id, 'Private/example.jpg', @now, @now);
                    INSERT INTO asset_revisions (
                        id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES (@revision_id, @asset_id, @hash, 4, @now, 'image/jpeg');
                    INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
                    VALUES (@person_id, 'Shared Person', @now, NULL);
                    """;
                seed.Parameters.AddWithValue("source_id", sourceId.Value);
                seed.Parameters.AddWithValue("asset_id", assetId.Value);
                seed.Parameters.AddWithValue("revision_id", revisionId.Value);
                seed.Parameters.AddWithValue("person_id", personId.Value);
                seed.Parameters.AddWithValue("hash", new string('a', 64));
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            PostgresSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(sourceId, "Private/example.jpg", now.AddMinutes(1));
            Assert.False(await exclusions.RestoreAsync(sourceId, "Private/example.jpg"));

            PostgresSourceCopyPurgeRepository purges = new(database);
            SourceCopyPurgeManifest manifest = await purges.PrepareManifestAsync(
                sourceId,
                "Private/example.jpg",
                new SourceCopyPurgeRoots(
                    Path.Combine(tempRoot, "analysis"),
                    Path.Combine(tempRoot, "review"),
                    Path.Combine(tempRoot, "detector")),
                now.AddMinutes(2));
            Assert.Empty(manifest.Artifacts);

            await purges.PurgeCatalogueAsync(sourceId, "Private/example.jpg");
            await purges.PurgeCatalogueAsync(sourceId, "Private/example.jpg");
            await purges.ClearManifestAsync(sourceId, "Private/example.jpg");
            await exclusions.SetPurgeStateAsync(
                sourceId,
                "Private/example.jpg",
                SourceCopyPurgeStates.Completed,
                null,
                now.AddMinutes(3));

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                Assert.Equal(0L, await CountAsync(connection, "assets"));
                Assert.Equal(0L, await CountAsync(connection, "asset_revisions"));
                Assert.Equal(1L, await CountAsync(connection, "people"));
                Assert.Equal(1L, await CountAsync(connection, "source_copy_exclusions"));
                Assert.Equal(0L, await CountAsync(connection, "source_copy_purge_manifests"));
                Assert.Equal(0L, await CountAsync(connection, "source_copy_purge_manifest_artifacts"));
            }

            SourceCopyExclusionState state = Assert.IsType<SourceCopyExclusionState>(
                await exclusions.GetAsync(sourceId, "Private/example.jpg"));
            Assert.Equal(SourceCopyPurgeStates.Completed, state.PurgeState);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
