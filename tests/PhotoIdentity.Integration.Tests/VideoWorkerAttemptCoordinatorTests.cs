using System.Text.Json;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoWorkerAttemptCoordinatorTests
{
    [Fact]
    public void Failure_stages_are_allowlisted_and_messages_never_leave_the_diagnostic()
    {
        var source = new IOException(@"C:\Users\private\OneDrive\family.mov");
        VideoAttemptFailureDiagnostics.Mark(source, "source-staging");
        VideoAttemptFailureDiagnostics.Mark(source, "tool-execution");
        VideoAttemptFailureDiagnostic diagnostic =
            VideoAttemptFailureDiagnostics.FromException(source);
        Assert.Equal("source-staging", diagnostic.Stage);
        Assert.Equal("filesystem-io", diagnostic.FailureClass);
        Assert.Null(diagnostic.SupervisorCheckpoint);
        Assert.Null(diagnostic.ToolExitCode);
        Assert.DoesNotContain("OneDrive", JsonSerializer.Serialize(diagnostic));

        Assert.Throws<ArgumentException>(() =>
            VideoAttemptFailureDiagnostics.Mark(source, @"C:\sensitive.mov"));

        var tool = new InvalidOperationException("path-and-arguments-private");
        VideoToolFailureEvidence.Mark(tool, "io-accounting-unavailable");
        VideoAttemptFailureDiagnostics.Mark(tool, "tool-execution");
        VideoAttemptFailureDiagnostic toolDiagnostic =
            VideoAttemptFailureDiagnostics.FromException(tool);
        Assert.Equal("tool-execution", toolDiagnostic.Stage);
        Assert.Equal("execution-safety-rejected", toolDiagnostic.FailureClass);
        Assert.Equal("io-accounting-unavailable", toolDiagnostic.SupervisorCheckpoint);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(toolDiagnostic));

        // Even a future adapter reporting arbitrary strings cannot leak them
        // through the settings endpoint's in-memory diagnostic.
        var malformed = new VideoAttemptFailureDiagnostic(
            @"C:\private\source.mov", @"ffmpeg says private-source",
            @"C:\private\cache", 123);
        VideoAttemptFailureDiagnostic scrubbed =
            VideoAttemptFailureDiagnostics.Sanitize(malformed);
        Assert.Equal("unclassified", scrubbed.Stage);
        Assert.Equal("unexpected-failure", scrubbed.FailureClass);
        Assert.Null(scrubbed.SupervisorCheckpoint);
        Assert.Equal(123, scrubbed.ToolExitCode);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(scrubbed));
    }

    [Fact]
    public async Task Unsafe_configuration_and_default_off_refuse_before_any_database_access()
    {
        await using PostgresCatalogueDatabase database = new(
            "Host=127.0.0.1;Port=1;Database=never_connect;Username=fixture;Pooling=false");
        var coordinator = new VideoWorkerAttemptCoordinator(database);
        var adapter = new SyntheticAdapter((_, _) =>
            Task.FromResult(new VideoDerivativeAttemptReport(true, false)));

        VideoWorkerCycleResult off = await coordinator.RunOneAsync(
            new VideoProcessingBudget(), adapter);
        Assert.False(off.Claimed);
        Assert.False(off.Published);
        Assert.Equal("caller-disabled", off.ReasonCode);
        Assert.Equal(0, adapter.Calls);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RunOneAsync(
                new VideoProcessingBudget(Enabled: true, Paused: false,
                    MaximumConcurrentJobs: 2), adapter));
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task Worker_cycle_fails_closed_on_publish_claim_and_supports_retryable_failures()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_video_worker_cycle_" + Guid.NewGuid().ToString("N");
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
            var config = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = name, Pooling = false
            };
            await using PostgresCatalogueDatabase db = new(config.ConnectionString);
            await db.InitializeAsync();
            var queue = new PostgresVideoProcessingJobRepository(db);
            var coordinator = new VideoWorkerAttemptCoordinator(db);
            var control = new PostgresVideoProcessingControlRepository(db);

            VideoProcessingBudget budget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 20000, MaximumTemporaryBytes: 80000,
                MaximumExtractedFrames: 8);
            var candidate = new VideoProcessingCandidate(
                true, false, true, true, 10000, 20000, 3);
            Guid source = Guid.NewGuid(), asset = Guid.NewGuid(), revision = Guid.NewGuid();
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand seed = conn.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','worker-cycle-fixture',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@asset,@source,'fixture.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@revision,@asset,repeat('a',64),10000,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,duration_seconds,inspected_at_utc,
                        tool_sha256,inspection_profile_id)
                    VALUES (@revision,'supported',8,now(),repeat('c',64),'fixture-worker-cycle');
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("asset", asset);
                seed.Parameters.AddWithValue("revision", revision);
                await seed.ExecuteNonQueryAsync();
            }
            Task<Guid> JobAsync(string preset) => queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(AssetRevisionId.From(revision),
                    VideoDerivativeKind.PlayableProxy, "wi0188-v1", preset,
                    new string('d', 64)), budget, candidate);

            Guid disabledJob = await JobAsync("not-started");
            var callback = new SyntheticAdapter((_, _) =>
                Task.FromResult(new VideoDerivativeAttemptReport(true, false)));
            VideoWorkerCycleResult stillOff =
                await coordinator.RunOneAsync(budget, callback);
            Assert.Equal("no-admissible-job", stillOff.ReasonCode);
            Assert.Equal(0, callback.Calls);
            Assert.Equal("queued", (await queue.GetAsync(disabledJob))?.Status);

            // Only the isolated disposable test database is enabled via SQL.
            // There is deliberately no production operator enable command.
            await using (NpgsqlConnection conn = await db.OpenConnectionAsync())
            await using (NpgsqlCommand enable = conn.CreateCommand())
            {
                enable.CommandText = """
                    UPDATE video_processing_control
                    SET enabled=true,paused=false WHERE singleton_id=1;
                    """;
                Assert.Equal(1, await enable.ExecuteNonQueryAsync());
            }

            // An adapter saying Published=true WITHOUT a durable success
            // acknowledgement must not produce successful worker status.
            VideoWorkerCycleResult fakeSuccess =
                await coordinator.RunOneAsync(budget, callback);
            Assert.True(fakeSuccess.Claimed);
            Assert.False(fakeSuccess.Published);
            Assert.Equal("publication-required", fakeSuccess.ReasonCode);
            Assert.Equal("failed", (await queue.GetAsync(disabledJob))?.Status);

            Guid retryId = await JobAsync("retryable");
            callback = new SyntheticAdapter((_, _) =>
                Task.FromException<VideoDerivativeAttemptReport>(
                    new IOException("sensitive-source-path.mp4")));
            VideoWorkerCycleResult retry = await coordinator.RunOneAsync(budget, callback);
            Assert.True(retry.Claimed);
            Assert.False(retry.Published);
            Assert.Equal("retry-pending", retry.ReasonCode);
            Assert.Equal("unclassified", retry.Failure?.Stage);
            Assert.Equal("filesystem-io", retry.Failure?.FailureClass);
            Assert.Equal("queued", (await queue.GetAsync(retryId))?.Status);
            Assert.DoesNotContain("sensitive", retry.ReasonCode);

            Guid cancelledId = await JobAsync("paused");
            await control.PauseAsync();
            var neverRun = new SyntheticAdapter((_, _) =>
                throw new InvalidOperationException("Should not run while paused."));
            VideoWorkerCycleResult paused = await coordinator.RunOneAsync(budget, neverRun);
            Assert.Equal("no-admissible-job", paused.ReasonCode);
            Assert.Equal(0, neverRun.Calls);
            Assert.Equal("queued", (await queue.GetAsync(cancelledId))?.Status);
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Selected_video_claim_rechecks_live_included_folders()
    {
        string? admin = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(admin)) return;

        string name = "photoidentity_video_scope_" + Guid.NewGuid().ToString("N");
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
            var config = new NpgsqlConnectionStringBuilder(admin)
            {
                Database = name, Pooling = false
            };
            await using PostgresCatalogueDatabase db = new(config.ConnectionString);
            await db.InitializeAsync();
            var queue = new PostgresVideoProcessingJobRepository(db);
            var control = new PostgresVideoProcessingControlRepository(db);
            VideoProcessingBudget budget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: 20000, MaximumTemporaryBytes: 80000,
                MaximumExtractedFrames: 8);
            var admission = new VideoProcessingCandidate(
                true, false, true, true, 10000, 20000, 3);
            Guid source = Guid.NewGuid();
            Guid removedAsset = Guid.NewGuid(), coveredAsset = Guid.NewGuid();
            Guid removedRevision = Guid.NewGuid(), coveredRevision = Guid.NewGuid();
            await using (NpgsqlConnection connection = await db.OpenConnectionAsync())
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources(id,kind,root_locator,created_at_utc)
                    VALUES (@source,'local-folder','fixture-root',now());
                    INSERT INTO archive_configuration(id,source_id,configured_at_utc)
                    VALUES (1,@source,now());
                    INSERT INTO archive_included_folders(source_id,relative_path,included_at_utc)
                    VALUES (@source,'2024',now());
                    INSERT INTO assets(id,source_id,source_key,created_at_utc,media_kind)
                    VALUES (@removed,@source,'2024/removed/old.mp4',now(),'video'),
                           (@covered,@source,'2024/included/current.mp4',now(),'video');
                    INSERT INTO asset_revisions(id,asset_id,content_sha256,size_bytes,
                        observed_at_utc,media_type,media_kind)
                    VALUES (@removedRevision,@removed,repeat('a',64),10000,now(),'video/mp4','video'),
                           (@coveredRevision,@covered,repeat('b',64),10000,now(),'video/mp4','video');
                    INSERT INTO video_revision_metadata(asset_revision_id,
                        inspection_status,duration_seconds,inspected_at_utc,
                        tool_sha256,inspection_profile_id)
                    VALUES (@removedRevision,'supported',8,now(),repeat('c',64),'scope-fixture'),
                           (@coveredRevision,'supported',8,now(),repeat('c',64),'scope-fixture');
                    """;
                seed.Parameters.AddWithValue("source", source);
                seed.Parameters.AddWithValue("removed", removedAsset);
                seed.Parameters.AddWithValue("covered", coveredAsset);
                seed.Parameters.AddWithValue("removedRevision", removedRevision);
                seed.Parameters.AddWithValue("coveredRevision", coveredRevision);
                await seed.ExecuteNonQueryAsync();
            }

            Task<Guid> Queue(Guid revision) => queue.EnqueueAsync(
                new VideoDerivativeJobIdentity(AssetRevisionId.From(revision),
                    VideoDerivativeKind.PlayableProxy, "wi0188-v1",
                    "coverage-claim", new string('d', 64)), budget, admission);
            Guid old = await Queue(removedRevision);
            Guid current = await Queue(coveredRevision);
            await control.EnableAsync();

            // Coverage changed *after* jobs were queued. The database gate
            // must reject the old job even though it is still queued.
            await using (NpgsqlConnection connection = await db.OpenConnectionAsync())
            await using (NpgsqlCommand narrow = connection.CreateCommand())
            {
                narrow.CommandText = """
                    UPDATE archive_included_folders SET relative_path='2024/included'
                    WHERE source_id=@source;
                    """;
                narrow.Parameters.AddWithValue("source", source);
                await narrow.ExecuteNonQueryAsync();
            }
            Assert.Null(await queue.TryClaimNextAsync(
                budget, photoWorkActive: false, selectedJobId: old,
                requireArchiveCoverage: true));
            Assert.Equal("queued", (await queue.GetAsync(old))?.Status);

            // Explicit selection cannot accidentally claim the older job.
            VideoProcessingJobLease? lease = await queue.TryClaimNextAsync(
                budget, photoWorkActive: false, selectedJobId: current,
                requireArchiveCoverage: true);
            Assert.NotNull(lease);
            Assert.Equal(current, lease!.Id);
            Assert.Equal("running", (await queue.GetAsync(current))?.Status);

            // Replacing coverage again is honored without deleting jobs.
            await using (NpgsqlConnection connection = await db.OpenConnectionAsync())
            await using (NpgsqlCommand narrow = connection.CreateCommand())
            {
                narrow.CommandText = """
                    UPDATE archive_included_folders SET relative_path='2024/removed'
                    WHERE source_id=@source;
                    """;
                narrow.Parameters.AddWithValue("source", source);
                await narrow.ExecuteNonQueryAsync();
            }
            // Current lease remains running; another cannot start concurrently.
            Assert.Null(await queue.TryClaimNextAsync(
                budget, photoWorkActive: false, selectedJobId: old,
                requireArchiveCoverage: true));
            Assert.True(await queue.TryFinishAttemptAsync(
                lease.Id, lease.LeaseToken, succeeded: false, retryable: false));
            VideoProcessingJobLease? restored = await queue.TryClaimNextAsync(
                budget, photoWorkActive: false, selectedJobId: old,
                requireArchiveCoverage: true);
            Assert.NotNull(restored);
            Assert.Equal(old, restored!.Id);
        }
        finally
        {
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class SyntheticAdapter(
        Func<VideoProcessingJobLease, CancellationToken,
            Task<VideoDerivativeAttemptReport>> execute)
        : IVideoPublishedDerivativeAdapter
    {
        public int Calls { get; private set; }

        public Task<VideoDerivativeAttemptReport> ProcessAsync(
            VideoProcessingJobLease lease, CancellationToken token)
        {
            Calls++;
            return execute(lease, token);
        }
    }
}
