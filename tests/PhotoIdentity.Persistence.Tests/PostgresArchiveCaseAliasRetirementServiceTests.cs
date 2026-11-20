using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresArchiveCaseAliasRetirementServiceTests
{
    [Fact]
    public async Task Retire_and_restore_preserve_original_and_secondary_history_and_reject_stale_or_human_evidence()
    {
        string? adminString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString))
        {
            return;
        }

        string dbName = $"photoidentity_case_retire_{Guid.NewGuid():N}";
        string quotedName = '"' + dbName.Replace("\"", "\"\"") + '"';
        NpgsqlConnectionStringBuilder adminBuilder = new(adminString) { Pooling = false };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (NpgsqlCommand create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quotedName};";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder builder = new(adminString)
            {
                Database = dbName,
                Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            await database.InitializeAsync();

            Guid source = Guid.NewGuid();
            Guid survivor = Guid.NewGuid();
            Guid secondary = Guid.NewGuid();
            Guid survivorOldRevision = Guid.NewGuid();
            Guid survivorCurrentRevision = Guid.NewGuid();
            Guid secondaryCurrentRevision = Guid.NewGuid();
            Guid face = Guid.NewGuid();
            DateTimeOffset t0 = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
            DateTimeOffset t1 = t0.AddHours(1);
            string currentHash = new('a', 64);

            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources(id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'test-root', @t0);
                    INSERT INTO assets(
                        id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES
                        (@survivor, @source, 'Fideli/photo.jpg', @t0, @t1),
                        (@secondary, @source, 'fideli/photo.jpg', @t1, @t1);
                    INSERT INTO asset_revisions(
                        id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES
                        (@old_revision, @survivor, @old_hash, 123, @t0, 'image/jpeg'),
                        (@survivor_revision, @survivor, @hash, 456, @t1, 'image/jpeg'),
                        (@secondary_revision, @secondary, @hash, 456, @t1, 'image/jpeg');
                    INSERT INTO archive_source_observations(
                        asset_id, observed_size_bytes, observed_last_write_utc,
                        observed_media_type, observed_at_utc, verification_state,
                        verified_revision_id, verified_size_bytes,
                        verified_last_write_utc, verified_media_type, verified_at_utc)
                    VALUES
                        (@survivor, 456, @t1, 'image/jpeg', @t1, 'verified',
                            @survivor_revision, 456, @t1, 'image/jpeg', @t1),
                        (@secondary, 456, @t1, 'image/jpeg', @t1, 'verified',
                            @secondary_revision, 456, @t1, 'image/jpeg', @t1);
                    INSERT INTO photo_tags(
                        normalized_name, display_name, created_by, created_at_utc)
                    VALUES ('preserve-history', 'Preserve History', 'manual-test', @t0);
                    INSERT INTO photo_tag_actions(
                        asset_revision_id, tag_id, action_kind, actor, created_at_utc)
                    SELECT @old_revision, id, 'add', 'manual-test', @t0
                    FROM photo_tags WHERE normalized_name = 'preserve-history';
                    INSERT INTO face_occurrences(
                        id, asset_revision_id, ordinal, created_at_utc)
                    VALUES (@face, @secondary_revision, 0, @t1);
                    INSERT INTO review_actions(
                        face_occurrence_id, action_kind, actor, created_at_utc)
                    VALUES (@face, 'unknown', 'identity-matcher:auto', @t1);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("survivor", survivor);
                seed.Parameters.AddWithValue("secondary", secondary);
                seed.Parameters.AddWithValue("old_revision", survivorOldRevision);
                seed.Parameters.AddWithValue("survivor_revision", survivorCurrentRevision);
                seed.Parameters.AddWithValue("secondary_revision", secondaryCurrentRevision);
                seed.Parameters.AddWithValue("face", face);
                seed.Parameters.AddWithValue("hash", currentHash);
                seed.Parameters.AddWithValue("old_hash", new string('b', 64));
                seed.Parameters.AddWithValue("t0", t0);
                seed.Parameters.AddWithValue("t1", t1);
                await seed.ExecuteNonQueryAsync();
            }

            PostgresArchiveCaseAliasRetirementService service = new(database);
            ArchiveCaseAliasRetirementRequest request = new(
                source, survivor, secondary,
                "Fideli/photo.jpg", "fideli/photo.jpg",
                survivorCurrentRevision, secondaryCurrentRevision,
                t0, t1, currentHash, 456, "disposable-test");

            // Stale/private manifest data cannot authorize retirement.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RetireAsync(request with { ExpectedSizeBytes = 455 }));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RetireAsync(request with { SurvivorCreatedAtUtc = t0.AddDays(-1) }));

            // An unrecognized reviewer is treated as human, not disposable.
            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand addHuman = connection.CreateCommand();
                addHuman.CommandText = """
                    INSERT INTO review_actions(
                        face_occurrence_id, action_kind, actor, created_at_utc)
                    VALUES (@face, 'unknown', 'human-test', @time);
                    """;
                addHuman.Parameters.AddWithValue("face", face);
                addHuman.Parameters.AddWithValue("time", t1);
                await addHuman.ExecuteNonQueryAsync();
            }
            InvalidOperationException blocked = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RetireAsync(request));
            Assert.Contains("protected", blocked.Message, StringComparison.Ordinal);

            // Synthetic fixture cleanup in disposable database ONLY.
            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand clearHuman = connection.CreateCommand();
                clearHuman.CommandText = """
                    DELETE FROM review_actions
                    WHERE face_occurrence_id = @face AND actor = 'human-test';
                    """;
                clearHuman.Parameters.AddWithValue("face", face);
                Assert.Equal(1, await clearHuman.ExecuteNonQueryAsync());
            }

            Assert.Equal(ArchiveAliasRetirementOutcome.Retired, await service.RetireAsync(request));
            Assert.Equal(ArchiveAliasRetirementOutcome.AlreadyRetired, await service.RetireAsync(request));

            await using (NpgsqlConnection verify = new(builder.ConnectionString))
            {
                await verify.OpenAsync();
                using NpgsqlCommand query = verify.CreateCommand();
                query.CommandText = """
                    SELECT
                        (SELECT count(*) FROM assets WHERE source_id = @source),
                        (SELECT count(*) FROM assets
                            WHERE id = @survivor AND deleted_at_utc IS NULL),
                        (SELECT count(*) FROM assets
                            WHERE id = @secondary AND deleted_at_utc IS NOT NULL),
                        (SELECT count(*) FROM asset_revisions
                            WHERE asset_id IN (@survivor, @secondary)),
                        (SELECT count(*) FROM photo_tag_actions
                            WHERE asset_revision_id = @old_revision),
                        (SELECT count(*) FROM review_actions
                            WHERE face_occurrence_id = @face),
                        (SELECT count(*) FROM archive_case_alias_retirements
                            WHERE secondary_asset_id = @secondary
                              AND survivor_asset_id = @survivor
                              AND restored_at_utc IS NULL);
                    """;
                query.Parameters.AddWithValue("source", source);
                query.Parameters.AddWithValue("survivor", survivor);
                query.Parameters.AddWithValue("secondary", secondary);
                query.Parameters.AddWithValue("old_revision", survivorOldRevision);
                query.Parameters.AddWithValue("face", face);
                await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
                Assert.Equal(3L, reader.GetInt64(3));
                Assert.Equal(1L, reader.GetInt64(4));
                Assert.Equal(1L, reader.GetInt64(5));
                Assert.Equal(1L, reader.GetInt64(6));
            }

            Assert.Equal(ArchiveAliasRetirementOutcome.Restored, await service.RestoreAsync(request));
            Assert.Equal(ArchiveAliasRetirementOutcome.AlreadyRestored, await service.RestoreAsync(request));

            await using (NpgsqlConnection verify = new(builder.ConnectionString))
            {
                await verify.OpenAsync();
                using NpgsqlCommand query = verify.CreateCommand();
                query.CommandText = """
                    SELECT
                        (SELECT count(*) FROM assets
                         WHERE source_id = @source AND deleted_at_utc IS NULL),
                        (SELECT count(*) FROM archive_case_alias_retirements
                         WHERE secondary_asset_id = @secondary AND restored_at_utc IS NOT NULL),
                        (SELECT count(*) FROM review_actions WHERE face_occurrence_id = @face);
                    """;
                query.Parameters.AddWithValue("source", source);
                query.Parameters.AddWithValue("secondary", secondary);
                query.Parameters.AddWithValue("face", face);
                await using NpgsqlDataReader reader = await query.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
            }

            // A second retirement cannot silently overwrite the restore log.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetireAsync(request));
        }
        finally
        {
            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedName} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
