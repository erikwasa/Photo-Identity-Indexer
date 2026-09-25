using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSourceCopyExclusionRepositoryTests
{
    [Fact]
    public async Task Exclusion_is_durable_locator_specific_and_revision_guarded()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_exclusion_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString) { Pooling = false };
        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await create.ExecuteNonQueryAsync();
        }

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
            Guid firstAsset = Guid.NewGuid();
            Guid secondAsset = Guid.NewGuid();
            AssetRevisionId firstRevision = AssetRevisionId.New();
            AssetRevisionId secondRevision = AssetRevisionId.New();
            DateTimeOffset now = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source_id, 'local-folder', 'private-root', @now);
                    INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES
                        (@first_asset, @source_id, 'A/private.jpg', @now, @now),
                        (@second_asset, @source_id, 'B/copy.jpg', @now, @now);
                    INSERT INTO asset_revisions (id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES
                        (@first_revision, @first_asset, @hash, 4, @now, 'image/jpeg'),
                        (@second_revision, @second_asset, @hash, 4, @now, 'image/jpeg');
                    """;
                seed.Parameters.AddWithValue("source_id", sourceId.Value);
                seed.Parameters.AddWithValue("first_asset", firstAsset);
                seed.Parameters.AddWithValue("second_asset", secondAsset);
                seed.Parameters.AddWithValue("first_revision", firstRevision.Value);
                seed.Parameters.AddWithValue("second_revision", secondRevision.Value);
                seed.Parameters.AddWithValue("hash", new string('a', 64));
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }

            PostgresSourceCopyExclusionRepository repository = new(database);
            SourceCopyExclusionState created = await repository.ExcludeAsync(
                sourceId,
                "A/private.jpg",
                now.AddMinutes(1));
            Assert.Equal(SourceCopyPurgeStates.Pending, created.PurgeState);
            Assert.True(await repository.IsRevisionExcludedAsync(firstRevision));
            Assert.False(await repository.IsRevisionExcludedAsync(secondRevision));

            await repository.SetPurgeStateAsync(
                sourceId,
                "A/private.jpg",
                SourceCopyPurgeStates.Completed,
                null,
                now.AddMinutes(2));

            PostgresSourceCopyExclusionRepository restarted = new(database);
            SourceCopyExclusionState durable = Assert.IsType<SourceCopyExclusionState>(
                await restarted.GetAsync(sourceId, "A/private.jpg"));
            Assert.Equal(created.ExcludedAtUtc, durable.ExcludedAtUtc);
            Assert.Equal(SourceCopyPurgeStates.Completed, durable.PurgeState);
            Assert.Null(await restarted.GetAsync(sourceId, "B/copy.jpg"));
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
