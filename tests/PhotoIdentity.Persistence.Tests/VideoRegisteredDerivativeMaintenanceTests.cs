using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class VideoRegisteredDerivativeMaintenanceTests
{
    [Fact]
    public async Task Invalid_excluded_missing_corrupt_receipts_are_revoked_before_private_unlink()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING"))) return;
        await using Fixture f = await Fixture.CreateAsync();
        var lifecycle = new VideoRegisteredDerivativeMaintenance(f.Db);
        Guid excluded = await f.RegisterAsync('a', "excluded.mp4", [1, 2, 3, 4]);
        Guid good = await f.RegisterAsync('b', "healthy.mp4", [1, 2, 3, 5]);
        Guid corrupt = await f.RegisterAsync('c', "corrupt.mp4", [1, 2, 3, 6]);
        Guid missing = await f.RegisterAsync('d', "missing.mp4", [1, 2, 3, 7]);

        // A fixture can delete/change private cache bytes without touching
        // the catalogue; SHA checks catch same-size corruption.
        File.WriteAllBytes(f.PathFor(corrupt), [7, 6, 5, 4]);
        File.Delete(f.PathFor(missing));
        await f.SqlAsync("""
            INSERT INTO source_copy_exclusions(
                source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
            VALUES (@source,'excluded.mp4',now(),'pending',now());
            """);

        string unknown = Path.Combine(f.Root, "video-derivatives", "unknown.txt");
        File.WriteAllText(unknown, "never delete");

        VideoRegisteredMaintenanceReport preview = await lifecycle.SweepAsync(
            f.Root, verifyHashes: true);
        Assert.True(preview.DryRun);
        Assert.Equal(3, preview.Planned);
        Assert.Equal(1, preview.Invalid);
        Assert.Equal(1, preview.Missing);
        Assert.Equal(1, preview.Corrupt);
        Assert.Equal(0, preview.Revoked);
        Assert.True(File.Exists(f.PathFor(excluded)));
        Assert.True(File.Exists(f.PathFor(corrupt)));
        Assert.True(File.Exists(f.PathFor(good)));
        Assert.True(File.Exists(unknown));
        Assert.NotNull(await new PostgresVideoPublishedDerivativeRepository(f.Db)
            .GetCurrentAsync(good));
        Assert.Null(await new PostgresVideoPublishedDerivativeRepository(f.Db)
            .GetCurrentAsync(excluded));

        VideoRegisteredMaintenanceReport applied = await lifecycle.SweepAsync(
            f.Root, verifyHashes: true, apply: true);
        Assert.False(applied.DryRun);
        Assert.Equal(3, applied.Revoked);
        Assert.Equal(2, applied.FilesDeleted);
        Assert.Equal(0, applied.DeleteFailures);
        Assert.False(File.Exists(f.PathFor(excluded)));
        Assert.False(File.Exists(f.PathFor(corrupt)));
        Assert.True(File.Exists(f.PathFor(good)));
        Assert.True(File.Exists(unknown));
        Assert.Equal("failed", await f.StatusAsync(excluded));
        Assert.Equal("failed", await f.StatusAsync(missing));
        Assert.Equal("failed", await f.StatusAsync(corrupt));
        Assert.Equal("succeeded", await f.StatusAsync(good));
        Assert.Equal(1L, await f.ReceiptCountAsync());
        Assert.False(await lifecycle.RequeueRevokedAsync(excluded));

        // Reversible source exclusion requires EXPLICIT requeue of the
        // now-current identity; neither maintenance nor restore enables a worker.
        await f.SqlAsync("""
            DELETE FROM source_copy_exclusions
            WHERE source_id=@source AND source_key='excluded.mp4';
            """);
        Assert.True(await lifecycle.RequeueRevokedAsync(excluded));
        Assert.False(await lifecycle.RequeueRevokedAsync(excluded));
        Assert.Equal("queued", await f.StatusAsync(excluded));
        Assert.Equal(1L, await f.ReceiptCountAsync());
        Assert.True(File.Exists(f.PathFor(good)));
    }

    [Fact]
    public async Task Quota_reclaims_oldest_current_receipt_without_deleting_other_private_files()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING"))) return;
        await using Fixture f = await Fixture.CreateAsync();
        var lifecycle = new VideoRegisteredDerivativeMaintenance(f.Db);

        Guid older = await f.RegisterAsync('a', "old.mp4",
            new byte[4], encodedBytes: 40L * 1024 * 1024, ageHours: 120);
        Guid newer = await f.RegisterAsync('b', "new.mp4",
            new byte[4], encodedBytes: 40L * 1024 * 1024, ageHours: 96);
        VideoRegisteredMaintenanceReport dry = await lifecycle.SweepAsync(
            f.Root, quotaMiB: 64);
        Assert.True(dry.DryRun);
        Assert.Equal(1, dry.Planned);
        Assert.Equal(1, dry.QuotaSelected);
        Assert.Equal(80L * 1024 * 1024, dry.RegisteredBytesBefore);
        Assert.Equal(40L * 1024 * 1024, dry.ProjectedBytesAfter);
        Assert.Equal(2, dry.Inspected);
        Assert.True(File.Exists(f.PathFor(older)));

        VideoRegisteredMaintenanceReport applied = await lifecycle.SweepAsync(
            f.Root, quotaMiB: 64, apply: true);
        Assert.Equal(1, applied.Revoked);
        Assert.Equal(1, applied.FilesDeleted);
        Assert.False(File.Exists(f.PathFor(older)));
        Assert.True(File.Exists(f.PathFor(newer)));
        Assert.Equal("failed", await f.StatusAsync(older));
        Assert.Equal("succeeded", await f.StatusAsync(newer));
        Assert.True(await lifecycle.RequeueRevokedAsync(older));
        Assert.Equal("queued", await f.StatusAsync(older));

        // Requeue is an explicit future retry permission, not an execution.
        Assert.Equal(1L, await f.ReceiptCountAsync());
    }

    [Fact]
    public async Task Disabled_paused_control_and_zero_running_lease_are_strict_preconditions()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING"))) return;
        await using Fixture f = await Fixture.CreateAsync();
        var lifecycle = new VideoRegisteredDerivativeMaintenance(f.Db);
        Guid id = await f.RegisterAsync('a', "video.mp4", [1, 2, 3, 4]);
        await f.SqlAsync("""
            UPDATE video_processing_control SET enabled=true, paused=true
            WHERE singleton_id=1;
            """);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.SweepAsync(f.Root, apply: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.RequeueRevokedAsync(id));
        Assert.True(File.Exists(f.PathFor(id)));

        await f.SqlAsync("""
            UPDATE video_processing_control SET enabled=false, paused=true
            WHERE singleton_id=1;
            UPDATE video_processing_jobs SET status='running',
              lease_token=@lease, leased_until_utc=now()+interval '60 seconds',
              completed_at_utc=NULL
            WHERE id=@job;
            """, job: id, lease: Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.SweepAsync(f.Root));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.RequeueRevokedAsync(id));
        Assert.True(File.Exists(f.PathFor(id)));
    }

    [Fact]
    public async Task Invalid_limits_fail_before_filesystem_or_database_access()
    {
        await using var db = new PostgresCatalogueDatabase(
            "Host=127.0.0.1;Port=1;Database=never_connect;Username=fixture;Pooling=false");
        var lifecycle = new VideoRegisteredDerivativeMaintenance(db);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            lifecycle.SweepAsync("bad-root", maximumRecords: 33));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            lifecycle.SweepAsync("bad-root", quotaMiB: 63));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            lifecycle.SweepAsync("bad-root",
                minimumQuotaAge: TimeSpan.FromHours(23)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            lifecycle.RequeueRevokedAsync(Guid.Empty));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly NpgsqlConnection _admin;
        private readonly Guid _source = Guid.NewGuid();
        private readonly Dictionary<Guid, (string Relative, long Bytes)> _files = [];
        private Fixture(NpgsqlConnection admin, PostgresCatalogueDatabase db,
            string dbName, string root)
        {
            _admin = admin; Db = db; Name = dbName; Root = root;
        }
        public PostgresCatalogueDatabase Db { get; }
        public string Root { get; }
        public string Name { get; }

        public static async Task<Fixture> CreateAsync()
        {
            string adminString = Environment.GetEnvironmentVariable(
                "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING")!;
            var adminBuilder = new NpgsqlConnectionStringBuilder(adminString) { Pooling = false };
            NpgsqlConnection admin = new(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            string dbName = "photoidentity_video_retain_" + Guid.NewGuid().ToString("N");
            await using (NpgsqlCommand create = admin.CreateCommand())
            {
                create.CommandText = $"CREATE DATABASE \"{dbName}\";";
                await create.ExecuteNonQueryAsync();
            }
            string root = Path.Combine(Path.GetTempPath(),
                "photoidentity-video-retain-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, VideoDerivativeFilePublisher.CacheMarker),
                VideoDerivativeFilePublisher.CacheMarkerContents);
            var dbConfig = new NpgsqlConnectionStringBuilder(adminString)
            {
                Database = dbName, Pooling = false
            };
            PostgresCatalogueDatabase db = new(dbConfig.ConnectionString);
            await db.InitializeAsync();
            var f = new Fixture(admin, db, dbName, root);
            await f.SqlAsync("""
                INSERT INTO sources(id,kind,root_locator,created_at_utc)
                VALUES (@source,'local-folder','synthetic-video-source',now());
                """);
            return f;
        }

        public async Task<Guid> RegisterAsync(char keyChar, string sourceKey,
            byte[] bytes, long? encodedBytes = null, int ageHours = 96)
        {
            Guid job = Guid.NewGuid();
            Guid asset = Guid.NewGuid();
            Guid revision = Guid.NewGuid();
            Guid lease = Guid.NewGuid();
            long size = encodedBytes ?? bytes.Length;
            string relative = "video-derivatives/" + job.ToString("N") +
                "/" + lease.ToString("N") + ".mp4";
            string dir = Path.Combine(Root, "video-derivatives", job.ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, lease.ToString("N") + ".mp4");
            using (FileStream output = File.Create(file))
            {
                output.Write(bytes);
                output.SetLength(size);
            }
            // For short synthetic fixtures, hash exactly what the file holds.
            string hash = Convert.ToHexString(
                await SHA256.HashDataAsync(File.OpenRead(file))).ToLowerInvariant();
            _files.Add(job, (relative, size));
            await using NpgsqlConnection connection = await Db.OpenConnectionAsync();
            await using NpgsqlCommand seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                VALUES (@asset,@source,@source_key,now(),'video');
                INSERT INTO asset_revisions(
                    id,asset_id,content_sha256,size_bytes,observed_at_utc,
                    media_type,media_kind)
                VALUES (@rev,@asset,repeat('a',64),1000,now(),'video/mp4','video');
                INSERT INTO video_revision_metadata(
                    asset_revision_id,inspection_status,duration_seconds,
                    inspected_at_utc,tool_sha256,inspection_profile_id)
                VALUES (@rev,'supported',6,now(),repeat('b',64),'synthetic');
                INSERT INTO video_processing_jobs(
                    id,asset_revision_id,idempotency_key,derivative_kind,
                    policy_version,preset_version,tool_sha256,
                    source_bytes,reserved_temporary_bytes,requested_frames,
                    status,attempt_count,completed_at_utc)
                VALUES (@job,@rev,@key,'PlayableProxy','wi0188-v1',
                    'synthetic',repeat('c',64),1000,200000000,0,'succeeded',1,now());
                INSERT INTO video_published_derivatives(
                    job_id,asset_revision_id,artifact_relative_path,
                    content_sha256,encoded_bytes,created_at_utc)
                VALUES (@job,@rev,@path,@hash,@bytes,
                    now()-(@hours * interval '1 hour'));
                """;
            seed.Parameters.AddWithValue("source", _source);
            seed.Parameters.AddWithValue("source_key", sourceKey);
            seed.Parameters.AddWithValue("asset", asset);
            seed.Parameters.AddWithValue("rev", revision);
            seed.Parameters.AddWithValue("job", job);
            seed.Parameters.AddWithValue("key", new string(keyChar, 64));
            seed.Parameters.AddWithValue("path", relative);
            seed.Parameters.AddWithValue("hash", hash);
            seed.Parameters.AddWithValue("bytes", size);
            seed.Parameters.AddWithValue("hours", ageHours);
            await seed.ExecuteNonQueryAsync();
            return job;
        }

        public string PathFor(Guid job) => Path.Combine(
            Root, _files[job].Relative.Replace('/', Path.DirectorySeparatorChar));

        public async Task SqlAsync(string query, Guid? job = null, Guid? lease = null)
        {
            await using NpgsqlConnection connection = await Db.OpenConnectionAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddWithValue("source", _source);
            if (job is Guid j) command.Parameters.AddWithValue("job", j);
            if (lease is Guid l) command.Parameters.AddWithValue("lease", l);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string> StatusAsync(Guid job)
        {
            await using NpgsqlConnection connection = await Db.OpenConnectionAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM video_processing_jobs WHERE id=@job;";
            command.Parameters.AddWithValue("job", job);
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public async Task<long> ReceiptCountAsync()
        {
            await using NpgsqlConnection connection = await Db.OpenConnectionAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM video_published_derivatives;";
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await using NpgsqlCommand drop = _admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
            await _admin.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }
}
