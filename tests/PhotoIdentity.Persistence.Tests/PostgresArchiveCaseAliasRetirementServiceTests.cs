using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresArchiveCaseAliasRetirementServiceTests
{
    [Fact]
    public async Task Explicit_Fideli_lowercase_keeper_overrides_age_and_effective_differences_but_not_protected_history()
    {
        string? adminString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString)) return;

        string dbName = "photoidentity_fideli_policy_" + Guid.NewGuid().ToString("N");
        string quotedName = '"' + dbName + '"';
        NpgsqlConnectionStringBuilder adminBuilder = new(adminString) { Pooling = false };
        await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        using (NpgsqlCommand create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quotedName};";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder builder = new(adminString)
            {
                Database = dbName, Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            await database.InitializeAsync();

            Guid source = Guid.NewGuid(), lower = Guid.NewGuid(), upper = Guid.NewGuid();
            Guid lowerRevision = Guid.NewGuid(), upperRevision = Guid.NewGuid();
            Guid upperFace = Guid.NewGuid();
            DateTimeOffset earlier = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
            DateTimeOffset later = earlier.AddDays(2);
            string hash = new('a', 64);

            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand seed = con.CreateCommand();
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source, 'local-folder', 'root', @earlier);
                    INSERT INTO assets
                        (id, source_id, source_key, media_kind, created_at_utc)
                    VALUES (@lower, @source, 'fideli/photo.jpg', 'photo', @later),
                           (@upper, @source, 'Fideli/photo.jpg', 'photo', @earlier);
                    INSERT INTO asset_revisions
                        (id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
                    VALUES (@lowerrev, @lower, @hash, 100, @later, 'image/jpeg'),
                           (@upperrev, @upper, @hash, 100, @later, 'image/jpeg');
                    INSERT INTO archive_source_observations
                        (asset_id, observed_size_bytes, observed_last_write_utc,
                         observed_media_type, observed_at_utc, verification_state,
                         verified_revision_id, verified_size_bytes,
                         verified_last_write_utc, verified_media_type, verified_at_utc)
                    VALUES
                        (@lower, 100, @later, 'image/jpeg', @later, 'verified',
                         @lowerrev, 100, @later, 'image/jpeg', @later),
                        (@upper, 100, @later, 'image/jpeg', @later, 'verified',
                         @upperrev, 100, @later, 'image/jpeg', @later);
                    INSERT INTO photo_tags
                        (normalized_name, display_name, created_by, created_at_utc)
                    VALUES ('reviewed', 'Reviewed', 'test', @later);
                    INSERT INTO photo_tag_actions
                        (asset_revision_id, tag_id, action_kind, actor, created_at_utc)
                    SELECT @lowerrev, id, 'add', 'manual', @later
                    FROM photo_tags WHERE normalized_name='reviewed';
                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES (@upperface, @upperrev, 0, @later);
                    INSERT INTO review_actions
                        (face_occurrence_id, action_kind, actor, created_at_utc)
                    VALUES (@upperface, 'unknown', 'identity-matcher:auto', @later);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("lower", lower);
                seed.Parameters.AddWithValue("upper", upper);
                seed.Parameters.AddWithValue("lowerrev", lowerRevision);
                seed.Parameters.AddWithValue("upperrev", upperRevision);
                seed.Parameters.AddWithValue("upperface", upperFace);
                seed.Parameters.AddWithValue("hash", hash);
                seed.Parameters.AddWithValue("earlier", earlier);
                seed.Parameters.AddWithValue("later", later);
                await seed.ExecuteNonQueryAsync();
            }

            ArchiveCaseAliasRetirementRequest request = new(
                source, lower, upper, "fideli/photo.jpg", "Fideli/photo.jpg",
                lowerRevision, upperRevision, later, earlier, hash, 100, "disposable-test");
            PostgresArchiveCaseAliasRetirementService service = new(database);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetireAsync(request));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RetireFideliLowercaseCanonicalAsync(request with
                {
                    SurvivorSourceKey = "other/photo.jpg",
                    SecondarySourceKey = "Other/photo.jpg"
                }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RetireFideliLowercaseCanonicalAsync(request with
                {
                    ExpectedSha256 = new string('b', 64)
                }));

            // An explicit upper human action always blocks the canonical policy.
            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand action = con.CreateCommand();
                action.CommandText = """
                    INSERT INTO review_actions
                        (face_occurrence_id, action_kind, actor, created_at_utc)
                    VALUES (@face, 'unknown', 'human-operator', @time);
                    """;
                action.Parameters.AddWithValue("face", upperFace);
                action.Parameters.AddWithValue("time", later);
                Assert.Equal(1, await action.ExecuteNonQueryAsync());
            }
            InvalidOperationException protectedHistory = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RetireFideliLowercaseCanonicalAsync(request));
            Assert.Contains("protected", protectedHistory.Message, StringComparison.Ordinal);

            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand cleanup = con.CreateCommand();
                cleanup.CommandText = """
                    DELETE FROM review_actions
                    WHERE face_occurrence_id = @face AND actor = 'human-operator';
                    """;
                cleanup.Parameters.AddWithValue("face", upperFace);
                Assert.Equal(1, await cleanup.ExecuteNonQueryAsync());
            }

            Assert.Equal(ArchiveAliasRetirementOutcome.Retired,
                await service.RetireFideliLowercaseCanonicalAsync(request));
            Assert.Equal(ArchiveAliasRetirementOutcome.AlreadyRetired,
                await service.RetireFideliLowercaseCanonicalAsync(request));

            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand inspect = con.CreateCommand();
                inspect.CommandText = """
                    SELECT
                        (SELECT count(*) FROM assets WHERE id = @lower AND deleted_at_utc IS NULL),
                        (SELECT count(*) FROM assets WHERE id = @upper AND deleted_at_utc IS NOT NULL),
                        (SELECT count(*) FROM asset_revisions WHERE asset_id IN (@lower, @upper)),
                        (SELECT count(*) FROM photo_tag_actions WHERE asset_revision_id=@lowerrev),
                        (SELECT count(*) FROM review_actions WHERE face_occurrence_id=@upperface),
                        (SELECT count(*) FROM archive_case_alias_retirements
                         WHERE secondary_asset_id=@upper AND restored_at_utc IS NULL);
                    """;
                inspect.Parameters.AddWithValue("lower", lower);
                inspect.Parameters.AddWithValue("upper", upper);
                inspect.Parameters.AddWithValue("lowerrev", lowerRevision);
                inspect.Parameters.AddWithValue("upperface", upperFace);
                await using NpgsqlDataReader reader = await inspect.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                for (int i = 0; i < 6; i++) Assert.Equal(i == 2 ? 2L : 1L, reader.GetInt64(i));
            }

            Assert.Equal(ArchiveAliasRetirementOutcome.Restored, await service.RestoreAsync(request));
        }
        finally
        {
            using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedName} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

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

            // A third capitalization makes the normalized path ambiguous.
            Guid thirdAlias = Guid.NewGuid();
            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand addThird = connection.CreateCommand();
                addThird.CommandText = """
                    INSERT INTO assets(
                        id, source_id, source_key, created_at_utc, last_seen_at_utc)
                    VALUES (@asset, @source, 'FIDELI/photo.jpg', @t1, @t1);
                    """;
                addThird.Parameters.AddWithValue("asset", thirdAlias);
                addThird.Parameters.AddWithValue("source", source);
                addThird.Parameters.AddWithValue("t1", t1);
                await addThird.ExecuteNonQueryAsync();
            }
            InvalidOperationException thirdBlocked = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RetireAsync(request));
            Assert.Contains("exact pair", thirdBlocked.Message, StringComparison.Ordinal);

            // A different effective current tag on the keeper also blocks
            // retirement, even though the later asset has no protected edits.
            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand cleanupAndTag = connection.CreateCommand();
                cleanupAndTag.CommandText = """
                    DELETE FROM assets WHERE id = @third;
                    INSERT INTO photo_tag_actions(
                        asset_revision_id, tag_id, action_kind, actor, created_at_utc)
                    SELECT @revision, id, 'add', 'manual-test', @time
                    FROM photo_tags WHERE normalized_name = 'preserve-history';
                    """;
                cleanupAndTag.Parameters.AddWithValue("third", thirdAlias);
                cleanupAndTag.Parameters.AddWithValue("revision", survivorCurrentRevision);
                cleanupAndTag.Parameters.AddWithValue("time", t1);
                await cleanupAndTag.ExecuteNonQueryAsync();
            }
            InvalidOperationException unequal = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RetireAsync(request));
            Assert.Contains("effective", unequal.Message, StringComparison.Ordinal);

            // Remove only the disposable fixture's current tag test action.
            // The keeper's historical manual tag action remains intact.
            await using (NpgsqlConnection connection = new(builder.ConnectionString))
            {
                await connection.OpenAsync();
                using NpgsqlCommand cleanup = connection.CreateCommand();
                cleanup.CommandText = """
                    DELETE FROM photo_tag_actions
                    WHERE asset_revision_id = @revision AND actor='manual-test';
                    """;
                cleanup.Parameters.AddWithValue("revision", survivorCurrentRevision);
                Assert.Equal(1, await cleanup.ExecuteNonQueryAsync());
            }

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

            // Before/after state is intentionally tested through the real
            // active gallery repositories, not only the missing marker.
            FaceOccurrenceId faceId = FaceOccurrenceId.From(face);
            PostgresReviewQueryRepository reviewGallery = new(database);
            PostgresSuggestionGalleryRepository suggestionGallery = new(database);
            ModelId modelId = new("alias-retirement-gallery-test");
            Sha256Digest modelHash = new(new string('c', 64));

            Assert.Single((await reviewGallery.GetFacesAsync(state: "all")).Items);
            Assert.Single((await suggestionGallery.GetFacesAsync(
                modelId, modelHash, 0, 40, "all", null,
                "created-desc", "all", null)).Items);

            Assert.Equal(ArchiveAliasRetirementOutcome.Retired, await service.RetireAsync(request));
            Assert.Equal(ArchiveAliasRetirementOutcome.AlreadyRetired, await service.RetireAsync(request));

            // The history still exists (checked below), but is no longer
            // an active review/identity-suggestion gallery candidate.
            Assert.Empty((await reviewGallery.GetFacesAsync(state: "all")).Items);
            Assert.Null(await reviewGallery.GetFaceAsync(faceId));
            var retiredGallery = await suggestionGallery.GetFacesAsync(
                modelId, modelHash, 0, 40, "all", null,
                "created-desc", "all", null);
            Assert.Empty(retiredGallery.Items);
            Assert.Equal(0, retiredGallery.Total);
            Assert.Null(await suggestionGallery.GetNavigationAsync(
                faceId, modelId, modelHash, "all", null,
                "created-desc", "all", null));
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => new PostgresReviewActionRepository(database).MarkUnknownAsync(
                    faceId, "manual-test", t1));

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
            Assert.Single((await reviewGallery.GetFacesAsync(state: "all")).Items);
            Assert.NotNull(await reviewGallery.GetFaceAsync(faceId));
            Assert.Single((await suggestionGallery.GetFacesAsync(
                modelId, modelHash, 0, 40, "all", null,
                "created-desc", "all", null)).Items);

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
