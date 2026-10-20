using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class VideoScopedOneShotControlTests
{
    private const string Preset = "wi0188-sdr-h264-aac-720p-pilot-v1";
    private static readonly string FfmpegHash = new('c', 64);

    [Fact]
    public async Task Selected_scope_claims_only_selected_job_and_deadline_revokes_live_lease()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;
        string dbName = "photoidentity_video_scope_" + Guid.NewGuid().ToString("N");
        var adminSettings = new NpgsqlConnectionStringBuilder(admin) { Pooling = false };
        await using NpgsqlConnection administrator = new(adminSettings.ConnectionString);
        await administrator.OpenAsync();
        await using (NpgsqlCommand create = administrator.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{dbName}\";";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(admin)
                { Database = dbName, Pooling = false };
            await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
            await database.InitializeAsync();
            Assert.Equal(37, PostgresCatalogueDatabase.CurrentSchemaVersion);
            var scoped = new PostgresVideoScopedAttemptControl(database);
            var queue = new PostgresVideoProcessingJobRepository(database);
            var safety = new PostgresVideoLeaseSafetyProbe(database);

            Guid source = Guid.NewGuid(), earlierRevision = Guid.NewGuid(),
                selectedRevision = Guid.NewGuid();
            await using (NpgsqlConnection conn = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = conn.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','synthetic-selected-v37',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@firstAsset,@source,'first.mp4',now(),'video'),
                           (@secondAsset,@source,'second.mp4',now(),'video');
                    INSERT INTO asset_revisions(
                        id,asset_id,content_sha256,size_bytes,observed_at_utc,
                        media_type,media_kind)
                    VALUES (@first,@firstAsset,repeat('a',64),4000,now(),
                            'video/mp4','video'),
                           (@second,@secondAsset,repeat('b',64),4000,now(),
                            'video/mp4','video');
                    INSERT INTO video_revision_metadata(
                        asset_revision_id,inspection_status,duration_seconds,
                        inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@first,'supported',3,now(),repeat('d',64),'fixture'),
                           (@second,'supported',3,now(),repeat('d',64),'fixture');
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("firstAsset", Guid.NewGuid());
                seed.Parameters.AddWithValue("secondAsset", Guid.NewGuid());
                seed.Parameters.AddWithValue("first", earlierRevision);
                seed.Parameters.AddWithValue("second", selectedRevision);
                await seed.ExecuteNonQueryAsync();
            }
            VideoProcessingBudget budget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 5000, MaximumTemporaryBytes: 80000,
                MaximumExtractedFrames: 1);
            var candidate = new VideoProcessingCandidate(
                true, false, true, true, 4000, 20000, 0);
            Guid firstJob = await queue.EnqueueAsync(new VideoDerivativeJobIdentity(
                AssetRevisionId.From(earlierRevision),
                VideoDerivativeKind.PlayableProxy, "wi0188-v1", Preset,
                FfmpegHash), budget, candidate);
            Guid selectedJob = await queue.EnqueueAsync(new VideoDerivativeJobIdentity(
                AssetRevisionId.From(selectedRevision),
                VideoDerivativeKind.PlayableProxy, "wi0188-v1", Preset,
                FfmpegHash), budget, candidate);

            Assert.Null(await queue.TryClaimNextAsync(budget));
            Assert.Null(await scoped.ArmAsync(selectedJob, new string('f', 64),
                Preset, TimeSpan.FromMinutes(2)));
            Assert.Null(await scoped.ArmAsync(selectedJob, FfmpegHash,
                "wrong-preset", TimeSpan.FromMinutes(2)));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                scoped.ArmAsync(selectedJob, FfmpegHash, Preset,
                    TimeSpan.FromMinutes(16)));

            VideoScopedAttemptGrant grant = Assert.IsType<VideoScopedAttemptGrant>(
                await scoped.ArmAsync(selectedJob, FfmpegHash,
                    Preset, TimeSpan.FromMinutes(2)));
            Assert.Equal(selectedJob, grant.JobId);
            Assert.Null(await scoped.ArmAsync(firstJob, FfmpegHash,
                Preset, TimeSpan.FromMinutes(2)));

            VideoProcessingJobLease lease = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget));
            Assert.Equal(selectedJob, lease.Id);
            Assert.Equal("queued", (await queue.GetAsync(firstJob))?.Status);
            Assert.True((await safety.CheckAsync(lease)).Allowed);
            Assert.True(await queue.TryHeartbeatAsync(lease.Id, lease.LeaseToken, 12));

            // The absolute expiry in PostgreSQL itself is a fence, even
            // without another CLI process alive to issue disarm.
            await using (NpgsqlConnection conn = await database.OpenConnectionAsync())
            await using (NpgsqlCommand expire = conn.CreateCommand())
            {
                expire.CommandText = """
                    UPDATE video_processing_control SET
                    authorization_expires_at_utc=now()-interval '1 second'
                    WHERE singleton_id=1;
                    """;
                Assert.Equal(1, await expire.ExecuteNonQueryAsync());
            }
            Assert.False((await safety.CheckAsync(lease)).Allowed);
            Assert.False(await queue.TryHeartbeatAsync(lease.Id, lease.LeaseToken, 50));
            Assert.False(await new PostgresVideoPublishedDerivativeRepository(database)
                .TryCommitAsync(lease,
                    VideoDerivativeArtifactPath.ForLease(lease),
                    new string('e', 64), 100));
            Assert.Null(await queue.TryClaimNextAsync(budget));

            Assert.True(await scoped.DisarmAsync(grant));
            Assert.False(await scoped.DisarmAsync(grant));
            Assert.Equal("queued", (await queue.GetAsync(firstJob))?.Status);
            Assert.True((await queue.GetAsync(selectedJob))?.CancellationRequested);
            VideoProcessingControlState control =
                await new PostgresVideoProcessingControlRepository(database).GetAsync();
            Assert.False(control.Enabled);
            Assert.True(control.Paused);
        }
        finally
        {
            await using NpgsqlCommand drop = administrator.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
