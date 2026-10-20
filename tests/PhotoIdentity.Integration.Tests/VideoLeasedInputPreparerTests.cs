using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoLeasedInputPreparerTests
{
    [Fact]
    public async Task Current_lease_can_stage_but_source_change_cannot_leave_partial_copy()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string databaseName = "photoidentity_video_stage_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-stage-src-" + Guid.NewGuid().ToString("N"));
        string scratch = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-stage-tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(scratch);
        await using NpgsqlConnection adminConnection = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            byte[] bytes = Enumerable.Range(0, 8192).Select(i => (byte)i).ToArray();
            string file = Path.Combine(root, "staged.mp4");
            await File.WriteAllBytesAsync(file, bytes);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var settings = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = databaseName, Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
            await database.InitializeAsync();
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(), revision = Guid.NewGuid();
            await using (NpgsqlConnection conn = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = conn.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder',@root,now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'staged.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,@hash,@size,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,duration_seconds,inspected_at_utc,
                        tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',6,now(),repeat('c',64),'fixture-stage-v1');
                    UPDATE video_processing_control
                    SET enabled=true,paused=false WHERE singleton_id=1;
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("root", root);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                seed.Parameters.AddWithValue("hash", hash);
                seed.Parameters.AddWithValue("size", (long)bytes.Length);
                await seed.ExecuteNonQueryAsync();
            }

            var queue = new PostgresVideoProcessingJobRepository(database);
            VideoProcessingBudget budget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 100000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 8);
            Guid job = await queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(AssetRevisionId.From(revision),
                    VideoDerivativeKind.PlayableProxy, "wi0188-v1", "unit-stage",
                    new string('d',64)), budget,
                new VideoProcessingCandidate(true,false,true,true,
                    bytes.Length,20000,4));
            VideoProcessingJobLease lease = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));
            Assert.Equal(job, lease.Id);

            var preparer = new VideoLeasedInputPreparer(database);
            string stagedPath;
            await using (var staged = await preparer.PrepareAsync(lease, scratch))
            {
                stagedPath = staged.FilePath;
                Assert.Equal(bytes, await File.ReadAllBytesAsync(stagedPath));
                Assert.False(stagedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
            }
            Assert.False(File.Exists(stagedPath));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));

            // Same length but altered contents: file metadata alone cannot
            // authorize a video input after source mutation.
            bytes[0] ^= 0xff;
            await File.WriteAllBytesAsync(file, bytes);
            await Assert.ThrowsAsync<IOException>(() => preparer.PrepareAsync(lease, scratch));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
            Directory.Delete(root, recursive: true);
            Directory.Delete(scratch, recursive: true);
        }
    }
}
