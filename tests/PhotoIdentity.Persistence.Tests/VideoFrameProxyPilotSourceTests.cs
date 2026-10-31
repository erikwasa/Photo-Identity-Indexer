using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

/// <summary>
/// Optional isolated PostgreSQL coverage. Never touches the real catalogue
/// or any personal video. Built and exercised by CI's disposable database.
/// </summary>
public sealed class VideoFrameProxyPilotSourceTests
{
    [Fact]
    public async Task Registered_snapshot_is_sha_checked_and_exclusion_or_corruption_refuses()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string dbName = "photoidentity_frame_pilot_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-pilot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, VideoDerivativeFilePublisher.CacheMarker),
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
                Database = dbName,
                Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(config.ConnectionString);
            await database.InitializeAsync();
            await new PostgresSourceCopyExclusionRepository(database)
                .EnsureReadyAsync();

            Guid source = Guid.NewGuid();
            Guid asset = Guid.NewGuid();
            Guid revision = Guid.NewGuid();
            Guid job = Guid.NewGuid();
            Guid lease = Guid.NewGuid();

            byte[] bytes = Enumerable.Range(0, 512).Select(i => (byte)(i % 251)).ToArray();
            string relative = "video-derivatives/" + job.ToString("N") + "/" +
                lease.ToString("N") + ".mp4";
            string folder = Path.Combine(root, "video-derivatives", job.ToString("N"));
            Directory.CreateDirectory(folder);
            string registeredFile = Path.Combine(folder, lease.ToString("N") + ".mp4");
            await File.WriteAllBytesAsync(registeredFile, bytes);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-unused-source',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'fixture.mp4',now(),'video');
                    INSERT INTO asset_revisions(
                        id,asset_id,content_sha256,size_bytes,observed_at_utc,
                        media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),1000,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(
                        asset_revision_id,inspection_status,duration_seconds,
                        color_transfer,inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',6,'bt709',now(),repeat('b',64),'synthetic');
                    INSERT INTO video_processing_jobs(
                        id,asset_revision_id,idempotency_key,derivative_kind,
                        policy_version,preset_version,tool_sha256,
                        source_bytes,reserved_temporary_bytes,requested_frames,
                        status,attempt_count,completed_at_utc)
                    VALUES (@job,@revision,repeat('c',64),'PlayableProxy',
                        'wi0188-v1','synthetic',repeat('d',64),1000,200000000,0,
                        'succeeded',1,now());
                    INSERT INTO video_published_derivatives(
                        job_id,asset_revision_id,artifact_relative_path,
                        content_sha256,encoded_bytes)
                    VALUES (@job,@revision,@relative,@sha,@size);
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                seed.Parameters.AddWithValue("job", job);
                seed.Parameters.AddWithValue("relative", relative);
                seed.Parameters.AddWithValue("sha", hash);
                seed.Parameters.AddWithValue("size", bytes.Length);
                await seed.ExecuteNonQueryAsync();
            }

            var pilot = new PostgresVideoFrameProxyPilotSource(database);
            string stage = Path.Combine(root, "stage-one.mp4");
            VideoFrameProxySnapshot valid = Assert.IsType<VideoFrameProxySnapshot>(
                await pilot.CopyCurrentProxyAsync(job, root, stage));
            Assert.Equal(revision, valid.RevisionId);
            Assert.Equal(6m, valid.DurationSeconds);
            Assert.Equal(bytes.Length, valid.ProxyBytes);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(stage));
            Assert.True(await pilot.StillCurrentAsync(job, revision));
            File.Delete(stage);

            // Same-size corruption is detected *after* staging; the owned
            // staged output is deleted while the original cache file remains.
            byte[] corrupt = (byte[])bytes.Clone();
            corrupt[7] ^= 0xff;
            await File.WriteAllBytesAsync(registeredFile, corrupt);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                pilot.CopyCurrentProxyAsync(job, root, stage));
            Assert.False(File.Exists(stage));
            Assert.True(File.Exists(registeredFile));
            await File.WriteAllBytesAsync(registeredFile, bytes);

            // Source-copy exclusion makes receipt invisible. No temp copy.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand exclude = connection.CreateCommand())
            {
                exclude.CommandText = """
                    INSERT INTO source_copy_exclusions(
                        source_id,source_key,excluded_at_utc,purge_state,purge_updated_at_utc)
                    VALUES (@source,'fixture.mp4',now(),'pending',now());
                    """;
                exclude.Parameters.AddWithValue("source", source);
                await exclude.ExecuteNonQueryAsync();
            }
            Assert.Null(await pilot.CopyCurrentProxyAsync(job, root, stage));
            Assert.False(File.Exists(stage));
            Assert.False(await pilot.StillCurrentAsync(job, revision));
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
