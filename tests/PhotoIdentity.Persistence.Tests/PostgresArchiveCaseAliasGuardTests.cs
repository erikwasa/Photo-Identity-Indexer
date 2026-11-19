using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresArchiveCaseAliasGuardTests
{
    [Fact]
    public async Task Scan_rejects_new_case_alias_and_preserves_existing_annotations()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_case_alias_{Guid.NewGuid():N}";
        string quotedName = '"' + databaseName.Replace("\"", "\"\"") + '"';
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString) { Pooling = false };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quotedName};";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder builder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            await database.InitializeAsync();

            SourceId sourceId = SourceId.New();
            Guid originalAssetId = Guid.NewGuid();
            Guid originalRevisionId = Guid.NewGuid();
            Guid personId = Guid.NewGuid();
            DateTimeOffset t0 = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
            DateTimeOffset t1 = t0.AddHours(1);

            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'archive-root', @t0);
                    INSERT INTO assets(id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES (@asset, @source, 'Fideli/portrait.jpg', @t0, @t0);
                    INSERT INTO asset_revisions(id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES (@revision, @asset, @hash, 10, @t0, 'image/jpeg');
                    INSERT INTO archive_source_observations(
                        asset_id, observed_size_bytes, observed_last_write_utc, observed_media_type,
                        observed_at_utc, verification_state, verified_revision_id, verified_size_bytes,
                        verified_last_write_utc, verified_media_type, verified_at_utc)
                    VALUES (@asset, 10, @t0, 'image/jpeg', @t0, 'verified',
                            @revision, 10, @t0, 'image/jpeg', @t0);
                    INSERT INTO people(id, display_name, created_at_utc)
                    VALUES (@person, 'Test Person', @t0);
                    INSERT INTO photo_person_actions(
                        asset_revision_id, person_id, action_kind, actor, created_at_utc)
                    VALUES (@revision, @person, 'add', 'test', @t0);
                    INSERT INTO photo_tags(normalized_name, display_name, created_by, created_at_utc)
                    VALUES ('test-tag', 'Test tag', 'test', @t0);
                    INSERT INTO photo_tag_actions(
                        asset_revision_id, tag_id, action_kind, actor, created_at_utc)
                    SELECT @revision, id, 'add', 'test', @t0
                    FROM photo_tags WHERE normalized_name = 'test-tag';
                    """;
                seed.Parameters.AddWithValue("source", sourceId.Value);
                seed.Parameters.AddWithValue("asset", originalAssetId);
                seed.Parameters.AddWithValue("revision", originalRevisionId);
                seed.Parameters.AddWithValue("person", personId);
                seed.Parameters.AddWithValue("hash", new string('a', 64));
                seed.Parameters.AddWithValue("t0", t0);
                await seed.ExecuteNonQueryAsync();
            }

            ArchiveCatalogueSource archive = new(sourceId, "local-folder", "archive-root", t0);
            PostgresArchiveSourceScanBatchRepository repository = new(database);
            ArchiveSourceScanWrite Write(string key) => new(
                new SourceAsset(
                    new SourceAssetReference(sourceId, key),
                    key,
                    "image/jpeg",
                    10,
                    t0,
                    AssetAvailability.OnlineOnly),
                VerifiedContentHash: null);
            Dictionary<string, ArchiveSourceScanBaseline> baselines = new(StringComparer.Ordinal);

            // An exact spelling still reuses the existing asset.
            IReadOnlyList<ArchiveSourceObservationPersistenceResult> exact =
                await repository.RecordBatchAsync(archive, [Write("Fideli/portrait.jpg")], baselines, t1);
            Assert.Equal(AssetId.From(originalAssetId), Assert.Single(exact).AssetId);

            // A newly observed casing variant must not silently create a second asset.
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.RecordBatchAsync(archive, [Write("fideli/portrait.jpg")], baselines, t1));
            Assert.Contains("case-only", exception.Message, StringComparison.Ordinal);

            await using (NpgsqlConnection verify = new(builder.ConnectionString))
            {
                await verify.OpenAsync();
                using NpgsqlCommand counts = verify.CreateCommand();
                counts.CommandText = """
                    SELECT
                        (SELECT COUNT(*) FROM assets WHERE source_id = @source),
                        (SELECT COUNT(*) FROM assets WHERE source_id = @source
                            AND source_key = 'fideli/portrait.jpg'),
                        (SELECT COUNT(*) FROM asset_revisions WHERE id = @revision
                            AND asset_id = @asset),
                        (SELECT COUNT(*) FROM photo_person_actions WHERE asset_revision_id = @revision),
                        (SELECT COUNT(*) FROM photo_tag_actions WHERE asset_revision_id = @revision);
                    """;
                counts.Parameters.AddWithValue("source", sourceId.Value);
                counts.Parameters.AddWithValue("asset", originalAssetId);
                counts.Parameters.AddWithValue("revision", originalRevisionId);
                await using NpgsqlDataReader reader = await counts.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
                Assert.Equal(1L, reader.GetInt64(3));
                Assert.Equal(1L, reader.GetInt64(4));
            }

            // A tombstone at the original casing must not be evaded by another casing.
            PostgresSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(sourceId, "Private/hidden.jpg", t1);
            InvalidOperationException privateAlias = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.RecordBatchAsync(archive, [Write("private/hidden.jpg")], baselines, t1));
            Assert.Contains("excluded source path", privateAlias.Message, StringComparison.Ordinal);

            await using NpgsqlConnection verifyExclusion = new(builder.ConnectionString);
            await verifyExclusion.OpenAsync();
            using NpgsqlCommand missing = verifyExclusion.CreateCommand();
            missing.CommandText =
                "SELECT COUNT(*) FROM assets WHERE source_id = @source AND source_key = 'private/hidden.jpg';";
            missing.Parameters.AddWithValue("source", sourceId.Value);
            Assert.Equal(0L, (long)(await missing.ExecuteScalarAsync())!);
        }
        finally
        {
            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedName} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
