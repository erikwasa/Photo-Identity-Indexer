using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

/// <summary>
/// A disposable PostgreSQL rehearsal. No production retirement/apply entrypoint
/// exists in this development slice.
/// </summary>
public sealed class PostgresArchiveCaseAliasRetirementMappingTests
{
    [Fact]
    public async Task A_retired_alias_rescans_into_survivor_without_reactivation_and_can_be_restored()
    {
        string? adminString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
        {
            return;
        }

        string name = "photoidentity_retired_alias_" + Guid.NewGuid().ToString("N");
        string quoted = '"' + name.Replace("\"", "\"\"") + '"';
        NpgsqlConnectionStringBuilder adminBuilder = new(adminString) { Pooling = false };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quoted};";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder builder = new(adminString)
            {
                Database = name,
                Pooling = false
            };
            await using PostgresCatalogueDatabase db = new(builder.ConnectionString);
            await db.InitializeAsync();

            SourceId source = SourceId.New();
            Guid oldAsset = Guid.NewGuid();
            Guid newAsset = Guid.NewGuid();
            Guid oldRevision = Guid.NewGuid();
            Guid newRevision = Guid.NewGuid();
            Guid face = Guid.NewGuid();
            Guid person = Guid.NewGuid();
            DateTimeOffset t0 = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
            DateTimeOffset t1 = t0.AddHours(1);
            DateTimeOffset t2 = t0.AddHours(2);
            DateTimeOffset t3 = t0.AddHours(3);
            DateTimeOffset t4 = t0.AddHours(4);
            string hash = new('a', 64);
            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand seed = con.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'archive-root', @t0);
                    INSERT INTO assets(
                        id, source_id, source_key, created_at_utc,
                        last_seen_at_utc, deleted_at_utc)
                    VALUES
                        (@old_asset, @source, 'Fideli/portrait.jpg', @t0, @t1, NULL),
                        (@new_asset, @source, 'fideli/portrait.jpg', @t1, @t1, @t2);
                    INSERT INTO asset_revisions(
                        id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES
                        (@old_revision, @old_asset, @hash, 100, @t0, 'image/jpeg'),
                        (@new_revision, @new_asset, @hash, 100, @t1, 'image/jpeg');
                    INSERT INTO archive_source_observations(
                        asset_id, observed_size_bytes, observed_last_write_utc,
                        observed_media_type, observed_at_utc, verification_state,
                        verified_revision_id, verified_size_bytes,
                        verified_last_write_utc, verified_media_type, verified_at_utc)
                    VALUES
                        (@old_asset, 100, @t0, 'image/jpeg', @t1, 'verified',
                            @old_revision, 100, @t0, 'image/jpeg', @t1),
                        (@new_asset, 100, @t0, 'image/jpeg', @t1, 'verified',
                            @new_revision, 100, @t0, 'image/jpeg', @t1);
                    INSERT INTO people (id, display_name, created_at_utc)
                    VALUES (@person, 'Synthetic Person', @t0);
                    INSERT INTO photo_tags(
                        normalized_name, display_name, created_by, created_at_utc)
                    VALUES ('synthetic', 'Synthetic', 'test', @t0);
                    INSERT INTO photo_tag_actions(
                        asset_revision_id, tag_id, action_kind, actor, created_at_utc)
                    SELECT @old_revision, id, 'add', 'human', @t0
                    FROM photo_tags WHERE normalized_name = 'synthetic';
                    INSERT INTO face_occurrences(
                        id, asset_revision_id, ordinal, created_at_utc)
                    VALUES (@face, @new_revision, 0, @t1);
                    INSERT INTO review_actions(
                        face_occurrence_id, action_kind, actor, created_at_utc)
                    VALUES (@face, 'unknown', 'identity-matcher:auto', @t1);
                    INSERT INTO archive_case_alias_retirements(
                        secondary_asset_id, survivor_asset_id, source_id,
                        secondary_source_key, survivor_source_key,
                        expected_sha256, retired_at_utc, retired_by)
                    VALUES (
                        @new_asset, @old_asset, @source,
                        'fideli/portrait.jpg', 'Fideli/portrait.jpg',
                        @hash, @t2, 'disposable-test');
                    """;
                seed.Parameters.AddWithValue("source", source.Value);
                seed.Parameters.AddWithValue("old_asset", oldAsset);
                seed.Parameters.AddWithValue("new_asset", newAsset);
                seed.Parameters.AddWithValue("old_revision", oldRevision);
                seed.Parameters.AddWithValue("new_revision", newRevision);
                seed.Parameters.AddWithValue("face", face);
                seed.Parameters.AddWithValue("person", person);
                seed.Parameters.AddWithValue("hash", hash);
                seed.Parameters.AddWithValue("t0", t0);
                seed.Parameters.AddWithValue("t1", t1);
                seed.Parameters.AddWithValue("t2", t2);
                await seed.ExecuteNonQueryAsync();
            }

            PostgresExactDuplicateRepository duplicateInventory = new(db);
            Assert.Empty(await duplicateInventory.GetGroupsAsync(source));

            PostgresArchiveSourceScanBatchRepository scan = new(db);
            ArchiveCatalogueSource catalogueSource = new(
                source, "local-folder", "archive-root", t0);

            ArchiveSourceScanWrite MakeWrite(string key = "fideli/portrait.jpg") => new(
                new SourceAsset(
                    new SourceAssetReference(source, key),
                    key,
                    "image/jpeg",
                    100, t0, AssetAvailability.Local),
                new Sha256Digest(hash));

            // The second-spelling file is still what Windows enumerates.
            // Persist to the original asset, never to the soft-retired one.
            IReadOnlyList<ArchiveSourceObservationPersistenceResult> res =
                await scan.RecordBatchAsync(catalogueSource, [MakeWrite()],
                    new Dictionary<string, ArchiveSourceScanBaseline>(StringComparer.Ordinal), t3);
            Assert.Equal(AssetId.From(oldAsset), Assert.Single(res).AssetId);

            _ = await scan.MarkMissingAssetsAsync(
                source, null, ["fideli/portrait.jpg"], t3);

            // A third capitalization cannot insert another asset or mark
            // the original as missing on a Windows case-insensitive source.
            IReadOnlyList<ArchiveSourceObservationPersistenceResult> thirdCase =
                await scan.RecordBatchAsync(
                    catalogueSource, [MakeWrite("FIDELI/portrait.jpg")],
                    new Dictionary<string, ArchiveSourceScanBaseline>(StringComparer.Ordinal),
                    t3.AddMinutes(1));
            Assert.Equal(AssetId.From(oldAsset), Assert.Single(thirdCase).AssetId);
            _ = await scan.MarkMissingAssetsAsync(
                source, null, ["FIDELI/portrait.jpg"], t3.AddMinutes(1));

            await using (NpgsqlConnection verify = new(builder.ConnectionString))
            {
                await verify.OpenAsync();
                using NpgsqlCommand command = verify.CreateCommand();
                command.CommandText = """
                    SELECT
                        (SELECT count(*) FROM assets WHERE source_id = @source),
                        (SELECT count(*) FROM assets
                            WHERE id = @old_asset AND deleted_at_utc IS NULL),
                        (SELECT count(*) FROM assets
                            WHERE id = @new_asset AND deleted_at_utc IS NOT NULL),
                        (SELECT count(*) FROM asset_revisions WHERE id IN (@old_revision,@new_revision)),
                        (SELECT count(*) FROM photo_tag_actions WHERE asset_revision_id = @old_revision),
                        (SELECT count(*) FROM review_actions WHERE face_occurrence_id = @face),
                        (SELECT verified_revision_id FROM archive_source_observations
                            WHERE asset_id = @old_asset);
                    """;
                command.Parameters.AddWithValue("source", source.Value);
                command.Parameters.AddWithValue("old_asset", oldAsset);
                command.Parameters.AddWithValue("new_asset", newAsset);
                command.Parameters.AddWithValue("old_revision", oldRevision);
                command.Parameters.AddWithValue("new_revision", newRevision);
                command.Parameters.AddWithValue("face", face);
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
                Assert.Equal(2L, reader.GetInt64(3));
                Assert.Equal(1L, reader.GetInt64(4));
                Assert.Equal(1L, reader.GetInt64(5));
                Assert.Equal(oldRevision, reader.GetGuid(6));
            }

            // If a retired row somehow becomes active, fail before a scan write.
            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand corrupt = con.CreateCommand();
                corrupt.CommandText =
                    "UPDATE assets SET deleted_at_utc = NULL WHERE id = @asset;";
                corrupt.Parameters.AddWithValue("asset", newAsset);
                await corrupt.ExecuteNonQueryAsync();
            }
            InvalidOperationException invalid = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scan.RecordBatchAsync(catalogueSource, [MakeWrite()],
                    new Dictionary<string, ArchiveSourceScanBaseline>(StringComparer.Ordinal), t3));
            Assert.Contains("inconsistent", invalid.Message);

            // Restore the synthetic active retirement state after the
            // deliberate corruption. Verify that visibility is still hidden.
            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand repairFixture = con.CreateCommand();
                repairFixture.CommandText =
                    "UPDATE assets SET deleted_at_utc = @time WHERE id = @asset;";
                repairFixture.Parameters.AddWithValue("time", t2);
                repairFixture.Parameters.AddWithValue("asset", newAsset);
                await repairFixture.ExecuteNonQueryAsync();
            }
            Assert.Empty(await duplicateInventory.GetGroupsAsync(source));

            // A rollback restores visibility and exact source-key routing.
            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand restore = con.CreateCommand();
                restore.CommandText = """
                    UPDATE archive_case_alias_retirements
                    SET restored_at_utc = @time, restored_by = 'disposable-test'
                    WHERE secondary_asset_id = @asset;
                    UPDATE assets
                    SET deleted_at_utc = NULL
                    WHERE id = @asset;
                    """;
                restore.Parameters.AddWithValue("time", t4);
                restore.Parameters.AddWithValue("asset", newAsset);
                await restore.ExecuteNonQueryAsync();
            }

            IReadOnlyList<ArchiveSourceObservationPersistenceResult> afterRestore =
                await scan.RecordBatchAsync(catalogueSource, [MakeWrite()],
                    new Dictionary<string, ArchiveSourceScanBaseline>(StringComparer.Ordinal), t4);
            Assert.Equal(AssetId.From(newAsset), Assert.Single(afterRestore).AssetId);
            IReadOnlyList<ExactDuplicateGroup> duplicates =
                await duplicateInventory.GetGroupsAsync(source);
            Assert.Equal(2, Assert.Single(duplicates).Copies.Count);
        }
        finally
        {
            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quoted} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
