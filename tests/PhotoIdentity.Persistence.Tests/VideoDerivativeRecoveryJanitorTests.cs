using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class VideoDerivativeRecoveryJanitorTests
{
    [Fact]
    public async Task Private_cleanup_preserves_receipts_and_sources_but_reclaims_old_orphans()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string dbName = "photoidentity_deriv_recovery_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, VideoDerivativeFilePublisher.CacheMarker),
            VideoDerivativeFilePublisher.CacheMarkerContents);

        await using NpgsqlConnection adminConnection = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{dbName}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var config = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = dbName, Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(config.ConnectionString);
            await database.InitializeAsync();
            Assert.Equal(36, PostgresCatalogueDatabase.CurrentSchemaVersion);
            var janitor = new VideoDerivativeRecoveryJanitor(database);
            Guid source = Guid.NewGuid();
            Guid asset = Guid.NewGuid();
            Guid revision = Guid.NewGuid();
            Guid orphanId = Guid.NewGuid();
            Guid keptId = Guid.NewGuid();
            Guid oldLease = Guid.NewGuid();
            Guid keptLease = Guid.NewGuid();
            Guid recentLease = Guid.NewGuid();

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-source',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'private.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),1000,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,duration_seconds,inspected_at_utc,
                        tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',6,now(),repeat('b',64),'synthetic');
                    INSERT INTO video_processing_jobs(
                        id,asset_revision_id,idempotency_key,derivative_kind,
                        policy_version,preset_version,tool_sha256,
                        source_bytes,reserved_temporary_bytes,requested_frames,
                        status,attempt_count,completed_at_utc,failure_code)
                    VALUES
                        (@orphan,@revision,repeat('c',64),'PlayableProxy',
                         'wi0188-v1','synthetic',repeat('d',64),1000,4000,0,
                         'failed',1,now(),'worker-error'),
                        (@kept,@revision,repeat('e',64),'PlayableProxy',
                         'wi0188-v1','synthetic',repeat('d',64),1000,4000,0,
                         'succeeded',1,now(),NULL);
                    INSERT INTO video_published_derivatives(
                        job_id,asset_revision_id,artifact_relative_path,
                        content_sha256,encoded_bytes)
                    VALUES (@kept,@revision,@path,repeat('f',64),4);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                seed.Parameters.AddWithValue("orphan", orphanId);
                seed.Parameters.AddWithValue("kept", keptId);
                seed.Parameters.AddWithValue("path", Relative(keptId, keptLease));
                await seed.ExecuteNonQueryAsync();
            }

            string orphan = WriteFile(root, orphanId, oldLease, old: true);
            string kept = WriteFile(root, keptId, keptLease, old: true);
            string recent = WriteFile(root, orphanId, recentLease, old: false);
            string unrelated = Path.Combine(root, "video-derivatives", "unowned-file.txt");
            File.WriteAllText(unrelated, "keep");

            VideoDerivativeRecoveryReport preview = await janitor.SweepAsync(
                root, TimeSpan.FromHours(48));
            Assert.True(preview.DryRun);
            Assert.Equal(3, preview.Considered);
            Assert.Equal(1, preview.Eligible);
            Assert.Equal(0, preview.Deleted);
            Assert.True(preview.Protected >= 2);
            Assert.True(preview.Refused >= 1);
            Assert.True(File.Exists(orphan));
            Assert.True(File.Exists(kept));
            Assert.True(File.Exists(recent));

            // A too-small apply budget must abort *before* deleting an
            // earlier eligible file. There are three known candidate files.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                janitor.SweepAsync(root, TimeSpan.FromHours(48),
                    maximumFiles: 2, apply: true));
            Assert.True(File.Exists(orphan));
            Assert.True(File.Exists(kept));

            VideoDerivativeRecoveryReport applied = await janitor.SweepAsync(
                root, TimeSpan.FromHours(48), apply: true);
            Assert.Equal(1, applied.Deleted);
            Assert.False(File.Exists(orphan));
            Assert.True(File.Exists(kept));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(unrelated));
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand lookup = connection.CreateCommand())
            {
                lookup.CommandText = """
                    SELECT count(*) FROM video_published_derivatives
                    WHERE job_id=@kept;
                    """;
                lookup.Parameters.AddWithValue("kept", keptId);
                Assert.Equal(1L, (long)(await lookup.ExecuteScalarAsync())!);
            }

            // Excluded evidence is hidden from the current API but remains a
            // registered receipt; orphan cleanup MUST NOT remove its file.
            await ExecAsync(database, """
                INSERT INTO source_copy_exclusions(
                    source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                VALUES (@source,'private.mp4',now(),'pending',now());
                """, source, "source");
            VideoDerivativeRecoveryReport excluded = await janitor.SweepAsync(
                root, TimeSpan.FromHours(48), apply: true);
            Assert.Equal(0, excluded.Deleted);
            Assert.Equal(1, excluded.InvalidatedRegistered);
            Assert.True(File.Exists(kept));

            // Active lease recovery and operator stop are mandatory. A
            // paused-but-enabled lane is not sufficient for destructive work.
            await ExecAsync(database, """
                UPDATE video_processing_control
                SET enabled=true, paused=true WHERE singleton_id=1;
                """, Guid.Empty);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                janitor.SweepAsync(root, TimeSpan.FromHours(48), apply: true));
            await ExecAsync(database, """
                UPDATE video_processing_control
                SET enabled=false, paused=true WHERE singleton_id=1;
                UPDATE video_processing_jobs
                SET status='running', completed_at_utc=NULL, failure_code=NULL,
                    lease_token=@id, leased_until_utc=now()+interval '60 seconds'
                WHERE id=@job;
                """, recentLease, "id", orphanId, "job");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                janitor.SweepAsync(root, TimeSpan.FromHours(48)));
            Assert.True(File.Exists(kept));

            // Archive/cache overlap is rejected even for preview.
            await ExecAsync(database, """
                UPDATE video_processing_jobs
                SET status='failed', completed_at_utc=now(),
                    failure_code='worker-error', lease_token=NULL,leased_until_utc=NULL
                WHERE id=@job;
                UPDATE sources SET root_locator=@root WHERE id=@source;
                """, orphanId, "job", source, "source", root, "root");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                janitor.SweepAsync(root, TimeSpan.FromHours(48)));
        }
        finally
        {
            await using (NpgsqlCommand drop = adminConnection.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
                await drop.ExecuteNonQueryAsync();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_scan_limits_are_refused_before_touching_a_database()
    {
        var database = new PostgresCatalogueDatabase(
            "Host=127.0.0.1;Port=1;Database=never_connect;Username=fixture;Pooling=false");
        await using (database)
        {
            var janitor = new VideoDerivativeRecoveryJanitor(database);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                janitor.SweepAsync("not-a-root", TimeSpan.FromHours(24)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                janitor.SweepAsync("not-a-root", TimeSpan.FromHours(48), maximumFiles: 65));
        }
    }

    private static string Relative(Guid job, Guid token) =>
        "video-derivatives/" + job.ToString("N") + "/" + token.ToString("N") + ".mp4";

    private static string WriteFile(string root, Guid job, Guid token, bool old)
    {
        string dir = Path.Combine(root, "video-derivatives", job.ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, token.ToString("N") + ".mp4");
        File.WriteAllBytes(path, [0, 0, 0, 4]);
        if (old) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-4));
        return path;
    }

    private static async Task ExecAsync(
        PostgresCatalogueDatabase database, string sql, Guid first,
        string firstName = "id", Guid? second = null, string secondName = "second",
        string? text = null, string textName = "text")
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (first != Guid.Empty) command.Parameters.AddWithValue(firstName, first);
        if (second is Guid item) command.Parameters.AddWithValue(secondName, item);
        if (text is not null) command.Parameters.AddWithValue(textName, text);
        await command.ExecuteNonQueryAsync();
    }
}
