using System.Text.Json;
using Xunit;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresCatalogueDatabaseTests
{
    [Fact]
    public async Task TryInitializeAsync_ReportsUnavailable_ForUnreachableServer()
    {
        await using PostgresCatalogueDatabase database = new(
            "Host=127.0.0.1;Port=1;Database=photoidentity;Username=test;Password=test;Pooling=false;Timeout=1");

        PostgresInitializationResult result = await database.TryInitializeAsync();

        Assert.Equal("unavailable", result.Health.Status);
        Assert.True(result.Health.Configured);
        Assert.Null(result.Health.SchemaVersion);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task InitializeAsync_IsVersionedAndIdempotent_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_test_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);

        NpgsqlConnectionStringBuilder adminBuilder =
            new(adminConnectionString)
            {
                Pooling = false,
            };

        await using NpgsqlConnection adminConnection =
            new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();

        await using (NpgsqlCommand createDatabase =
                     adminConnection.CreateCommand())
        {
            createDatabase.CommandText =
                $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder =
                new(adminConnectionString)
                {
                    Database = databaseName,
                    Pooling = false,
                };

            await using PostgresCatalogueDatabase database =
                new(testBuilder.ConnectionString);

            PostgresInitializationResult first =
                await database.TryInitializeAsync();
            PostgresInitializationResult second =
                await database.TryInitializeAsync();

            Assert.Null(first.Error);
            Assert.Equal("ready", first.Health.Status);
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                first.Health.SchemaVersion);

            Assert.Null(second.Error);
            Assert.Equal("ready", second.Health.Status);
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                second.Health.SchemaVersion);

            await using NpgsqlConnection verificationConnection =
                new(testBuilder.ConnectionString);
            await verificationConnection.OpenAsync();

            await using NpgsqlCommand readMigration =
                verificationConnection.CreateCommand();
            readMigration.CommandText =
                """
                SELECT COUNT(*)
                FROM photo_identity_schema_migrations
                WHERE version = @version;
                """;
            readMigration.Parameters.AddWithValue(
                "version",
                PostgresCatalogueDatabase.CurrentSchemaVersion);

            object? count = await readMigration.ExecuteScalarAsync();
            Assert.Equal(1L, Convert.ToInt64(count));

            await using (NpgsqlCommand readFoundationalTables =
                         verificationConnection.CreateCommand())
            {
                readFoundationalTables.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN (
                          'sources',
                          'assets',
                          'asset_revisions',
                          'face_occurrences',
                          'face_observations',
                          'face_crops',
                          'embeddings',
                          'processing_runs',
                          'processing_jobs');
                    """;

                object? tableCount =
                    await readFoundationalTables.ExecuteScalarAsync();
                Assert.Equal(9L, Convert.ToInt64(tableCount));
            }

            await using (NpgsqlCommand readArchiveAnalysisTables =
                         verificationConnection.CreateCommand())
            {
                readArchiveAnalysisTables.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN (
                          'archive_analysis_profiles',
                          'archive_analysis_runs',
                          'asset_revision_analysis');
                    """;

                object? archiveAnalysisTableCount =
                    await readArchiveAnalysisTables.ExecuteScalarAsync();
                Assert.Equal(3L, Convert.ToInt64(archiveAnalysisTableCount));
            }

            await using (NpgsqlCommand readArchiveAvailabilityTable =
                         verificationConnection.CreateCommand())
            {
                readArchiveAvailabilityTable.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name = 'archive_asset_availability';
                    """;

                object? archiveAvailabilityTableCount =
                    await readArchiveAvailabilityTable.ExecuteScalarAsync();
                Assert.Equal(1L, Convert.ToInt64(archiveAvailabilityTableCount));
            }

            await using (NpgsqlCommand readArchiveObservationTable =
                         verificationConnection.CreateCommand())
            {
                readArchiveObservationTable.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name = 'archive_source_observations';
                    """;

                object? archiveObservationTableCount =
                    await readArchiveObservationTable.ExecuteScalarAsync();
                Assert.Equal(1L, Convert.ToInt64(archiveObservationTableCount));
            }

            await using (NpgsqlCommand readArchiveCoverageTables =
                         verificationConnection.CreateCommand())
            {
                readArchiveCoverageTables.CommandText =
                    """
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN (
                          'archive_configuration',
                          'archive_included_folders');
                    """;

                object? archiveCoverageTableCount =
                    await readArchiveCoverageTables.ExecuteScalarAsync();
                Assert.Equal(2L, Convert.ToInt64(archiveCoverageTableCount));
            }

            await using (NpgsqlCommand readColumnTypes =
                         verificationConnection.CreateCommand())
            {
                readColumnTypes.CommandText =
                    """
                    SELECT
                        (SELECT data_type
                         FROM information_schema.columns
                         WHERE table_schema = 'public'
                           AND table_name = 'sources'
                           AND column_name = 'id'),
                        (SELECT data_type
                         FROM information_schema.columns
                         WHERE table_schema = 'public'
                           AND table_name = 'processing_runs'
                           AND column_name = 'configuration_json'),
                        (SELECT data_type
                         FROM information_schema.columns
                         WHERE table_schema = 'public'
                           AND table_name = 'embeddings'
                           AND column_name = 'vector_blob');
                    """;

                await using NpgsqlDataReader typeReader =
                    await readColumnTypes.ExecuteReaderAsync();
                Assert.True(await typeReader.ReadAsync());
                Assert.Equal("uuid", typeReader.GetString(0));
                Assert.Equal("jsonb", typeReader.GetString(1));
                Assert.Equal("bytea", typeReader.GetString(2));
            }

            Guid sourceId = Guid.NewGuid();
            Guid assetId = Guid.NewGuid();
            Guid revisionId = Guid.NewGuid();
            DateTimeOffset seededAt =
                new(2026, 9, 2, 20, 0, 0, TimeSpan.Zero);
            await using (NpgsqlCommand seedRevision =
                         verificationConnection.CreateCommand())
            {
                seedRevision.CommandText =
                    """
                    INSERT INTO sources (
                        id, kind, root_locator, created_at_utc)
                    VALUES (
                        @source_id, 'test', 'test-root', @now);

                    INSERT INTO assets (
                        id, source_id, source_key, created_at_utc)
                    VALUES (
                        @asset_id, @source_id, 'photo.jpg', @now);

                    INSERT INTO asset_revisions (
                        id, asset_id, content_sha256, size_bytes, observed_at_utc)
                    VALUES (
                        @revision_id,
                        @asset_id,
                        @content_sha256,
                        1,
                        @now);
                    """;
                seedRevision.Parameters.AddWithValue(
                    "source_id",
                    sourceId);
                seedRevision.Parameters.AddWithValue(
                    "asset_id",
                    assetId);
                seedRevision.Parameters.AddWithValue(
                    "revision_id",
                    revisionId);
                seedRevision.Parameters.AddWithValue(
                    "content_sha256",
                    new string('a', 64));
                seedRevision.Parameters.AddWithValue(
                    "now",
                    seededAt);
                await seedRevision.ExecuteNonQueryAsync();
            }

            IArchiveCoverageRepository archiveCoverage =
                new PostgresArchiveCoverageRepository(database);
            ArchiveCatalogueSource coverageSource = new(
                SourceId.From(sourceId),
                "test",
                "test-root",
                seededAt);

            Assert.Null(await archiveCoverage.GetAsync());

            ArchiveCoverageState configuredCoverage =
                await archiveCoverage.ConfigureAndIncludeAsync(
                    coverageSource,
                    "2026/03",
                    seededAt.AddMinutes(20));
            Assert.Equal(new[] { "2026/03" }, configuredCoverage.IncludedFolders);

            configuredCoverage = await archiveCoverage.ConfigureAndIncludeAsync(
                coverageSource,
                "2026/04",
                seededAt.AddMinutes(21));
            Assert.Equal(
                new[] { "2026/03", "2026/04" },
                configuredCoverage.IncludedFolders);

            configuredCoverage = await archiveCoverage.ConfigureAndIncludeAsync(
                coverageSource,
                "2026",
                seededAt.AddMinutes(22));
            Assert.Equal(new[] { "2026" }, configuredCoverage.IncludedFolders);

            ArchiveCoverageState replacedCoverage =
                await archiveCoverage.ReplaceIncludedFoldersAsync(
                    new[] { "1970/01", "1970", "2026/08" },
                    seededAt.AddMinutes(23));
            Assert.Equal(
                new[] { "1970", "2026/08" },
                replacedCoverage.IncludedFolders);

            ArchiveCoverageState persistedCoverage =
                Assert.IsType<ArchiveCoverageState>(
                    await archiveCoverage.GetAsync());
            Assert.Equal(coverageSource, persistedCoverage.Source);
            Assert.Equal(
                replacedCoverage.IncludedFolders,
                persistedCoverage.IncludedFolders);

            IArchiveAvailabilityRepository archiveAvailability =
                new PostgresArchiveAvailabilityRepository(database);
            DateTimeOffset firstAvailabilityCheck =
                seededAt.AddMinutes(1);
            await archiveAvailability.RecordAsync(
                AssetId.From(assetId),
                AssetAvailability.OnlineOnly,
                firstAvailabilityCheck);
            DateTimeOffset secondAvailabilityCheck = firstAvailabilityCheck.AddMinutes(1);
            await archiveAvailability.RecordAsync(
                AssetId.From(assetId),
                AssetAvailability.Local,
                secondAvailabilityCheck);

            await using (NpgsqlCommand readAvailability =
                         verificationConnection.CreateCommand())
            {
                readAvailability.CommandText =
                    """
                    SELECT availability, checked_at_utc
                    FROM archive_asset_availability
                    WHERE asset_id = @asset_id;
                    """;
                readAvailability.Parameters.AddWithValue("asset_id", assetId);
                await using NpgsqlDataReader availabilityReader =
                    await readAvailability.ExecuteReaderAsync();
                Assert.True(await availabilityReader.ReadAsync());
                Assert.Equal("local", availabilityReader.GetString(0));
                Assert.Equal(
                    secondAvailabilityCheck.ToUniversalTime(),
                    availabilityReader.GetFieldValue<DateTimeOffset>(1));
            }

            IArchiveSourceObservationRepository sourceObservations =
                new PostgresArchiveSourceObservationRepository(database);
            DateTimeOffset sourceObservedAt =
                seededAt.AddMinutes(10);
            DateTimeOffset sourceLastWrite =
                seededAt.AddMinutes(-5);
            ArchiveCatalogueSource archiveSource = new(
                SourceId.From(sourceId),
                "test",
                "test-root",
                sourceObservedAt.AddHours(-1));
            SourceAsset sourceAsset = new(
                new SourceAssetReference(
                    SourceId.From(sourceId),
                    "photo.jpg"),
                "photo.jpg",
                "image/jpeg",
                1,
                sourceLastWrite,
                AssetAvailability.OnlineOnly);

            ArchiveSourceObservationPersistenceResult unverified =
                await sourceObservations.RecordScanObservationAsync(
                    archiveSource,
                    sourceAsset,
                    verifiedContentHash: null,
                    sourceObservedAt);
            Assert.Equal(
                ArchiveSourceObservationVerificationState.NeedsSourceVerification,
                unverified.VerificationState);
            Assert.Equal(AssetRevisionId.From(revisionId), unverified.RevisionId);

            ArchiveSourceVerificationPersistenceResult verified =
                await sourceObservations.RecordVerifiedContentAsync(
                    AssetId.From(assetId),
                    new Sha256Digest(new string('a', 64)),
                    1,
                    sourceLastWrite,
                    "image/jpeg",
                    sourceObservedAt.AddMinutes(1));
            Assert.Equal(AssetRevisionId.From(revisionId), verified.RevisionId);
            Assert.False(verified.NewRevision);

            ArchiveSourceObservationSnapshot persistedObservation =
                Assert.IsType<ArchiveSourceObservationSnapshot>(
                    await sourceObservations.GetAsync(AssetId.From(assetId)));
            Assert.Equal(
                ArchiveSourceObservationVerificationState.Verified,
                persistedObservation.VerificationState);
            Assert.Equal(AssetAvailability.Local, persistedObservation.Availability);
            Assert.Equal(
                AssetRevisionId.From(revisionId),
                persistedObservation.VerifiedRevisionId);

            ArchiveSourceObservationPersistenceResult unchanged =
                await sourceObservations.RecordScanObservationAsync(
                    archiveSource,
                    new SourceAsset(
                        sourceAsset.Reference,
                        sourceAsset.RelativePath,
                        sourceAsset.MediaType,
                        sourceAsset.SizeBytes,
                        sourceAsset.LastWriteTimeUtc,
                        AssetAvailability.Local),
                    verifiedContentHash: null,
                    sourceObservedAt.AddMinutes(2));
            Assert.Equal(
                ArchiveSourceObservationVerificationState.Verified,
                unchanged.VerificationState);

            ArchiveSourceObservationPersistenceResult diverged =
                await sourceObservations.RecordScanObservationAsync(
                    archiveSource,
                    new SourceAsset(
                        sourceAsset.Reference,
                        sourceAsset.RelativePath,
                        sourceAsset.MediaType,
                        2,
                        sourceAsset.LastWriteTimeUtc,
                        AssetAvailability.OnlineOnly),
                    verifiedContentHash: null,
                    sourceObservedAt.AddMinutes(3));
            Assert.Equal(
                ArchiveSourceObservationVerificationState.NeedsSourceVerification,
                diverged.VerificationState);

            PostgresProcessingRepository processingRepository = new(database);
            IProcessingRunRepository processingRuns = processingRepository;
            IProcessingExecutionRepository processingExecution = processingRepository;
            DateTimeOffset processingAt = seededAt.AddHours(1);

            ProcessingRunId durableRunId = ProcessingRunId.New();
            ProcessingJobId durableJobId = ProcessingJobId.New();
            CatalogueProcessingRun durableRun = new(
                durableRunId,
                ProcessingRunStatus.Pending,
                "{\"mode\":\"archive-live-test\"}",
                processingAt);
            CatalogueProcessingJob durableJob = new(
                durableJobId,
                durableRunId,
                AssetRevisionId.From(revisionId),
                ProcessingJobStatus.Queued,
                0,
                processingAt,
                idempotencyKey: $"live:{durableRunId}:{revisionId}");

            CatalogueProcessingBatch firstBatch =
                await processingRuns.CreateRunAsync(
                    durableRun,
                    new[] { durableJob });
            CatalogueProcessingBatch repeatedBatch =
                await processingRuns.CreateRunAsync(
                    durableRun,
                    new[] { durableJob });
            Assert.Equal(ProcessingRunStatus.Pending, firstBatch.Run.Status);
            Assert.Single(firstBatch.Jobs);
            Assert.Single(repeatedBatch.Jobs);

            CatalogueProcessingJob firstClaim =
                Assert.IsType<CatalogueProcessingJob>(
                    await processingExecution.ClaimNextJobAsync(
                        durableRunId,
                        processingAt,
                        TimeSpan.FromMinutes(5)));
            Assert.Equal(1, firstClaim.AttemptCount);
            Assert.NotNull(firstClaim.LeaseToken);

            CatalogueProcessingJob checkpointed =
                await processingExecution.SaveCheckpointAsync(
                    durableJobId,
                    firstClaim.LeaseToken!.Value,
                    "{\"stage\":1}",
                    processingAt.AddMinutes(1),
                    TimeSpan.FromMinutes(5));
            AssertStageCheckpoint(checkpointed.CheckpointJson);

            await Assert.ThrowsAsync<ProcessingLeaseLostException>(
                () => processingExecution.CompleteJobAsync(
                    durableJobId,
                    ProcessingLeaseToken.New(),
                    processingAt.AddMinutes(2)));

            CatalogueProcessingJob retryQueued =
                await processingExecution.FailJobAsync(
                    durableJobId,
                    checkpointed.LeaseToken!.Value,
                    ProcessingFailureKind.Transient,
                    "temporary",
                    processingAt.AddMinutes(2),
                    processingAt.AddMinutes(10));
            Assert.Equal(ProcessingJobStatus.Queued, retryQueued.Status);
            Assert.Equal(
                ProcessingFailureKind.Transient,
                retryQueued.LastFailureKind);
            AssertStageCheckpoint(retryQueued.CheckpointJson);
            Assert.Null(
                await processingExecution.ClaimNextJobAsync(
                    durableRunId,
                    processingAt.AddMinutes(9),
                    TimeSpan.FromMinutes(5)));

            CatalogueProcessingJob retryClaim =
                Assert.IsType<CatalogueProcessingJob>(
                    await processingExecution.ClaimNextJobAsync(
                        durableRunId,
                        processingAt.AddMinutes(10),
                        TimeSpan.FromMinutes(5)));
            Assert.Equal(2, retryClaim.AttemptCount);
            AssertStageCheckpoint(retryClaim.CheckpointJson);

            CatalogueProcessingJob succeeded =
                await processingExecution.CompleteJobAsync(
                    durableJobId,
                    retryClaim.LeaseToken!.Value,
                    processingAt.AddMinutes(11));
            Assert.Equal(ProcessingJobStatus.Succeeded, succeeded.Status);

            ProcessingRunSummary completedSummary =
                await processingExecution.GetRunSummaryAsync(durableRunId);
            Assert.Equal(1, completedSummary.SucceededJobs);
            Assert.Equal(2, completedSummary.AttemptCount);

            CatalogueProcessingRun completedRun =
                await processingExecution.CompleteRunAsync(
                    durableRunId,
                    processingAt.AddMinutes(12));
            Assert.Equal(ProcessingRunStatus.Completed, completedRun.Status);

            PostgresProcessingRepository restartedProcessing = new(database);
            CatalogueProcessingRun persistedRun =
                Assert.IsType<CatalogueProcessingRun>(
                    await restartedProcessing.GetRunAsync(durableRunId));
            CatalogueProcessingJob persistedJob =
                Assert.Single(
                    await restartedProcessing.GetJobsAsync(durableRunId));
            Assert.Equal(ProcessingRunStatus.Completed, persistedRun.Status);
            Assert.Equal(ProcessingJobStatus.Succeeded, persistedJob.Status);
            AssertStageCheckpoint(persistedJob.CheckpointJson);

            ProcessingRunId reclaimedRunId = ProcessingRunId.New();
            CatalogueProcessingRun reclaimedRun = new(
                reclaimedRunId,
                ProcessingRunStatus.Pending,
                "{\"mode\":\"lease-reclaim\"}",
                processingAt.AddMinutes(20));
            CatalogueProcessingJob reclaimedSeed = new(
                ProcessingJobId.New(),
                reclaimedRunId,
                AssetRevisionId.From(revisionId),
                ProcessingJobStatus.Queued,
                0,
                processingAt.AddMinutes(20),
                idempotencyKey: $"reclaim:{reclaimedRunId}:{revisionId}");
            await processingRuns.CreateRunAsync(
                reclaimedRun,
                new[] { reclaimedSeed });

            CatalogueProcessingJob originalLease =
                Assert.IsType<CatalogueProcessingJob>(
                    await processingExecution.ClaimNextJobAsync(
                        reclaimedRunId,
                        processingAt.AddMinutes(20),
                        TimeSpan.FromMinutes(2)));
            CatalogueProcessingJob reclaimedLease =
                Assert.IsType<CatalogueProcessingJob>(
                    await restartedProcessing.ClaimNextJobAsync(
                        reclaimedRunId,
                        processingAt.AddMinutes(23),
                        TimeSpan.FromMinutes(2)));
            Assert.Equal(originalLease.Id, reclaimedLease.Id);
            Assert.Equal(2, reclaimedLease.AttemptCount);
            Assert.NotEqual(
                originalLease.LeaseToken,
                reclaimedLease.LeaseToken);
            Assert.Equal(
                ProcessingFailureKind.Transient,
                reclaimedLease.LastFailureKind);
            await restartedProcessing.CompleteJobAsync(
                reclaimedLease.Id,
                reclaimedLease.LeaseToken!.Value,
                processingAt.AddMinutes(24));
            Assert.Equal(
                ProcessingRunStatus.Completed,
                (await restartedProcessing.CompleteRunAsync(
                    reclaimedRunId,
                    processingAt.AddMinutes(25))).Status);

            ProcessingRunId competingRunId = ProcessingRunId.New();
            CatalogueProcessingRun competingRun = new(
                competingRunId,
                ProcessingRunStatus.Pending,
                "{\"mode\":\"competing-claim\"}",
                processingAt.AddMinutes(30));
            CatalogueProcessingJob competingSeed = new(
                ProcessingJobId.New(),
                competingRunId,
                AssetRevisionId.From(revisionId),
                ProcessingJobStatus.Queued,
                0,
                processingAt.AddMinutes(30),
                idempotencyKey: $"compete:{competingRunId}:{revisionId}");
            await processingRuns.CreateRunAsync(
                competingRun,
                new[] { competingSeed });

            PostgresProcessingRepository competingA = new(database);
            PostgresProcessingRepository competingB = new(database);
            CatalogueProcessingJob?[] competingClaims =
                await Task.WhenAll(
                    competingA.ClaimNextJobAsync(
                        competingRunId,
                        processingAt.AddMinutes(30),
                        TimeSpan.FromMinutes(5)),
                    competingB.ClaimNextJobAsync(
                        competingRunId,
                        processingAt.AddMinutes(30),
                        TimeSpan.FromMinutes(5)));
            CatalogueProcessingJob competingClaim =
                Assert.Single(
                    competingClaims
                        .Where(static job => job is not null)
                        .Select(static job => job!));
            await competingA.CompleteJobAsync(
                competingClaim.Id,
                competingClaim.LeaseToken!.Value,
                processingAt.AddMinutes(31));
            Assert.Equal(
                ProcessingRunStatus.Completed,
                (await competingA.CompleteRunAsync(
                    competingRunId,
                    processingAt.AddMinutes(32))).Status);

            ProcessingRunId cancelledRunId = ProcessingRunId.New();
            CatalogueProcessingRun cancelledSeedRun = new(
                cancelledRunId,
                ProcessingRunStatus.Pending,
                "{\"mode\":\"cancel\"}",
                processingAt.AddMinutes(40));
            CatalogueProcessingJob cancelledSeedJob = new(
                ProcessingJobId.New(),
                cancelledRunId,
                AssetRevisionId.From(revisionId),
                ProcessingJobStatus.Queued,
                0,
                processingAt.AddMinutes(40),
                idempotencyKey: $"cancel:{cancelledRunId}:{revisionId}");
            await processingRuns.CreateRunAsync(
                cancelledSeedRun,
                new[] { cancelledSeedJob });
            CatalogueProcessingRun cancelledRun =
                await processingRuns.RequestCancellationAsync(
                    cancelledRunId,
                    processingAt.AddMinutes(41));
            Assert.Equal(ProcessingRunStatus.Cancelled, cancelledRun.Status);
            CatalogueProcessingJob cancelledJob =
                Assert.Single(
                    await processingRuns.GetJobsAsync(cancelledRunId));
            Assert.Equal(ProcessingJobStatus.Cancelled, cancelledJob.Status);
            Assert.Null(
                await processingExecution.ClaimNextJobAsync(
                    cancelledRunId,
                    processingAt.AddMinutes(42),
                    TimeSpan.FromMinutes(5)));

            Guid processingRunId = Guid.NewGuid();
            await using (NpgsqlCommand seedProcessingRun =
                         verificationConnection.CreateCommand())
            {
                seedProcessingRun.CommandText =
                    """
                    INSERT INTO processing_runs (
                        id,
                        status,
                        configuration_json,
                        started_at_utc)
                    VALUES (
                        @processing_run_id,
                        'pending',
                        '{}'::jsonb,
                        @now);
                    """;
                seedProcessingRun.Parameters.AddWithValue(
                    "processing_run_id",
                    processingRunId);
                seedProcessingRun.Parameters.AddWithValue(
                    "now",
                    DateTimeOffset.UtcNow);
                await seedProcessingRun.ExecuteNonQueryAsync();
            }

            AnalysisProfileDefinition profile = new(
                new Sha256Digest(new string('b', 64)),
                new ModelId("test-detector"),
                new Sha256Digest(new string('c', 64)),
                new ModelId("test-embedder"),
                new Sha256Digest(new string('d', 64)),
                new AlignmentProtocolId("test-alignment"));
            Sha256Digest profileHash = profile.ComputeHash();
            ProcessingRunId runId = ProcessingRunId.From(processingRunId);
            AssetRevisionId revision = AssetRevisionId.From(revisionId);
            IArchiveAnalysisStateRepository archiveAnalysis =
                new PostgresArchiveAnalysisStateRepository(database);

            await archiveAnalysis.RegisterRunAsync(
                runId,
                profile,
                DateTimeOffset.UtcNow);
            Assert.Equal(
                profileHash,
                await archiveAnalysis.GetRunProfileHashAsync(runId));
            Assert.False(
                await archiveAnalysis.IsCompletedAsync(revision, profileHash));

            await archiveAnalysis.RecordCompletionAsync(
                runId,
                revision,
                profileHash,
                DateTimeOffset.UtcNow);

            Assert.True(
                await archiveAnalysis.IsCompletedAsync(revision, profileHash));

            await using (NpgsqlCommand mutateRevision =
                         verificationConnection.CreateCommand())
            {
                mutateRevision.CommandText =
                    """
                    UPDATE asset_revisions
                    SET content_sha256 = @replacement_content_sha256
                    WHERE id = @revision_id;
                    """;
                mutateRevision.Parameters.AddWithValue(
                    "revision_id",
                    revisionId);
                mutateRevision.Parameters.AddWithValue(
                    "replacement_content_sha256",
                    new string('b', 64));

                PostgresException immutable =
                    await Assert.ThrowsAsync<PostgresException>(
                        () => mutateRevision.ExecuteNonQueryAsync());
                Assert.Contains(
                    "asset revision identity is immutable",
                    immutable.MessageText,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            await using (NpgsqlCommand terminateConnections =
                         adminConnection.CreateCommand())
            {
                terminateConnections.CommandText =
                    """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid();
                    """;
                terminateConnections.Parameters.AddWithValue(
                    "database_name",
                    databaseName);
                await terminateConnections.ExecuteNonQueryAsync();
            }

            await using NpgsqlCommand dropDatabase =
                adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName};";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static void AssertStageCheckpoint(string? checkpointJson)
    {
        Assert.NotNull(checkpointJson);
        using JsonDocument checkpoint = JsonDocument.Parse(checkpointJson);
        JsonElement root = checkpoint.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(1, root.GetProperty("stage").GetInt32());
        Assert.Single(root.EnumerateObject());
    }

    private static string QuoteIdentifier(string identifier)
    {
        const char quote = (char)34;
        string quoteString = quote.ToString();
        string escaped = identifier.Replace(
            quoteString,
            quoteString + quoteString,
            StringComparison.Ordinal);
        return quoteString + escaped + quoteString;
    }
}
