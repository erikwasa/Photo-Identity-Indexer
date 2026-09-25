using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresExactDuplicateRepositoryTests
{
    [Fact]
    public async Task Inventory_uses_verified_current_revision_and_keeps_presence_independent_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_exact_duplicates_{Guid.NewGuid():N}";
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
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            Assert.Null(initialization.Error);

            SourceId sourceId = SourceId.New();
            Guid firstAsset = Guid.NewGuid();
            Guid secondAsset = Guid.NewGuid();
            Guid thirdAsset = Guid.NewGuid();
            Guid unverifiedAsset = Guid.NewGuid();
            Guid firstRevision = Guid.NewGuid();
            Guid secondRevision = Guid.NewGuid();
            Guid thirdRevision = Guid.NewGuid();
            Guid unverifiedRevision = Guid.NewGuid();
            Sha256Digest duplicateHash = new(new string('a', 64));
            DateTimeOffset now = new(2026, 9, 25, 18, 0, 0, TimeSpan.Zero);

            await SeedAsync(
                testBuilder.ConnectionString,
                sourceId,
                firstAsset,
                secondAsset,
                thirdAsset,
                unverifiedAsset,
                firstRevision,
                secondRevision,
                thirdRevision,
                unverifiedRevision,
                duplicateHash,
                now);

            PostgresExactDuplicateRepository repository = new(database);
            IReadOnlyList<ExactDuplicateGroup> groups = await repository.GetGroupsAsync(sourceId);
            ExactDuplicateGroup group = Assert.Single(groups);
            Assert.Equal(duplicateHash, group.ContentHash);
            Assert.Equal(2, group.Copies.Count);
            Assert.Contains(group.Copies, copy => copy.AssetId == AssetId.From(firstAsset));
            Assert.Contains(group.Copies, copy => copy.AssetId == AssetId.From(secondAsset));
            Assert.DoesNotContain(group.Copies, copy => copy.AssetId == AssetId.From(thirdAsset));
            Assert.DoesNotContain(group.Copies, copy => copy.AssetId == AssetId.From(unverifiedAsset));
            Assert.NotEqual(group.Copies[0].AssetId, group.Copies[1].AssetId);
            Assert.NotEqual(group.Copies[0].RevisionId, group.Copies[1].RevisionId);

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand markMissing = connection.CreateCommand();
                markMissing.CommandText = "UPDATE assets SET deleted_at_utc = @now WHERE id = @asset_id;";
                markMissing.Parameters.AddWithValue("now", now.AddMinutes(1));
                markMissing.Parameters.AddWithValue("asset_id", secondAsset);
                await markMissing.ExecuteNonQueryAsync();
            }

            groups = await repository.GetGroupsAsync(sourceId);
            group = Assert.Single(groups);
            Assert.Contains(group.Copies, copy => copy.AssetId == AssetId.From(secondAsset) && copy.IsMissing);
            await AssertContentHashIndexIsNonUniqueAsync(testBuilder.ConnectionString);

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand requireVerification = connection.CreateCommand();
                requireVerification.CommandText = """
                    UPDATE archive_source_observations
                    SET verification_state = 'needs-source-verification'
                    WHERE asset_id = @asset_id;
                    """;
                requireVerification.Parameters.AddWithValue("asset_id", firstAsset);
                await requireVerification.ExecuteNonQueryAsync();
            }

            Assert.Empty(await repository.GetGroupsAsync(sourceId));
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAsync(
        string connectionString,
        SourceId sourceId,
        Guid firstAsset,
        Guid secondAsset,
        Guid thirdAsset,
        Guid unverifiedAsset,
        Guid firstRevision,
        Guid secondRevision,
        Guid thirdRevision,
        Guid unverifiedRevision,
        Sha256Digest duplicateHash,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'local-folder', 'duplicate-test-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES
                (@first_asset, @source_id, 'one/photo.jpg', @now, @now),
                (@second_asset, @source_id, 'two/copy.jpg', @now, @now),
                (@third_asset, @source_id, 'three/other.jpg', @now, @now),
                (@unverified_asset, @source_id, 'four/unverified.jpg', @now, @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
            VALUES
                (@first_revision, @first_asset, @duplicate_hash, 123, @now, 'image/jpeg'),
                (@second_revision, @second_asset, @duplicate_hash, 123, @now, 'image/jpeg'),
                (@third_revision, @third_asset, @other_hash, 123, @now, 'image/jpeg'),
                (@unverified_revision, @unverified_asset, @duplicate_hash, 123, @now, 'image/jpeg');

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
                (@first_asset, 123, @now, 'image/jpeg', @now, 'verified', @first_revision, 123, @now, 'image/jpeg', @now),
                (@second_asset, 123, @now, 'image/jpeg', @now, 'verified', @second_revision, 123, @now, 'image/jpeg', @now),
                (@third_asset, 123, @now, 'image/jpeg', @now, 'verified', @third_revision, 123, @now, 'image/jpeg', @now),
                (@unverified_asset, 123, @now, 'image/jpeg', @now, 'unverified', NULL, NULL, NULL, NULL, NULL);
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("first_asset", firstAsset);
        command.Parameters.AddWithValue("second_asset", secondAsset);
        command.Parameters.AddWithValue("third_asset", thirdAsset);
        command.Parameters.AddWithValue("unverified_asset", unverifiedAsset);
        command.Parameters.AddWithValue("first_revision", firstRevision);
        command.Parameters.AddWithValue("second_revision", secondRevision);
        command.Parameters.AddWithValue("third_revision", thirdRevision);
        command.Parameters.AddWithValue("unverified_revision", unverifiedRevision);
        command.Parameters.AddWithValue("duplicate_hash", duplicateHash.ToString());
        command.Parameters.AddWithValue("other_hash", new string('b', 64));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertContentHashIndexIsNonUniqueAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = current_schema()
              AND tablename = 'asset_revisions'
              AND indexname = 'ix_asset_revisions_content_sha256';
            """;
        string? definition = (string?)await command.ExecuteScalarAsync();
        Assert.NotNull(definition);
        Assert.DoesNotContain("UNIQUE", definition!, StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
