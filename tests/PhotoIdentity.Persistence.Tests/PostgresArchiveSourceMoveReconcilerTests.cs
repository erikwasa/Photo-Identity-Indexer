using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresArchiveSourceMoveReconcilerTests
{
    [Fact]
    public async Task ReconcileAsync_PreservesMissingAssetIdentityAndRevisionHistory_WhenMoveIsExactAndUnique()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_move_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
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
            Guid oldAssetId = Guid.NewGuid();
            Guid newAssetId = Guid.NewGuid();
            Guid oldRevisionId = Guid.NewGuid();
            Guid newRevisionId = Guid.NewGuid();
            Guid faceId = Guid.NewGuid();
            DateTimeOffset t0 = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
            DateTimeOffset t1 = new(2026, 9, 25, 11, 0, 0, TimeSpan.Zero);
            string contentHash = new string('a', 64);

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source_id, 'local-folder', 'move-root', @t0);

                    INSERT INTO assets (
                        id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc)
                    VALUES
                        (@old_asset_id, @source_id, 'A/old.jpg', @t0, @t0, @t1),
                        (@new_asset_id, @source_id, 'B/new.jpg', @t1, @t1, NULL);

                    INSERT INTO asset_revisions (
                        id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES
                        (@old_revision_id, @old_asset_id, @hash, 5, @t0, 'image/jpeg'),
                        (@new_revision_id, @new_asset_id, @hash, 5, @t1, 'image/jpeg');

                    INSERT INTO archive_source_observations (
                        asset_id,
                        observed_size_bytes,
                        observed_last_write_utc,
                        observed_media_type,
                        observed_at_utc,
                        verification_state,
                        verified_revision_id,
                        verified_size_bytes,
                        verified_last_write_utc,
                        verified_media_type,
                        verified_at_utc)
                    VALUES
                        (@old_asset_id, 5, @t0, 'image/jpeg', @t1, 'verified', @old_revision_id, 5, @t0, 'image/jpeg', @t0),
                        (@new_asset_id, 5, @t0, 'image/jpeg', @t1, 'verified', @new_revision_id, 5, @t0, 'image/jpeg', @t1);

                    INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
                    VALUES
                        (@old_asset_id, 'unavailable', @t1),
                        (@new_asset_id, 'local', @t1);

                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES (@face_id, @old_revision_id, 0, @t0);
                    """;
                seed.Parameters.AddWithValue("source_id", sourceId.Value);
                seed.Parameters.AddWithValue("old_asset_id", oldAssetId);
                seed.Parameters.AddWithValue("new_asset_id", newAssetId);
                seed.Parameters.AddWithValue("old_revision_id", oldRevisionId);
                seed.Parameters.AddWithValue("new_revision_id", newRevisionId);
                seed.Parameters.AddWithValue("face_id", faceId);
                seed.Parameters.AddWithValue("hash", contentHash);
                seed.Parameters.AddWithValue("t0", t0);
                seed.Parameters.AddWithValue("t1", t1);
                await seed.ExecuteNonQueryAsync();
            }

            PostgresArchiveSourceMoveReconciler reconciler = new(database);
            int reconciled = await reconciler.ReconcileAsync(sourceId, ["A", "B"], t1);
            Assert.Equal(1, reconciled);

            await using NpgsqlConnection verify = new(testBuilder.ConnectionString);
            await verify.OpenAsync();
            await using NpgsqlCommand command = verify.CreateCommand();
            command.CommandText = """
                SELECT
                    asset.id,
                    asset.source_key,
                    asset.deleted_at_utc,
                    observation.verified_revision_id,
                    availability.availability,
                    (SELECT COUNT(*) FROM assets WHERE source_id = @source_id) AS asset_count,
                    (SELECT COUNT(*) FROM face_occurrences WHERE asset_revision_id = @old_revision_id) AS face_count
                FROM assets AS asset
                INNER JOIN archive_source_observations AS observation ON observation.asset_id = asset.id
                LEFT JOIN archive_asset_availability AS availability ON availability.asset_id = asset.id
                WHERE asset.source_id = @source_id;
                """;
            command.Parameters.AddWithValue("source_id", sourceId.Value);
            command.Parameters.AddWithValue("old_revision_id", oldRevisionId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(oldAssetId, reader.GetGuid(0));
            Assert.Equal("B/new.jpg", reader.GetString(1));
            Assert.True(reader.IsDBNull(2));
            Assert.Equal(oldRevisionId, reader.GetGuid(3));
            Assert.Equal("local", reader.GetString(4));
            Assert.Equal(1L, reader.GetInt64(5));
            Assert.Equal(1L, reader.GetInt64(6));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
