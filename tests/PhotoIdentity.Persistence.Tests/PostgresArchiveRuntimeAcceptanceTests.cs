using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresArchiveRuntimeAcceptanceTests
{
    [Fact]
    public async Task Archive_contracts_operate_together_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_archive_acceptance_{Guid.NewGuid():N}";
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
            await database.InitializeAsync();

            DateTimeOffset now = new(2026, 9, 10, 6, 0, 0, TimeSpan.Zero);
            SourceId sourceId = SourceId.New();
            AssetId assetId = AssetId.New();
            AssetRevisionId revisionId = AssetRevisionId.New();
            ProcessingRunId runId = ProcessingRunId.New();
            Sha256Digest profileHash = new(new string('a', 64));
            const string proxyProfileId = "acceptance-proxy";

            await SeedAnalysedRevisionAsync(
                testBuilder.ConnectionString,
                sourceId,
                assetId,
                revisionId,
                runId,
                profileHash,
                now);

            IArchiveCoverageRepository coverage = new PostgresArchiveCoverageRepository(database);
            IArchiveStatusRepository status = new PostgresArchiveStatusRepository(database);
            IArchiveAvailabilityRepository availability = new PostgresArchiveAvailabilityRepository(database);
            IArchiveHydrationRepository hydration = new PostgresArchiveHydrationRepository(database);
            IArchiveSourceHydrationRepository sourceHydration = new PostgresArchiveSourceHydrationRepository(database);
            IArchiveSourceVerificationStateRepository verification =
                new PostgresArchiveSourceVerificationStateRepository(database);
            IArchiveStorageAccountingRepository storage = new PostgresArchiveStorageAccountingRepository(database);
            IArchivePostAnalysisRepository postAnalysis = new PostgresArchivePostAnalysisRepository(database);

            ArchiveCatalogueSource source = new(sourceId, "local-folder", "archive-root", now);
            ArchiveCoverageState configured = await coverage.ConfigureAndIncludeAsync(
                source,
                "1970",
                now);
            Assert.Equal(sourceId, configured.Source.SourceId);
            Assert.Equal(["1970"], configured.IncludedFolders);

            CatalogueArchiveFolderStatus initial = await status.GetStatusAsync(
                sourceId,
                "1970",
                profileHash);
            Assert.Equal(1, initial.CurrentImages);
            Assert.Equal(1, initial.LocalImages);
            Assert.Equal(1, initial.AnalysedImages);
            Assert.Equal(123, await storage.GetCurrentLogicalSourceBytesAsync(sourceId));
            Assert.Equal(
                revisionId,
                await postAnalysis.GetNextMissingProxyRevisionAsync(
                    sourceId,
                    profileHash,
                    proxyProfileId));

            CatalogueArchiveItemPage firstPage = await status.GetItemsAsync(
                sourceId,
                "1970",
                profileHash,
                "all",
                0,
                1);
            CatalogueArchiveItemStatus firstItem = Assert.Single(firstPage.Items);
            Assert.Equal("1970/photo.jpg", firstItem.RelativePath);
            Assert.Equal(revisionId, firstItem.RevisionId);
            Assert.Equal("local", firstItem.Availability);
            Assert.Equal("verified", firstItem.SourceVerificationState);
            Assert.Equal("analysed", firstItem.AnalysisState);

            ArchiveManagedHydrationState claimed = await hydration.ClaimAsync(revisionId, now.AddMinutes(1));
            Assert.True(claimed.IsActive);
            Assert.Single(await hydration.GetActiveLeasesAsync());

            await verification.MarkNeedsVerificationAsync(assetId, now.AddMinutes(2));
            Assert.False((await hydration.GetAsync(revisionId))!.IsActive);
            ArchiveManagedSourceHydrationState sourceLease = Assert.IsType<ArchiveManagedSourceHydrationState>(
                await sourceHydration.GetAsync(assetId));
            Assert.True(sourceLease.IsActive);
            Assert.Single(await sourceHydration.GetActiveLeasesAsync());

            await availability.RecordAsync(assetId, AssetAvailability.OnlineOnly, now.AddMinutes(3));
            CatalogueArchiveFolderStatus transitioned = await status.GetStatusAsync(
                sourceId,
                "1970",
                profileHash);
            Assert.Equal(1, transitioned.OnlineOnlyImages);
            Assert.Equal(1, transitioned.NeedsSourceVerificationImages);

            CatalogueArchiveItemPage filtered = await status.GetItemsAsync(
                sourceId,
                "1970",
                profileHash,
                availability: "online-only",
                verification: "needs-source-verification",
                analysis: "all",
                offset: 0,
                limit: 50);
            CatalogueArchiveItemStatus filteredItem = Assert.Single(filtered.Items);
            Assert.Equal(revisionId, filteredItem.RevisionId);
            Assert.Equal("online-only", filteredItem.Availability);
            Assert.Equal("needs-source-verification", filteredItem.SourceVerificationState);

            Assert.True(await sourceHydration.TransferToRevisionAsync(
                assetId,
                revisionId,
                now.AddMinutes(4)));
            Assert.False((await sourceHydration.GetAsync(assetId))!.IsActive);
            Assert.True((await hydration.GetAsync(revisionId))!.IsActive);

            await MarkVerifiedAndPersistProxyAsync(
                testBuilder.ConnectionString,
                assetId,
                revisionId,
                proxyProfileId,
                now.AddMinutes(5));
            await availability.RecordAsync(assetId, AssetAvailability.Local, now.AddMinutes(5));

            Assert.Null(await postAnalysis.GetNextMissingProxyRevisionAsync(
                sourceId,
                profileHash,
                proxyProfileId));
            Assert.Equal(42, await storage.GetReviewProxyBytesAsync(proxyProfileId));

            CatalogueArchiveFolderStatus final = await status.GetStatusAsync(
                sourceId,
                "1970",
                profileHash);
            Assert.Equal(1, final.LocalImages);
            Assert.Equal(1, final.AnalysedImages);
            Assert.Equal(0, final.NeedsSourceVerificationImages);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAnalysedRevisionAsync(
        string connectionString,
        SourceId sourceId,
        AssetId assetId,
        AssetRevisionId revisionId,
        ProcessingRunId runId,
        Sha256Digest profileHash,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'local-folder', 'archive-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, '1970/photo.jpg', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type)
            VALUES (
                @revision_id, @asset_id, @revision_hash, 123, @now, 'image/jpeg');

            INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
            VALUES (@asset_id, 'local', @now);

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
            VALUES (
                @asset_id,
                123,
                @now,
                'image/jpeg',
                @now,
                'verified',
                @revision_id,
                123,
                @now,
                'image/jpeg',
                @now);

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
                @pipeline_hash,
                'detector',
                @detector_hash,
                'embedder',
                @embedder_hash,
                'alignment',
                'archive runtime acceptance',
                @now);

            INSERT INTO processing_runs (
                id, status, configuration_json, started_at_utc, completed_at_utc)
            VALUES (@run_id, 'completed', '{}', @now, @now);

            INSERT INTO archive_analysis_runs (
                processing_run_id, profile_hash, registered_at_utc)
            VALUES (@run_id, @profile_hash, @now);

            INSERT INTO asset_revision_analysis (
                asset_revision_id, profile_hash, processing_run_id, completed_at_utc)
            VALUES (@revision_id, @profile_hash, @run_id, @now);
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("asset_id", assetId.Value);
        command.Parameters.AddWithValue("revision_id", revisionId.Value);
        command.Parameters.AddWithValue("run_id", runId.Value);
        command.Parameters.AddWithValue("revision_hash", new string('1', 64));
        command.Parameters.AddWithValue("profile_hash", profileHash.ToString());
        command.Parameters.AddWithValue("pipeline_hash", new string('2', 64));
        command.Parameters.AddWithValue("detector_hash", new string('3', 64));
        command.Parameters.AddWithValue("embedder_hash", new string('4', 64));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task MarkVerifiedAndPersistProxyAsync(
        string connectionString,
        AssetId assetId,
        AssetRevisionId revisionId,
        string profileId,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE archive_source_observations
            SET verification_state = 'verified',
                verified_revision_id = @revision_id,
                verified_size_bytes = 123,
                verified_last_write_utc = @now,
                verified_media_type = 'image/jpeg',
                verified_at_utc = @now
            WHERE asset_id = @asset_id;

            INSERT INTO archive_review_proxy_profiles (
                profile_id,
                protocol_version,
                encoder,
                format,
                jpeg_quality,
                maximum_long_edge,
                resize_policy,
                canonical_definition,
                recorded_at_utc)
            VALUES (
                @profile_id,
                'v1',
                'test',
                'jpeg',
                85,
                1600,
                'fit',
                'archive runtime acceptance proxy',
                @now);

            INSERT INTO asset_revision_review_proxies (
                asset_revision_id,
                profile_id,
                encoded_byte_length,
                content_sha256,
                width,
                height,
                generated_at_utc,
                relative_path)
            VALUES (
                @revision_id,
                @profile_id,
                42,
                @proxy_hash,
                100,
                80,
                @now,
                'review-proxies/acceptance.jpg');
            """;
        command.Parameters.AddWithValue("asset_id", assetId.Value);
        command.Parameters.AddWithValue("revision_id", revisionId.Value);
        command.Parameters.AddWithValue("profile_id", profileId);
        command.Parameters.AddWithValue("proxy_hash", new string('5', 64));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
}
