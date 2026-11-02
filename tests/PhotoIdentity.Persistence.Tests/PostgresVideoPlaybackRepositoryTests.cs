using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoPlaybackRepositoryTests
{
    [Fact]
    public void Proxy_paths_must_match_exact_published_lease()
    {
        Guid job = Guid.NewGuid(), lease = Guid.NewGuid();
        string root = Path.Combine(Path.GetTempPath(), "test-video-cache");
        string relative = $"video-derivatives/{job:N}/{lease:N}.mp4";
        Assert.True(PostgresVideoPlaybackRepository.TryCanonicalPath(
            root, job, relative, out string path));
        Assert.Equal(Path.Combine(root, "video-derivatives", job.ToString("N"),
            lease.ToString("N") + ".mp4"), path);
        Assert.False(PostgresVideoPlaybackRepository.TryCanonicalPath(
            root, job, "../" + relative, out _));
        Assert.False(PostgresVideoPlaybackRepository.TryCanonicalPath(
            root, Guid.NewGuid(), relative, out _));
        Assert.False(PostgresVideoPlaybackRepository.TryCanonicalPath(
            root, job, relative.Replace('/', '\\'), out _));
        Assert.False(PostgresVideoPlaybackRepository.TryCanonicalPath(
            root, job, relative.Replace(".mp4", ".mov"), out _));
    }

    [Fact]
    public async Task Current_retained_proxy_is_read_without_original_and_denies_tamper_eviction_and_exclusion()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string dbName = "photoidentity_video_play_" + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-play-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(folder, "cache");
        string originals = Path.Combine(folder, "originals");
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(
            Path.Combine(cache, VideoDerivativeFilePublisher.CacheMarker),
            VideoDerivativeFilePublisher.CacheMarkerContents);
        // No original is placed at originals: a proxy must remain usable even
        // when its source bytes are not locally available.
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
            var connectionString = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = dbName, Pooling = false
            };
            await using PostgresCatalogueDatabase database = new(
                connectionString.ConnectionString);
            await database.InitializeAsync();
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(),
                revision = Guid.NewGuid();
            await using (NpgsqlConnection db = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = db.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder',@root,now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'offline/clip.mov',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,
                        size_bytes,observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),5000,now(),
                        'video/quicktime','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,color_transfer,duration_seconds,
                        inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported','bt709',6,now(),
                        repeat('b',64),'synthetic');
                    UPDATE video_processing_control SET enabled=true,paused=false
                    WHERE singleton_id=1;
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("root", originals);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                await seed.ExecuteNonQueryAsync();
            }

            VideoProcessingBudget budget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 6000, MaximumTemporaryBytes: 30000,
                MaximumExtractedFrames: 4);
            var queue = new PostgresVideoProcessingJobRepository(database);
            await queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(AssetRevisionId.From(revision),
                    VideoDerivativeKind.PlayableProxy, "wi0188-v1",
                    "wi0188-sdr-h264-aac-720p-pilot-v1", new string('c', 64)),
                budget, new VideoProcessingCandidate(true, false, true, true,
                    5000, 12000, 0));
            VideoProcessingJobLease lease = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));
            string workspace = VideoScratchOwnership.Create(cache, "video-tool");
            string output = Path.Combine(workspace, "output.mp4");
            byte[] mp4 = new byte[128];
            mp4[0] = 0; mp4[1] = 0; mp4[2] = 0; mp4[3] = 24;
            mp4[4] = (byte)'f'; mp4[5] = (byte)'t';
            mp4[6] = (byte)'y'; mp4[7] = (byte)'p';
            await File.WriteAllBytesAsync(output, mp4);
            Assert.True(await new VideoDerivativeFilePublisher(database)
                .PublishAsync(lease, cache, output));
            string file = Path.Combine(cache, VideoDerivativeArtifactPath.ForLease(lease)
                .Replace('/', Path.DirectorySeparatorChar));
            var playback = new PostgresVideoPlaybackRepository(database);

            await using (VideoPlaybackLease? opened = await playback.OpenCurrentAsync(
                AssetRevisionId.From(revision), cache))
            {
                Assert.NotNull(opened);
                byte[] read = new byte[mp4.Length];
                await opened.Stream.ReadExactlyAsync(read);
                Assert.Equal(mp4, read);
                opened.Stream.Position = 17; // source handle is seekable
                Assert.Equal(0, opened.Stream.ReadByte());
            }

            mp4[20] = 42;
            await File.WriteAllBytesAsync(file, mp4);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                playback.OpenCurrentAsync(AssetRevisionId.From(revision), cache));
            mp4[20] = 0;
            await File.WriteAllBytesAsync(file, mp4);

            string moved = file + ".removed";
            File.Move(file, moved);
            await Assert.ThrowsAnyAsync<IOException>(() =>
                playback.OpenCurrentAsync(AssetRevisionId.From(revision), cache));
            File.Move(moved, file);

            await new PostgresSourceCopyExclusionRepository(database)
                .ExcludeAsync(SourceId.From(source), "offline/clip.mov",
                    DateTimeOffset.UtcNow);
            Assert.Null(await playback.OpenCurrentAsync(
                AssetRevisionId.From(revision), cache));
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
