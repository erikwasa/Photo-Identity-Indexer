using Npgsql;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class PostgresSourceCopyPurgeIntegrationTests
{
    [Fact]
    public async Task Purge_removes_real_derivatives_and_photo_state_while_preserving_shared_person_and_other_photo()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_purge_integration_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString) { Pooling = false };
        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand create = adminConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await create.ExecuteNonQueryAsync();
        }

        string root = Path.Combine(Path.GetTempPath(), $"photoidentity-pg-purge-integration-{Guid.NewGuid():N}");
        string analysisRoot = Path.Combine(root, "analysis");
        string reviewRoot = Path.Combine(root, "review");
        string detectorRoot = Path.Combine(root, "detector");
        Directory.CreateDirectory(analysisRoot);
        Directory.CreateDirectory(reviewRoot);
        Directory.CreateDirectory(detectorRoot);

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };
            await using PostgresCatalogueDatabase database = new(testBuilder.ConnectionString);
            await database.InitializeAsync();

            SourceId sourceId = SourceId.New();
            AssetId purgedAssetId = AssetId.New();
            AssetRevisionId purgedRevisionId = AssetRevisionId.New();
            FaceOccurrenceId purgedFaceId = FaceOccurrenceId.New();
            AssetId retainedAssetId = AssetId.New();
            AssetRevisionId retainedRevisionId = AssetRevisionId.New();
            FaceOccurrenceId retainedFaceId = FaceOccurrenceId.New();
            FaceCropId cropId = FaceCropId.New();
            PersonId sharedPersonId = PersonId.New();
            Guid analysisRunId = Guid.NewGuid();
            Guid detectorRunId = Guid.NewGuid();
            DateTimeOffset now = new(2026, 9, 26, 1, 0, 0, TimeSpan.Zero);
            const string sourceKey = "Private/excluded.jpg";
            const string retainedSourceKey = "Private/keep.jpg";
            const string proxyRelative = "archive/proxy.jpg";
            const string faceReviewRelative = "faces/review.jpg";
            string faceCropRelative = $"runs/{analysisRunId:D}/assets/{purgedRevisionId}/faces/face-001/aligned.png";
            string analysisDirectory = Path.Combine(
                analysisRoot,
                "runs",
                analysisRunId.ToString("D"),
                "assets",
                purgedRevisionId.ToString());
            string detectorDirectory = Path.Combine(
                detectorRoot,
                "rollouts",
                detectorRunId.ToString("D"),
                "assets",
                purgedRevisionId.ToString());
            string proxyPath = Resolve(reviewRoot, proxyRelative);
            string faceReviewPath = Resolve(reviewRoot, faceReviewRelative);
            string faceCropPath = Resolve(analysisRoot, faceCropRelative);

            await SeedAsync(
                testBuilder.ConnectionString,
                sourceId,
                purgedAssetId,
                purgedRevisionId,
                purgedFaceId,
                retainedAssetId,
                retainedRevisionId,
                retainedFaceId,
                cropId,
                sharedPersonId,
                analysisRunId,
                detectorRunId,
                sourceKey,
                retainedSourceKey,
                proxyRelative,
                faceReviewRelative,
                faceCropRelative,
                analysisRoot,
                detectorRoot,
                now);

            await WriteAsync(proxyPath);
            await WriteAsync(faceReviewPath);
            await WriteAsync(faceCropPath);
            await WriteAsync(Path.Combine(analysisDirectory, "analysis.json"));
            await WriteAsync(Path.Combine(detectorDirectory, "candidate-000", "inspection.png"));

            PostgresSourceCopyExclusionRepository exclusions = new(database);
            await exclusions.ExcludeAsync(sourceId, sourceKey, now.AddMinutes(1));
            Assert.False(await exclusions.RestoreAsync(sourceId, sourceKey));

            SourceCopyPurgeService service = new(
                exclusions,
                new PostgresSourceCopyPurgeRepository(database),
                new SourceCopyPurgeRoots(analysisRoot, reviewRoot, detectorRoot),
                new SourceCopyPurgeFileSystem(),
                TimeProvider.System);

            Assert.True(await service.PurgeAsync(sourceId, sourceKey));
            Assert.True(await service.PurgeAsync(sourceId, sourceKey));

            Assert.False(File.Exists(proxyPath));
            Assert.False(File.Exists(faceReviewPath));
            Assert.False(File.Exists(faceCropPath));
            Assert.False(Directory.Exists(analysisDirectory));
            Assert.False(Directory.Exists(detectorDirectory));

            await using (NpgsqlConnection connection = new(testBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                Assert.Equal(1L, await CountAsync(connection, "assets"));
                Assert.Equal(1L, await CountAsync(connection, "asset_revisions"));
                Assert.Equal(1L, await CountAsync(connection, "face_occurrences"));
                Assert.Equal(0L, await CountAsync(connection, "face_crops"));
                Assert.Equal(0L, await CountAsync(connection, "embeddings"));
                Assert.Equal(1L, await CountAsync(connection, "people"));
                Assert.Equal(1L, await CountAsync(connection, "person_labels"));
                Assert.Equal(0L, await CountAsync(connection, "identity_suggestions"));
                Assert.Equal(0L, await CountAsync(connection, "identity_suggestion_review_actions"));
                Assert.Equal(0L, await CountAsync(connection, "review_actions"));
                Assert.Equal(0L, await CountAsync(connection, "asset_revision_review_proxies"));
                Assert.Equal(0L, await CountAsync(connection, "face_review_derivatives"));
                Assert.Equal(0L, await CountAsync(connection, "asset_revision_analysis"));
                Assert.Equal(0L, await CountAsync(connection, "detector_reconciliation_plans"));
                Assert.Equal(0L, await CountAsync(connection, "detector_reconciliation_candidates"));
                Assert.Equal(1L, await CountAsync(connection, "source_copy_exclusions"));
                Assert.Equal(0L, await CountAsync(connection, "source_copy_purge_manifests"));
                Assert.Equal(0L, await CountAsync(connection, "source_copy_purge_manifest_artifacts"));
            }

            SourceCopyExclusionState state = Assert.IsType<SourceCopyExclusionState>(
                await exclusions.GetAsync(sourceId, sourceKey));
            Assert.Equal(SourceCopyPurgeStates.Completed, state.PurgeState);
            Assert.Null(state.PurgeErrorCode);

            Assert.True(await exclusions.RestoreAsync(sourceId, sourceKey));
            Assert.Null(await exclusions.GetAsync(sourceId, sourceKey));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAsync(
        string connectionString,
        SourceId sourceId,
        AssetId purgedAssetId,
        AssetRevisionId purgedRevisionId,
        FaceOccurrenceId purgedFaceId,
        AssetId retainedAssetId,
        AssetRevisionId retainedRevisionId,
        FaceOccurrenceId retainedFaceId,
        FaceCropId cropId,
        PersonId sharedPersonId,
        Guid analysisRunId,
        Guid detectorRunId,
        string sourceKey,
        string retainedSourceKey,
        string proxyRelative,
        string faceReviewRelative,
        string faceCropRelative,
        string analysisRoot,
        string detectorRoot,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'local-folder', 'private-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES
                (@purged_asset_id, @source_id, @source_key, @now, @now),
                (@retained_asset_id, @source_id, @retained_source_key, @now, @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES
                (@purged_revision_id, @purged_asset_id, @purged_hash, 4, @now, 'image/jpeg', 10, 10),
                (@retained_revision_id, @retained_asset_id, @retained_hash, 4, @now, 'image/jpeg', 10, 10);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES
                (@purged_face_id, @purged_revision_id, 0, @now),
                (@retained_face_id, @retained_revision_id, 0, @now);

            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256,
                storage_path, width, height, created_at_utc)
            VALUES (
                @crop_id, @purged_face_id, 'aligned-test', @crop_hash,
                @crop_path, 8, 8, @now);

            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES (
                @crop_id, 'purge-test', @embedding_hash, 2, 1.0, decode('00000000', 'hex'), @now);

            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
            VALUES (@person_id, 'Shared Person', @now, NULL);

            INSERT INTO person_labels (
                person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc, note)
            VALUES
                (@person_id, @purged_face_id, 'manual', 'purge-test', @now, NULL),
                (@person_id, @retained_face_id, 'manual', 'purge-test', @now, NULL);

            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, note, created_at_utc, reversed_at_utc, reverses_action_id)
            VALUES (
                @purged_face_id,
                'assign',
                @person_id,
                (SELECT id FROM person_labels
                 WHERE person_id = @person_id AND face_occurrence_id = @purged_face_id),
                'purge-test',
                NULL,
                @now,
                @now,
                NULL);

            INSERT INTO review_actions (
                face_occurrence_id, action_kind, person_id, person_label_id,
                actor, note, created_at_utc, reversed_at_utc, reverses_action_id)
            VALUES (
                @purged_face_id,
                'undo',
                NULL,
                NULL,
                'purge-test',
                NULL,
                @now,
                NULL,
                (SELECT id FROM review_actions
                 WHERE face_occurrence_id = @purged_face_id AND action_kind = 'assign'));

            INSERT INTO identity_suggestions (
                face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
            VALUES (
                @purged_face_id, @person_id, 'purge-test', @suggestion_hash,
                0.9, 'accepted', @now);

            INSERT INTO identity_suggestion_review_actions (
                suggestion_id, action_kind, review_action_id, actor, note, created_at_utc)
            VALUES (
                (SELECT id FROM identity_suggestions WHERE face_occurrence_id = @purged_face_id),
                'accept',
                (SELECT id FROM review_actions
                 WHERE face_occurrence_id = @purged_face_id AND action_kind = 'assign'),
                'purge-test',
                NULL,
                @now);

            INSERT INTO archive_review_proxy_profiles (
                profile_id, protocol_version, encoder, format, jpeg_quality,
                maximum_long_edge, resize_policy, canonical_definition, recorded_at_utc)
            VALUES (
                'purge-test', 'v1', 'test', 'jpeg', 85,
                1600, 'fit', 'purge integration proxy', @now);

            INSERT INTO asset_revision_review_proxies (
                asset_revision_id, profile_id, encoded_byte_length, content_sha256,
                width, height, generated_at_utc, relative_path)
            VALUES (
                @purged_revision_id, 'purge-test', 4, @proxy_hash,
                10, 10, @now, @proxy_path);

            INSERT INTO face_review_derivatives (
                face_occurrence_id, profile_id, encoded_byte_length, content_sha256,
                width, height, generated_at_utc, relative_path)
            VALUES (
                @purged_face_id, 'purge-test', 4, @face_review_hash,
                8, 8, @now, @face_review_path);

            INSERT INTO archive_analysis_profiles (
                profile_hash, detector_pipeline_hash, detector_model_id, detector_model_hash,
                embedder_model_id, embedder_model_hash, alignment_protocol,
                canonical_definition, recorded_at_utc)
            VALUES (
                @analysis_profile_hash, @analysis_pipeline_hash,
                'detector', @detector_model_hash,
                'embedder', @embedder_model_hash,
                'alignment', 'purge integration analysis', @now);

            INSERT INTO processing_runs (
                id, status, configuration_json, started_at_utc, completed_at_utc)
            VALUES
                (@analysis_run_id, 'completed', CAST(@analysis_config AS jsonb), @now, @now),
                (@detector_run_id, 'completed', CAST(@detector_config AS jsonb), @now, @now);

            INSERT INTO archive_analysis_runs (processing_run_id, profile_hash, registered_at_utc)
            VALUES (@analysis_run_id, @analysis_profile_hash, @now);

            INSERT INTO asset_revision_analysis (
                asset_revision_id, profile_hash, processing_run_id, completed_at_utc)
            VALUES (@purged_revision_id, @analysis_profile_hash, @analysis_run_id, @now);

            INSERT INTO detector_pipelines (
                pipeline_hash, detector_model_id, detector_model_hash,
                canonical_definition, recorded_at_utc)
            VALUES (
                @rollout_pipeline_hash, 'detector', @rollout_detector_hash,
                'purge integration detector', @now);

            INSERT INTO processing_run_detector_pipelines (
                processing_run_id, pipeline_hash, recorded_at_utc)
            VALUES (@detector_run_id, @rollout_pipeline_hash, @now);

            INSERT INTO detector_reconciliation_plans (
                processing_run_id, asset_revision_id, pipeline_hash, planned_at_utc)
            VALUES (@detector_run_id, @purged_revision_id, @rollout_pipeline_hash, @now);

            INSERT INTO detector_reconciliation_candidates (
                processing_run_id, asset_revision_id, candidate_index, disposition,
                proposed_face_occurrence_id, bounding_box_json, landmarks_json,
                applied_face_occurrence_id, applied_at_utc)
            VALUES (
                @detector_run_id, @purged_revision_id, 0, 'existing',
                @purged_face_id, '{}'::jsonb, '{}'::jsonb,
                NULL, NULL);
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("purged_asset_id", purgedAssetId.Value);
        command.Parameters.AddWithValue("purged_revision_id", purgedRevisionId.Value);
        command.Parameters.AddWithValue("purged_face_id", purgedFaceId.Value);
        command.Parameters.AddWithValue("retained_asset_id", retainedAssetId.Value);
        command.Parameters.AddWithValue("retained_revision_id", retainedRevisionId.Value);
        command.Parameters.AddWithValue("retained_face_id", retainedFaceId.Value);
        command.Parameters.AddWithValue("crop_id", cropId.Value);
        command.Parameters.AddWithValue("person_id", sharedPersonId.Value);
        command.Parameters.AddWithValue("analysis_run_id", analysisRunId);
        command.Parameters.AddWithValue("detector_run_id", detectorRunId);
        command.Parameters.AddWithValue("source_key", sourceKey);
        command.Parameters.AddWithValue("retained_source_key", retainedSourceKey);
        command.Parameters.AddWithValue("crop_path", faceCropRelative);
        command.Parameters.AddWithValue("proxy_path", proxyRelative);
        command.Parameters.AddWithValue("face_review_path", faceReviewRelative);
        command.Parameters.AddWithValue("analysis_config", System.Text.Json.JsonSerializer.Serialize(new { outputRoot = analysisRoot }));
        command.Parameters.AddWithValue("detector_config", System.Text.Json.JsonSerializer.Serialize(new { outputRoot = detectorRoot }));
        command.Parameters.AddWithValue("purged_hash", new string('1', 64));
        command.Parameters.AddWithValue("retained_hash", new string('2', 64));
        command.Parameters.AddWithValue("crop_hash", new string('3', 64));
        command.Parameters.AddWithValue("embedding_hash", new string('4', 64));
        command.Parameters.AddWithValue("suggestion_hash", new string('5', 64));
        command.Parameters.AddWithValue("proxy_hash", new string('6', 64));
        command.Parameters.AddWithValue("face_review_hash", new string('7', 64));
        command.Parameters.AddWithValue("analysis_profile_hash", new string('8', 64));
        command.Parameters.AddWithValue("analysis_pipeline_hash", new string('9', 64));
        command.Parameters.AddWithValue("detector_model_hash", new string('a', 64));
        command.Parameters.AddWithValue("embedder_model_hash", new string('b', 64));
        command.Parameters.AddWithValue("rollout_pipeline_hash", new string('c', 64));
        command.Parameters.AddWithValue("rollout_detector_hash", new string('d', 64));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string Resolve(string root, string relative) =>
        Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static async Task WriteAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "purge-test");
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
