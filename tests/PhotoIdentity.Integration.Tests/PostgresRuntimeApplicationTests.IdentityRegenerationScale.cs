using System.Diagnostics;
using Npgsql;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PostgresRuntimeApplicationTests_IdentityRegenerationScale
{
    private const int ExemplarCount = 32;
    private const int TargetCount = 96;
    private const int TargetBatchSize = 8;

    [Fact]
    public async Task Identity_regeneration_advances_in_bounded_batches_while_status_reads_stay_responsive_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_regen_scale_{Guid.NewGuid():N}";
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
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                initialization.Health.SchemaVersion);

            ModelId modelId = new("regen-scale-model");
            Sha256Digest modelHash = new(new string('a', 64));
            DateTimeOffset now = new(2026, 9, 11, 6, 0, 0, TimeSpan.Zero);
            await SeedScaleCatalogueAsync(
                testBuilder.ConnectionString,
                modelId,
                modelHash,
                now);

            TimeProvider clock = TimeProvider.System;
            PostgresIdentitySuggestionPolicyRepository policies = new(database, clock);
            ReviewIdentitySuggestionPolicy policy = await policies.GetAsync(modelId, modelHash);
            Assert.False(policy.AutoAssignEnabled);

            PostgresIdentityMatchRegenerationRepository runs = new(database);
            ReviewIdentityMatchRegenerationRun run = await runs.StartAsync(
                modelId,
                modelHash,
                policy.Version,
                "test:scale-acceptance",
                now.AddMinutes(1));
            Assert.Equal(TargetCount, run.TargetCount);

            ArchiveThroughputMetrics metrics = new(clock);
            IdentityMatchRegenerationHostedService worker = new(
                runs,
                new PostgresIdentityMatchRegenerationScorer(database, clock),
                policies,
                new PostgresIdentityAutoAssignmentService(database, clock),
                new PostgresIdentityMatchEvidenceVersionReader(database),
                clock,
                metrics);

            int expectedProcessed = 0;
            int statusReadCount = 0;
            double maxStatusReadMilliseconds = 0;

            while (expectedProcessed < TargetCount)
            {
                Task<bool> advance = worker.AdvanceOnceAsync();

                using CancellationTokenSource statusTimeout = new(TimeSpan.FromSeconds(2));
                Stopwatch statusTimer = Stopwatch.StartNew();
                ReviewIdentityMatchRegenerationRun? duringBatch = await runs.GetLatestAsync(
                    modelId,
                    modelHash,
                    statusTimeout.Token);
                statusTimer.Stop();

                statusReadCount++;
                maxStatusReadMilliseconds = Math.Max(
                    maxStatusReadMilliseconds,
                    statusTimer.Elapsed.TotalMilliseconds);
                Assert.NotNull(duringBatch);
                Assert.True(duringBatch.IsActive);

                Assert.True(await advance);
                expectedProcessed = Math.Min(
                    TargetCount,
                    expectedProcessed + TargetBatchSize);

                ReviewIdentityMatchRegenerationRun progressed =
                    Assert.IsType<ReviewIdentityMatchRegenerationRun>(
                        await runs.GetLatestAsync(modelId, modelHash));
                Assert.Equal(expectedProcessed, progressed.ProcessedTargetCount);
                Assert.Equal(0, progressed.ErrorCount);
                Assert.True(progressed.IsActive);
            }

            Assert.True(await worker.AdvanceOnceAsync());
            ReviewIdentityMatchRegenerationRun completed =
                Assert.IsType<ReviewIdentityMatchRegenerationRun>(
                    await runs.GetLatestAsync(modelId, modelHash));
            Assert.Equal(ReviewIdentityMatchRegenerationStatuses.Completed, completed.Status);
            Assert.Equal(TargetCount, completed.TargetCount);
            Assert.Equal(TargetCount, completed.ProcessedTargetCount);
            Assert.Equal(TargetCount, completed.SuggestedTargetCount);
            Assert.Equal(TargetCount * 2, completed.SuggestionCount);
            Assert.Equal(0, completed.AutomaticallyAssignedCount);
            Assert.Equal(0, completed.ErrorCount);

            ArchiveThroughputSnapshot snapshot = metrics.GetSnapshot();
            Assert.Equal(
                TargetCount,
                Counter(snapshot, ArchiveThroughputMetricNames.IdentityRegenerationTargetsClaimed));
            Assert.Equal(
                TargetCount,
                Counter(snapshot, ArchiveThroughputMetricNames.IdentityRegenerationTargetsCompleted));
            Assert.Equal(
                1,
                Counter(snapshot, ArchiveThroughputMetricNames.IdentityRegenerationRunsCompleted));
            Assert.Equal(0, Counter(snapshot, ArchiveThroughputMetricNames.IdentityRegenerationTargetsFailed));
            Assert.Equal(0, Counter(snapshot, ArchiveThroughputMetricNames.IdentityRegenerationRunsFailed));
            Assert.Equal(TargetCount / TargetBatchSize, statusReadCount);
            Assert.True(maxStatusReadMilliseconds < TimeSpan.FromSeconds(2).TotalMilliseconds);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static long Counter(ArchiveThroughputSnapshot snapshot, string name) =>
        snapshot.Counters.SingleOrDefault(counter =>
            string.Equals(counter.Name, name, StringComparison.Ordinal))?.Value ?? 0;

    private static async Task SeedScaleCatalogueAsync(
        string connectionString,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset now)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        float[] vector = [1f, 0f];
        byte[] vectorBlob = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, vectorBlob, 0, vectorBlob.Length);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', 'regeneration-scale-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'scale.jpg', @now);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES (
                @revision_id,
                @asset_id,
                @revision_hash,
                1000000,
                @now,
                'image/jpeg',
                4000,
                3000);

            INSERT INTO people (id, display_name, created_at_utc)
            SELECT
                md5('regen-scale-person-' || series::text)::uuid,
                'Scale person ' || series::text,
                @now
            FROM generate_series(1, @exemplar_count) AS series;

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            SELECT
                md5('regen-scale-exemplar-face-' || series::text)::uuid,
                @revision_id,
                series - 1,
                @now
            FROM generate_series(1, @exemplar_count) AS series;

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            SELECT
                md5('regen-scale-target-face-' || series::text)::uuid,
                @revision_id,
                @exemplar_count + series - 1,
                @now
            FROM generate_series(1, @target_count) AS series;

            INSERT INTO face_crops (
                id,
                face_occurrence_id,
                crop_protocol,
                content_sha256,
                storage_path,
                width,
                height,
                created_at_utc)
            SELECT
                md5('regen-scale-exemplar-crop-' || series::text)::uuid,
                md5('regen-scale-exemplar-face-' || series::text)::uuid,
                'scale-crop',
                md5('regen-scale-exemplar-hash-a-' || series::text) ||
                    md5('regen-scale-exemplar-hash-b-' || series::text),
                'scale/exemplar/' || series::text || '.jpg',
                112,
                112,
                @now
            FROM generate_series(1, @exemplar_count) AS series;

            INSERT INTO face_crops (
                id,
                face_occurrence_id,
                crop_protocol,
                content_sha256,
                storage_path,
                width,
                height,
                created_at_utc)
            SELECT
                md5('regen-scale-target-crop-' || series::text)::uuid,
                md5('regen-scale-target-face-' || series::text)::uuid,
                'scale-crop',
                md5('regen-scale-target-hash-a-' || series::text) ||
                    md5('regen-scale-target-hash-b-' || series::text),
                'scale/target/' || series::text || '.jpg',
                112,
                112,
                @now
            FROM generate_series(1, @target_count) AS series;

            INSERT INTO embeddings (
                id,
                face_crop_id,
                model_id,
                model_hash,
                dimensions,
                l2_norm,
                vector_blob,
                created_at_utc)
            SELECT
                100000 + series,
                md5('regen-scale-exemplar-crop-' || series::text)::uuid,
                @model_id,
                @model_hash,
                2,
                1.0,
                @vector_blob,
                @now
            FROM generate_series(1, @exemplar_count) AS series;

            INSERT INTO embeddings (
                id,
                face_crop_id,
                model_id,
                model_hash,
                dimensions,
                l2_norm,
                vector_blob,
                created_at_utc)
            SELECT
                200000 + series,
                md5('regen-scale-target-crop-' || series::text)::uuid,
                @model_id,
                @model_hash,
                2,
                1.0,
                @vector_blob,
                @now
            FROM generate_series(1, @target_count) AS series;

            INSERT INTO person_labels (
                person_id,
                face_occurrence_id,
                label_kind,
                assigned_by,
                assigned_at_utc)
            SELECT
                md5('regen-scale-person-' || series::text)::uuid,
                md5('regen-scale-exemplar-face-' || series::text)::uuid,
                'confirmed',
                'test:scale-fixture',
                @now
            FROM generate_series(1, @exemplar_count) AS series;
            """;
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("asset_id", assetId);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("revision_hash", new string('b', 64));
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("exemplar_count", ExemplarCount);
        command.Parameters.AddWithValue("target_count", TargetCount);
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("vector_blob", vectorBlob);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
