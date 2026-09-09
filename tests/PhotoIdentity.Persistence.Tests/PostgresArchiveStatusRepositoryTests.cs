using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresArchiveStatusRepositoryTests
{
    [Fact]
    public async Task GetStatusAndItemsAsync_ClassifiesArchiveState_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_archive_status_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };

            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            PostgresInitializationResult initialization = await database.TryInitializeAsync();
            Assert.Null(initialization.Error);

            SourceId sourceId = SourceId.New();
            Sha256Digest profileHash = new(new string('a', 64));
            Guid analysedRevisionId = Guid.NewGuid();
            Guid onlineOnlyRevisionId = Guid.NewGuid();
            Guid failedRevisionId = Guid.NewGuid();
            await SeedArchiveAsync(
                testBuilder.ConnectionString,
                Guid.Parse(sourceId.ToString()),
                profileHash,
                analysedRevisionId,
                onlineOnlyRevisionId,
                failedRevisionId);

            PostgresArchiveStatusRepository repository = new(database);
            CatalogueArchiveFolderStatus status = await repository.GetStatusAsync(
                sourceId,
                "1970",
                profileHash);

            Assert.Equal("1970", status.RelativeFolder);
            Assert.Equal(4, status.CurrentImages);
            Assert.Equal(3, status.LocalImages);
            Assert.Equal(1, status.OnlineOnlyImages);
            Assert.Equal(1, status.AnalysedImages);
            Assert.Equal(1, status.PendingImages);
            Assert.Equal(1, status.FailedImages);
            Assert.Equal(1, status.NeedsSourceVerificationImages);
            Assert.Equal(1, status.MissingImages);

            CatalogueArchiveItemPage unavailable = await repository.GetItemsAsync(
                sourceId,
                "1970",
                profileHash,
                "unavailable",
                0,
                50);
            CatalogueArchiveItemStatus onlineOnly = Assert.Single(unavailable.Items);
            Assert.Equal("1970/online-only.jpg", onlineOnly.RelativePath);
            Assert.Equal(AssetRevisionId.From(onlineOnlyRevisionId), onlineOnly.RevisionId);
            Assert.Equal("online-only", onlineOnly.Availability);
            Assert.Equal("verified", onlineOnly.SourceVerificationState);
            Assert.Equal("unavailable", onlineOnly.AnalysisState);

            CatalogueArchiveItemPage orthogonalPending = await repository.GetItemsAsync(
                sourceId,
                "1970",
                profileHash,
                availability: "online-only",
                verification: "verified",
                analysis: "pending",
                offset: 0,
                limit: 50);
            CatalogueArchiveItemStatus pending = Assert.Single(orthogonalPending.Items);
            Assert.Equal("1970/online-only.jpg", pending.RelativePath);
            Assert.Equal("pending", pending.AnalysisState);

            CatalogueArchiveItemPage failed = await repository.GetItemsAsync(
                sourceId,
                "1970",
                profileHash,
                availability: "local",
                verification: "verified",
                analysis: "failed",
                offset: 0,
                limit: 50);
            CatalogueArchiveItemStatus failedItem = Assert.Single(failed.Items);
            Assert.Equal("1970/failed.jpg", failedItem.RelativePath);
            Assert.Equal("detector failure", failedItem.LastError);

            CatalogueArchiveRunStatus latest = Assert.IsType<CatalogueArchiveRunStatus>(
                await repository.GetLatestRunAsync(profileHash));
            Assert.Equal("completed", latest.Status);
            Assert.Equal(2, latest.TotalJobs);
            Assert.Equal(1, latest.FailedJobs);
            Assert.Equal(1, latest.SucceededJobs);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedArchiveAsync(
        string connectionString,
        Guid sourceId,
        Sha256Digest profileHash,
        Guid analysedRevisionId,
        Guid onlineOnlyRevisionId,
        Guid failedRevisionId)
    {
        Guid analysedAssetId = Guid.NewGuid();
        Guid onlineOnlyAssetId = Guid.NewGuid();
        Guid failedAssetId = Guid.NewGuid();
        Guid needsVerificationAssetId = Guid.NewGuid();
        Guid needsVerificationRevisionId = Guid.NewGuid();
        Guid missingAssetId = Guid.NewGuid();
        Guid missingRevisionId = Guid.NewGuid();
        Guid runId = Guid.NewGuid();
        Guid failedJobId = Guid.NewGuid();
        Guid succeededJobId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 9, 18, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'local-folder', 'archive-status-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, deleted_at_utc)
            VALUES
                (@analysed_asset_id, @source_id, '1970/analysed.jpg', @now, NULL),
                (@online_only_asset_id, @source_id, '1970/online-only.jpg', @now, NULL),
                (@failed_asset_id, @source_id, '1970/failed.jpg', @now, NULL),
                (@needs_verification_asset_id, @source_id, '1970/needs-verification.jpg', @now, NULL),
                (@missing_asset_id, @source_id, '1970/missing.jpg', @now, @deleted_at_utc);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type)
            VALUES
                (@analysed_revision_id, @analysed_asset_id, @analysed_hash, 10, @now, 'image/jpeg'),
                (@online_only_revision_id, @online_only_asset_id, @online_only_hash, 11, @now, 'image/jpeg'),
                (@failed_revision_id, @failed_asset_id, @failed_hash, 12, @now, 'image/jpeg'),
                (@needs_verification_revision_id, @needs_verification_asset_id, @needs_verification_hash, 13, @now, 'image/jpeg'),
                (@missing_revision_id, @missing_asset_id, @missing_hash, 14, @now, 'image/jpeg');

            INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
            VALUES
                (@online_only_asset_id, 'online-only', @now),
                (@failed_asset_id, 'local', @now),
                (@needs_verification_asset_id, 'local', @now);

            INSERT INTO archive_source_observations (
                asset_id,
                observed_size_bytes,
                observed_last_write_utc,
                observed_media_type,
                observed_at_utc,
                verification_state,
                verified_revision_id,
                verified_size_bytes,
                verified_last_write_utc,
                verified_media_type,
                verified_at_utc)
            VALUES
                (@analysed_asset_id, 10, @now, 'image/jpeg', @now, 'verified', @analysed_revision_id, 10, @now, 'image/jpeg', @now),
                (@online_only_asset_id, 11, @now, 'image/jpeg', @now, 'verified', @online_only_revision_id, 11, @now, 'image/jpeg', @now),
                (@failed_asset_id, 12, @now, 'image/jpeg', @now, 'verified', @failed_revision_id, 12, @now, 'image/jpeg', @now),
                (@needs_verification_asset_id, 13, @now, 'image/jpeg', @now, 'needs-source-verification', NULL, NULL, NULL, NULL, NULL);

            INSERT INTO archive_analysis_profiles (
                profile_hash,
                detector_pipeline_hash,
                detector_model_id,
                detector_model_hash,
                embedder_model_id,
                embedder_model_hash,
                alignment_protocol,
                canonical_definition,
                recorded_at_utc)
            VALUES (
                @profile_hash,
                @detector_pipeline_hash,
                'detector',
                @detector_model_hash,
                'embedder',
                @embedder_model_hash,
                'identity',
                'archive status profile',
                @now);

            INSERT INTO processing_runs (
                id,
                status,
                configuration_json,
                started_at_utc,
                completed_at_utc)
            VALUES (
                @run_id,
                'completed',
                '{}',
                @run_started_at,
                @run_completed_at);

            INSERT INTO archive_analysis_runs (
                processing_run_id,
                profile_hash,
                registered_at_utc)
            VALUES (
                @run_id,
                @profile_hash,
                @run_started_at);

            INSERT INTO processing_jobs (
                id,
                processing_run_id,
                asset_revision_id,
                status,
                attempt_count,
                available_at_utc,
                started_at_utc,
                completed_at_utc,
                error,
                idempotency_key)
            VALUES
                (
                    @failed_job_id,
                    @run_id,
                    @failed_revision_id,
                    'failed',
                    1,
                    @run_started_at,
                    @run_started_at,
                    @run_completed_at,
                    'detector failure',
                    'archive-status-failed'),
                (
                    @succeeded_job_id,
                    @run_id,
                    @analysed_revision_id,
                    'succeeded',
                    1,
                    @run_started_at,
                    @run_started_at,
                    @run_completed_at,
                    NULL,
                    'archive-status-succeeded');

            INSERT INTO asset_revision_analysis (
                asset_revision_id,
                profile_hash,
                processing_run_id,
                completed_at_utc)
            VALUES (
                @analysed_revision_id,
                @profile_hash,
                @run_id,
                @run_completed_at);
            """;
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("deleted_at_utc", now.AddMinutes(30));
        command.Parameters.AddWithValue("analysed_asset_id", analysedAssetId);
        command.Parameters.AddWithValue("online_only_asset_id", onlineOnlyAssetId);
        command.Parameters.AddWithValue("failed_asset_id", failedAssetId);
        command.Parameters.AddWithValue("needs_verification_asset_id", needsVerificationAssetId);
        command.Parameters.AddWithValue("missing_asset_id", missingAssetId);
        command.Parameters.AddWithValue("analysed_revision_id", analysedRevisionId);
        command.Parameters.AddWithValue("online_only_revision_id", onlineOnlyRevisionId);
        command.Parameters.AddWithValue("failed_revision_id", failedRevisionId);
        command.Parameters.AddWithValue("needs_verification_revision_id", needsVerificationRevisionId);
        command.Parameters.AddWithValue("missing_revision_id", missingRevisionId);
        command.Parameters.AddWithValue("analysed_hash", new string('1', 64));
        command.Parameters.AddWithValue("online_only_hash", new string('2', 64));
        command.Parameters.AddWithValue("failed_hash", new string('3', 64));
        command.Parameters.AddWithValue("needs_verification_hash", new string('4', 64));
        command.Parameters.AddWithValue("missing_hash", new string('5', 64));
        command.Parameters.AddWithValue("profile_hash", profileHash.ToString());
        command.Parameters.AddWithValue("detector_pipeline_hash", new string('6', 64));
        command.Parameters.AddWithValue("detector_model_hash", new string('7', 64));
        command.Parameters.AddWithValue("embedder_model_hash", new string('8', 64));
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("run_started_at", now.AddMinutes(1));
        command.Parameters.AddWithValue("run_completed_at", now.AddMinutes(5));
        command.Parameters.AddWithValue("failed_job_id", failedJobId);
        command.Parameters.AddWithValue("succeeded_job_id", succeededJobId);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
}
