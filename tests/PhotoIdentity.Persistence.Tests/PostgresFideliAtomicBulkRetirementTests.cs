using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresFideliAtomicBulkRetirementTests
{
    [Fact]
    public async Task All_pairs_commit_or_none_and_protected_history_blocks_the_whole_set()
    {
        string? adminString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminString)) return;

        string databaseName = "photoidentity_fideli_bulk_" + Guid.NewGuid().ToString("N");
        string quoted = '"' + databaseName + '"';
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
                Database = databaseName, Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            await database.InitializeAsync();

            DateTimeOffset t0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
            DateTimeOffset t1 = t0.AddHours(1);
            Guid source = Guid.NewGuid();
            string hash = new('a', 64);
            List<ArchiveCaseAliasRetirementRequest> pairs = [];
            Dictionary<Guid, long> reviews = [];
            Guid protectedFace = Guid.Empty;

            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using (NpgsqlCommand addSource = con.CreateCommand())
                {
                    addSource.CommandText = """
                        INSERT INTO sources(id, kind, root_locator, created_at_utc)
                        VALUES (@id, 'local-folder', 'disposable-bulk-fixture', @t0);
                        """;
                    addSource.Parameters.AddWithValue("id", source);
                    addSource.Parameters.AddWithValue("t0", t0);
                    await addSource.ExecuteNonQueryAsync();
                }

                for (int i = 0; i < 2; i++)
                {
                    Guid lower = Guid.NewGuid(), upper = Guid.NewGuid();
                    Guid lowerRev = Guid.NewGuid(), upperRev = Guid.NewGuid();
                    Guid face = Guid.NewGuid();
                    string filename = $"photo{i}.jpg";
                    using NpgsqlCommand seed = con.CreateCommand();
                    seed.CommandText = """
                        INSERT INTO assets (id, source_id, source_key, media_kind, created_at_utc)
                        VALUES (@lower, @source, @lowerkey, 'photo', @t1),
                               (@upper, @source, @upperkey, 'photo', @t0);
                        INSERT INTO asset_revisions
                            (id, asset_id, content_sha256, size_bytes,
                             observed_at_utc, media_type)
                        VALUES (@lowerrev, @lower, @hash, 100, @t1, 'image/jpeg'),
                               (@upperrev, @upper, @hash, 100, @t1, 'image/jpeg');
                        INSERT INTO archive_source_observations
                            (asset_id, observed_size_bytes, observed_last_write_utc,
                             observed_media_type, observed_at_utc, verification_state,
                             verified_revision_id, verified_size_bytes,
                             verified_last_write_utc, verified_media_type, verified_at_utc)
                        VALUES (@lower, 100, @t1, 'image/jpeg', @t1, 'verified',
                                    @lowerrev, 100, @t1, 'image/jpeg', @t1),
                               (@upper, 100, @t1, 'image/jpeg', @t1, 'verified',
                                    @upperrev, 100, @t1, 'image/jpeg', @t1);
                        INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                        VALUES (@face, @upperrev, 0, @t1);
                        INSERT INTO review_actions (face_occurrence_id, action_kind, actor, created_at_utc)
                        VALUES (@face, 'unknown', 'identity-matcher:auto', @t1);
                        """;
                    seed.Parameters.AddWithValue("lower", lower);
                    seed.Parameters.AddWithValue("upper", upper);
                    seed.Parameters.AddWithValue("source", source);
                    seed.Parameters.AddWithValue("lowerkey", $"fideli/{filename}");
                    seed.Parameters.AddWithValue("upperkey", $"Fideli/{filename}");
                    seed.Parameters.AddWithValue("lowerrev", lowerRev);
                    seed.Parameters.AddWithValue("upperrev", upperRev);
                    seed.Parameters.AddWithValue("face", face);
                    seed.Parameters.AddWithValue("hash", hash);
                    seed.Parameters.AddWithValue("t0", t0);
                    seed.Parameters.AddWithValue("t1", t1);
                    await seed.ExecuteNonQueryAsync();
                    pairs.Add(new ArchiveCaseAliasRetirementRequest(
                        source, lower, upper, $"fideli/{filename}", $"Fideli/{filename}",
                        lowerRev, upperRev, t1, t0, hash, 100, "disposable-bulk-test"));
                    reviews.Add(upper, 1L);
                    if (i == 1) protectedFace = face;
                }

                using NpgsqlCommand protect = con.CreateCommand();
                protect.CommandText = """
                    INSERT INTO review_actions (face_occurrence_id, action_kind, actor, created_at_utc)
                    VALUES (@face, 'unknown', 'human-operator', @t1);
                    """;
                protect.Parameters.AddWithValue("face", protectedFace);
                protect.Parameters.AddWithValue("t1", t1);
                await protect.ExecuteNonQueryAsync();
            }

            PostgresArchiveCaseAliasRetirementService service = new(database);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RetireFideliLowercaseCanonicalBulkAsync(pairs, reviews));

            await using (NpgsqlConnection inspect = new(builder.ConnectionString))
            {
                await inspect.OpenAsync();
                using NpgsqlCommand state = inspect.CreateCommand();
                state.CommandText = """
                    SELECT (SELECT count(*) FROM assets WHERE source_id=@source AND deleted_at_utc IS NULL),
                           (SELECT count(*) FROM archive_case_alias_retirements WHERE source_id=@source);
                    """;
                state.Parameters.AddWithValue("source", source);
                await using NpgsqlDataReader reader = await state.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(4L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
            }

            await using (NpgsqlConnection con = new(builder.ConnectionString))
            {
                await con.OpenAsync();
                using NpgsqlCommand unprotect = con.CreateCommand();
                unprotect.CommandText = """
                    DELETE FROM review_actions
                    WHERE face_occurrence_id = @face AND actor = 'human-operator';
                    """;
                unprotect.Parameters.AddWithValue("face", protectedFace);
                Assert.Equal(1, await unprotect.ExecuteNonQueryAsync());
            }

            Assert.Equal(2, await service.RetireFideliLowercaseCanonicalBulkAsync(pairs, reviews));
            await using (NpgsqlConnection inspect = new(builder.ConnectionString))
            {
                await inspect.OpenAsync();
                using NpgsqlCommand state = inspect.CreateCommand();
                state.CommandText = """
                    SELECT
                        (SELECT count(*) FROM assets
                         WHERE source_id=@source AND deleted_at_utc IS NULL
                           AND source_key LIKE 'fideli/%'),
                        (SELECT count(*) FROM assets
                         WHERE source_id=@source AND deleted_at_utc IS NOT NULL
                           AND source_key LIKE 'Fideli/%'),
                        (SELECT count(*) FROM archive_case_alias_retirements
                         WHERE source_id=@source AND restored_at_utc IS NULL),
                        (SELECT count(*) FROM asset_revisions WHERE asset_id = ANY(@assets)),
                        (SELECT count(*) FROM review_actions WHERE actor = 'identity-matcher:auto'
                           AND face_occurrence_id IN (
                               SELECT face.id FROM face_occurrences face
                               JOIN asset_revisions rev ON rev.id=face.asset_revision_id
                               WHERE rev.asset_id = ANY(@assets)));
                    """;
                state.Parameters.AddWithValue("source", source);
                state.Parameters.AddWithValue("assets",
                    pairs.SelectMany(p => new[] { p.SurvivorAssetId, p.SecondaryAssetId }).ToArray());
                await using NpgsqlDataReader reader = await state.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(2L, reader.GetInt64(1));
                Assert.Equal(2L, reader.GetInt64(2));
                Assert.Equal(4L, reader.GetInt64(3));
                Assert.Equal(2L, reader.GetInt64(4));
            }

            foreach (ArchiveCaseAliasRetirementRequest pair in pairs)
                Assert.Equal(ArchiveAliasRetirementOutcome.Restored, await service.RestoreAsync(pair));
        }
        finally
        {
            using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quoted} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
