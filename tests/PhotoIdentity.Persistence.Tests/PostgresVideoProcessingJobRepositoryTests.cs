using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresVideoProcessingJobRepositoryTests
{
    [Fact]
    public async Task Jobs_are_revision_bound_fenced_and_recovered_without_touching_photo_jobs()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_video_jobs_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection adminConnection = new(
            new NpgsqlConnectionStringBuilder(admin) { Pooling = false }.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder settings = new(admin)
            {
                Database = name,
                Pooling = false,
            };
            await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
            await database.InitializeAsync();
            await database.InitializeAsync();
            Guid source = Guid.NewGuid();
            Guid videoAsset = Guid.NewGuid();
            Guid videoRevision = Guid.NewGuid();
            Guid photoAsset = Guid.NewGuid();
            Guid photoRevision = Guid.NewGuid();
            DateTimeOffset now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','fixture-video-source',@now);
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@video_asset,@source,'clip.mov',@now,'video'),
                           (@photo_asset,@source,'still.jpg',@now,'photo');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                                                observed_at_utc,media_type,media_kind)
                    VALUES (@video_revision,@video_asset,repeat('a',64),5000,@now,'video/quicktime','video'),
                           (@photo_revision,@photo_asset,repeat('b',64),5000,@now,'image/jpeg','photo');
                    INSERT INTO video_revision_metadata(asset_revision_id,inspection_status,
                        duration_seconds,inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@video_revision,'supported',6,@now,repeat('c',64),'fixture-video-v1');
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("video_asset", videoAsset);
                seed.Parameters.AddWithValue("photo_asset", photoAsset);
                seed.Parameters.AddWithValue("video_revision", videoRevision);
                seed.Parameters.AddWithValue("photo_revision", photoRevision);
                seed.Parameters.AddWithValue("now", now);
                await seed.ExecuteNonQueryAsync();
            }
            await using (NpgsqlConnection inspect = await database.OpenConnectionAsync())
            await using (NpgsqlCommand count = inspect.CreateCommand())
            {
                count.CommandText = """
                    SELECT
                        (SELECT count(*) FROM photo_identity_schema_migrations WHERE version=34),
                        (SELECT count(*) FROM video_processing_jobs),
                        (SELECT count(*) FROM processing_jobs),
                        (SELECT count(*) FROM assets WHERE media_kind='photo');
                    """;
                await using NpgsqlDataReader reader = await count.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
                Assert.Equal(1L, reader.GetInt64(3));
            }

            VideoProcessingBudget budget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 6000, MaximumTemporaryBytes: 50000,
                MaximumExtractedFrames: 16);
            VideoProcessingCandidate candidate = new(true, false, true, true, 5000, 10000, 12);
            var queue = new PostgresVideoProcessingJobRepository(database);
            VideoDerivativeJobIdentity Identity(string preset, AssetRevisionId? revision = null) =>
                new(revision ?? AssetRevisionId.From(videoRevision),
                    VideoDerivativeKind.PlayableProxy, "wi0188-v1", preset, new string('e', 64));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(Identity("p1"), new VideoProcessingBudget(), candidate));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(Identity("p1", AssetRevisionId.From(photoRevision)), budget, candidate));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(Identity("p1"), budget, candidate with { IsLocallyAvailable = false }));

            Guid job = await queue.EnqueueAsync(Identity("p1"), budget, candidate);
            Assert.Equal(job, await queue.EnqueueAsync(Identity("p1"), budget, candidate));
            Assert.Equal("queued", (await queue.GetAsync(job))?.Status);

            var inspector = new PostgresVideoProcessingQueueInspector(database);
            VideoProcessingQueueOverview initialSummary = await inspector.GetOverviewAsync();
            Assert.Equal(1, initialSummary.Queued);
            Assert.Equal(0, initialSummary.Running);
            Assert.Equal(0, initialSummary.ReservedTemporaryBytes);
            VideoProcessingJobOperatorRow listed = Assert.Single(
                await inspector.ListRecentAsync());
            Assert.Equal(job, listed.JobId);
            Assert.Equal("queued", listed.Status);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                inspector.ListRecentAsync(51));

            Guid cancelledBeforeStart = await queue.EnqueueAsync(Identity("cancelled"), budget, candidate);
            Assert.True(await queue.RequestCancellationAsync(cancelledBeforeStart));
            Assert.Equal("cancelled", (await queue.GetAsync(cancelledBeforeStart))?.Status);
            Assert.False(await queue.RequestCancellationAsync(cancelledBeforeStart));

            Assert.Null(await queue.TryClaimNextAsync(new VideoProcessingBudget(), false));
            Assert.Null(await queue.TryClaimNextAsync(budget with { Paused = true }, false));
            Assert.Null(await queue.TryClaimNextAsync(budget, true));

            VideoProcessingJobLease first = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget, false));
            Assert.Equal(job, first.Id);
            Assert.Equal("PlayableProxy", first.DerivativeKind);
            Assert.Equal(10000, first.TemporaryBytes);
            Assert.Equal(1, (await queue.GetAsync(job))?.Attempts);
            Assert.False(await queue.TryHeartbeatAsync(job, Guid.NewGuid(), 10));
            Assert.True(await queue.TryHeartbeatAsync(job, first.LeaseToken, 45));
            Assert.Equal(45, (await queue.GetAsync(job))?.ProgressPercent);
            Assert.Null(await queue.TryClaimNextAsync(budget, false)); // one active job
            VideoProcessingQueueOverview runningSummary = await inspector.GetOverviewAsync();
            Assert.Equal(1, runningSummary.Running);
            Assert.Equal(10000, runningSummary.ReservedTemporaryBytes);

            Assert.True(await queue.TryFinishAttemptAsync(job, first.LeaseToken,
                succeeded: false, retryable: true));
            Assert.Equal("queued", (await queue.GetAsync(job))?.Status);
            await ExecuteAsync(database, """
                UPDATE video_processing_jobs
                SET available_at_utc=now()-interval '1 second'
                WHERE id=@id;
                """, job);
            VideoProcessingJobLease second = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget, false));
            Assert.Equal(job, second.Id);
            Assert.NotEqual(first.LeaseToken, second.LeaseToken);
            Assert.False(await queue.TryHeartbeatAsync(job, first.LeaseToken, 50));
            Assert.False(await queue.TryFinishAttemptAsync(job, first.LeaseToken, true, false));

            // Simulate process crash: the second lease expires and a fresh claim
            // recovers the job. The old lease can no longer acknowledge it.
            await ExecuteAsync(database, """
                UPDATE video_processing_jobs
                SET leased_until_utc=now()-interval '1 second'
                WHERE id=@id;
                """, job);
            VideoProcessingJobLease third = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget, false));
            Assert.Equal(3, (await queue.GetAsync(job))?.Attempts);
            Assert.NotEqual(second.LeaseToken, third.LeaseToken);
            Assert.False(await queue.TryFinishAttemptAsync(job, second.LeaseToken, true, false));

            await ExecuteAsync(database, """
                UPDATE video_processing_jobs
                SET leased_until_utc=now()-interval '1 second'
                WHERE id=@id;
                """, job);
            Assert.Null(await queue.TryClaimNextAsync(budget, false));
            VideoProcessingJobState exhausted = Assert.IsType<VideoProcessingJobState>(
                await queue.GetAsync(job));
            Assert.Equal("failed", exhausted.Status);
            Assert.Equal("attempt-limit", exhausted.FailureCode);
            Assert.Equal(3, exhausted.Attempts);
            Assert.Equal(job, await queue.EnqueueAsync(Identity("p1"), budget, candidate));
            Assert.Equal("failed", (await queue.GetAsync(job))?.Status);

            Guid runningCancel = await queue.EnqueueAsync(Identity("p2"), budget, candidate);
            VideoProcessingJobLease cancellable = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget, false));
            Assert.Equal(runningCancel, cancellable.Id);
            Assert.True(await queue.RequestCancellationAsync(runningCancel));
            Assert.False(await queue.TryHeartbeatAsync(runningCancel, cancellable.LeaseToken, 50));
            Assert.False(await queue.TryFinishAttemptAsync(runningCancel, cancellable.LeaseToken, true, false));
            await ExecuteAsync(database, """
                UPDATE video_processing_jobs
                SET leased_until_utc=now()-interval '1 second'
                WHERE id=@id;
                """, runningCancel);
            Assert.Equal(1, await queue.RecoverExpiredAsync());
            Assert.Equal("cancelled", (await queue.GetAsync(runningCancel))?.Status);

            Guid successId = await queue.EnqueueAsync(Identity("p3"), budget, candidate);
            VideoProcessingJobLease successful = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget, false));
            Assert.Equal(successId, successful.Id);
            Assert.True(await queue.TryFinishAttemptAsync(successId, successful.LeaseToken, true, false));
            Assert.Equal("succeeded", (await queue.GetAsync(successId))?.Status);

            // New revision is higher-priority than queued historical work.
            Guid staleJob = await queue.EnqueueAsync(Identity("stale"), budget, candidate);
            Guid newestRevision = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand revised = connection.CreateCommand())
            {
                revised.CommandText = """
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                                                observed_at_utc,media_type,media_kind)
                    VALUES (@id,@asset,repeat('d',64),5000,@new_time,'video/quicktime','video');
                    """;
                revised.Parameters.AddWithValue("id", newestRevision);
                revised.Parameters.AddWithValue("asset", videoAsset);
                revised.Parameters.AddWithValue("new_time", now.AddMinutes(1));
                await revised.ExecuteNonQueryAsync();
            }
            Assert.Null(await queue.TryClaimNextAsync(budget, false));
            Assert.Equal("queued", (await queue.GetAsync(staleJob))?.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(Identity("another"), budget, candidate));

            // A running lease must cease to be usable immediately when its
            // source copy is excluded, even if the lease has not expired.
            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand supported = connection.CreateCommand())
            {
                supported.CommandText = """
                    INSERT INTO video_revision_metadata(asset_revision_id,inspection_status,
                        duration_seconds,inspected_at_utc,tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',6,@now,repeat('c',64),'fixture-video-v1');
                    """;
                supported.Parameters.AddWithValue("revision", newestRevision);
                supported.Parameters.AddWithValue("now", now.AddMinutes(1));
                await supported.ExecuteNonQueryAsync();
            }
            Guid newestJob = await queue.EnqueueAsync(
                Identity("exclusion-race", AssetRevisionId.From(newestRevision)), budget, candidate);
            VideoProcessingJobLease excludedLease = Assert.IsType<VideoProcessingJobLease>(
                await queue.TryClaimNextAsync(budget, false));
            Assert.Equal(newestJob, excludedLease.Id);

            var exclusions = new PostgresSourceCopyExclusionRepository(database);
            await exclusions.ExcludeAsync(SourceId.From(source), "clip.mov", now.AddMinutes(2));
            Assert.False(await queue.TryHeartbeatAsync(newestJob, excludedLease.LeaseToken, 25));
            Assert.False(await queue.TryFinishAttemptAsync(
                newestJob, excludedLease.LeaseToken, succeeded: true, retryable: false));
            Assert.Null(await queue.GetAsync(staleJob));
            Assert.Null(await queue.TryClaimNextAsync(budget, false));
            Assert.Empty(await inspector.ListRecentAsync());
            VideoProcessingQueueOverview hidden = await inspector.GetOverviewAsync();
            Assert.Equal(0, hidden.Queued + hidden.Running + hidden.Succeeded +
                            hidden.Failed + hidden.Cancelled);

            await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
            await using (NpgsqlCommand inspect = connection.CreateCommand())
            {
                inspect.CommandText = """
                    SELECT
                        (SELECT count(*) FROM processing_jobs),
                        (SELECT count(*) FROM processing_runs),
                        (SELECT count(*) FROM asset_revisions WHERE media_kind='photo');
                    """;
                await using NpgsqlDataReader reader = await inspect.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
            }
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteAsync(
        PostgresCatalogueDatabase database, string sql, Guid jobId)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", jobId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}
