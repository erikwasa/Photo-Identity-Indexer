using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSuggestionGalleryRepositoryTests
{
    [Fact]
    public async Task GalleryAsync_PreservesFiltersPolicyStateAndNavigation_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_gallery_{Guid.NewGuid():N}";
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

            Guid sourceId = Guid.NewGuid();
            Guid firstAssetId = Guid.NewGuid();
            Guid secondAssetId = Guid.NewGuid();
            Guid assignedAssetId = Guid.NewGuid();
            Guid firstRevisionId = Guid.NewGuid();
            Guid secondRevisionId = Guid.NewGuid();
            Guid assignedRevisionId = Guid.NewGuid();
            Guid firstFaceId = Guid.NewGuid();
            Guid secondFaceId = Guid.NewGuid();
            Guid assignedFaceId = Guid.NewGuid();
            Guid suggestedPersonId = Guid.NewGuid();
            Guid assignedPersonId = Guid.NewGuid();
            Guid processingRunId = Guid.NewGuid();
            ModelId modelId = new("gallery-model");
            Sha256Digest modelHash = new(new string('a', 64));
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);

            await SeedAsync(
                database,
                sourceId,
                firstAssetId,
                secondAssetId,
                assignedAssetId,
                firstRevisionId,
                secondRevisionId,
                assignedRevisionId,
                firstFaceId,
                secondFaceId,
                assignedFaceId,
                suggestedPersonId,
                assignedPersonId,
                processingRunId,
                modelId,
                modelHash,
                now);

            ISuggestionGalleryRepository repository =
                new PostgresSuggestionGalleryRepository(database);

            ReviewSuggestionGalleryPage unreviewed = await repository.GetFacesAsync(
                modelId,
                modelHash,
                0,
                40,
                "unreviewed",
                null,
                "created-desc",
                "all",
                null);

            Assert.Equal(2, unreviewed.Total);
            Assert.Equal(FaceOccurrenceId.From(firstFaceId), unreviewed.Items[0].Id);
            Assert.Equal(FaceOccurrenceId.From(secondFaceId), unreviewed.Items[1].Id);
            Assert.Equal("first.jpg", unreviewed.Items[0].PhotoName);
            Assert.Equal(1920, unreviewed.Items[0].PhotoWidth);
            Assert.Equal(1080, unreviewed.Items[0].PhotoHeight);
            Assert.Equal(0.93, unreviewed.Items[0].Confidence);
            Assert.Equal("crop/first.jpg", unreviewed.Items[0].CropStoragePath);
            Assert.NotNull(unreviewed.Items[0].BoundingBoxJson);
            ReviewSuggestionGalleryTopSuggestion top =
                Assert.IsType<ReviewSuggestionGalleryTopSuggestion>(unreviewed.Items[0].TopSuggestion);
            Assert.Equal(PersonId.From(suggestedPersonId), top.Person.Id);
            Assert.Equal("Ada Suggested", top.Person.DisplayName);
            Assert.Equal("high", top.ConfidenceGroup);
            Assert.Null(unreviewed.Items[1].TopSuggestion);

            ReviewSuggestionGalleryPage high = await repository.GetFacesAsync(
                modelId,
                modelHash,
                0,
                40,
                "unreviewed",
                null,
                "confidence-group",
                "high",
                null);
            Assert.Single(high.Items);
            Assert.Equal(FaceOccurrenceId.From(firstFaceId), high.Items[0].Id);

            ReviewSuggestionGalleryPage personFiltered = await repository.GetFacesAsync(
                modelId,
                modelHash,
                0,
                40,
                "unreviewed",
                null,
                "suggested-person",
                "all",
                PersonId.From(suggestedPersonId));
            Assert.Single(personFiltered.Items);
            Assert.Equal(FaceOccurrenceId.From(firstFaceId), personFiltered.Items[0].Id);

            ReviewSuggestionGalleryPage runFiltered = await repository.GetFacesAsync(
                modelId,
                modelHash,
                0,
                40,
                "unreviewed",
                ProcessingRunId.From(processingRunId),
                "created-desc",
                "all",
                null);
            Assert.Single(runFiltered.Items);
            Assert.Equal(FaceOccurrenceId.From(secondFaceId), runFiltered.Items[0].Id);

            ReviewSuggestionGalleryPage assigned = await repository.GetFacesAsync(
                modelId,
                modelHash,
                0,
                40,
                "assigned",
                null,
                "created-desc",
                "all",
                null);
            ReviewSuggestionGalleryFace assignedFace = Assert.Single(assigned.Items);
            Assert.Equal(FaceOccurrenceId.From(assignedFaceId), assignedFace.Id);
            Assert.Equal("assigned", assignedFace.State);
            Assert.Equal(PersonId.From(assignedPersonId), assignedFace.Person?.Id);
            Assert.Equal("Bob Assigned", assignedFace.Person?.DisplayName);

            ReviewSuggestionGalleryNavigation navigation =
                Assert.IsType<ReviewSuggestionGalleryNavigation>(
                    await repository.GetNavigationAsync(
                        FaceOccurrenceId.From(firstFaceId),
                        modelId,
                        modelHash,
                        "unreviewed",
                        null,
                        "created-desc",
                        "all",
                        null));
            Assert.Null(navigation.PreviousFaceId);
            Assert.Equal(FaceOccurrenceId.From(secondFaceId), navigation.NextFaceId);
            Assert.Equal(1, navigation.Position);
            Assert.Equal(2, navigation.Total);
            Assert.Equal("created-desc", navigation.Sort);
        }
        finally
        {
            await DropDatabaseAsync(adminConnection, databaseName, quotedDatabaseName);
        }
    }

    private static async Task SeedAsync(
        PostgresCatalogueDatabase database,
        Guid sourceId,
        Guid firstAssetId,
        Guid secondAssetId,
        Guid assignedAssetId,
        Guid firstRevisionId,
        Guid secondRevisionId,
        Guid assignedRevisionId,
        Guid firstFaceId,
        Guid secondFaceId,
        Guid assignedFaceId,
        Guid suggestedPersonId,
        Guid assignedPersonId,
        Guid processingRunId,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', 'gallery-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES
                (@first_asset_id, @source_id, 'folder/first.jpg', @now),
                (@second_asset_id, @source_id, 'folder/second.jpg', @now),
                (@assigned_asset_id, @source_id, 'folder/assigned.jpg', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES
                (@first_revision_id, @first_asset_id, @first_hash, 10, @now, 'image/jpeg', 1920, 1080),
                (@second_revision_id, @second_asset_id, @second_hash, 20, @now, 'image/jpeg', 1200, 800),
                (@assigned_revision_id, @assigned_asset_id, @assigned_hash, 30, @now, 'image/jpeg', 800, 1200);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES
                (@first_face_id, @first_revision_id, 0, @first_created),
                (@second_face_id, @second_revision_id, 0, @second_created),
                (@assigned_face_id, @assigned_revision_id, 0, @assigned_created);

            INSERT INTO face_observations (
                face_occurrence_id, detector_model_id, detector_model_hash, confidence,
                bounding_box_json, landmarks_json, observed_at_utc)
            VALUES (
                @first_face_id, 'detector', @detector_hash, 0.93,
                '{"x":10,"y":20,"width":100,"height":120}'::jsonb,
                '{}'::jsonb,
                @now);

            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256, storage_path,
                width, height, created_at_utc)
            VALUES (
                @crop_id, @first_face_id, 'test-crop', @crop_hash, 'crop/first.jpg',
                112, 112, @now);

            INSERT INTO people (id, display_name, created_at_utc)
            VALUES
                (@suggested_person_id, 'Ada Suggested', @now),
                (@assigned_person_id, 'Bob Assigned', @now);

            INSERT INTO identity_suggestions (
                id, face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
            VALUES (
                501, @first_face_id, @suggested_person_id, @model_id, @model_hash,
                0.90, 'pending', @now);

            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank, suggestion_id,
                score_margin, generated_at_utc)
            VALUES (
                @first_face_id, @model_id, @model_hash, 1, 501, 0.20, @now);

            INSERT INTO person_labels (
                id, person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
            VALUES (
                701, @assigned_person_id, @assigned_face_id, 'manual', 'maintainer', @now);

            INSERT INTO review_actions (
                id, face_occurrence_id, action_kind, person_id, person_label_id,
                actor, created_at_utc)
            VALUES (
                801, @assigned_face_id, 'assign', @assigned_person_id, 701,
                'maintainer', @now);

            INSERT INTO processing_runs (
                id, status, configuration_json, started_at_utc)
            VALUES (
                @processing_run_id, 'running', '{}'::jsonb, @now);

            INSERT INTO processing_jobs (
                id, processing_run_id, asset_revision_id, status, attempt_count,
                available_at_utc, idempotency_key)
            VALUES (
                @processing_job_id, @processing_run_id, @second_revision_id,
                'queued', 0, @now, 'gallery-run-second');
            """;
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("first_asset_id", firstAssetId);
        command.Parameters.AddWithValue("second_asset_id", secondAssetId);
        command.Parameters.AddWithValue("assigned_asset_id", assignedAssetId);
        command.Parameters.AddWithValue("first_revision_id", firstRevisionId);
        command.Parameters.AddWithValue("second_revision_id", secondRevisionId);
        command.Parameters.AddWithValue("assigned_revision_id", assignedRevisionId);
        command.Parameters.AddWithValue("first_face_id", firstFaceId);
        command.Parameters.AddWithValue("second_face_id", secondFaceId);
        command.Parameters.AddWithValue("assigned_face_id", assignedFaceId);
        command.Parameters.AddWithValue("suggested_person_id", suggestedPersonId);
        command.Parameters.AddWithValue("assigned_person_id", assignedPersonId);
        command.Parameters.AddWithValue("processing_run_id", processingRunId);
        command.Parameters.AddWithValue("processing_job_id", Guid.NewGuid());
        command.Parameters.AddWithValue("crop_id", Guid.NewGuid());
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("first_hash", new string('1', 64));
        command.Parameters.AddWithValue("second_hash", new string('2', 64));
        command.Parameters.AddWithValue("assigned_hash", new string('3', 64));
        command.Parameters.AddWithValue("detector_hash", new string('d', 64));
        command.Parameters.AddWithValue("crop_hash", new string('c', 64));
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("first_created", now.AddMinutes(3));
        command.Parameters.AddWithValue("second_created", now.AddMinutes(2));
        command.Parameters.AddWithValue("assigned_created", now.AddMinutes(1));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(
        NpgsqlConnection adminConnection,
        string databaseName,
        string quotedDatabaseName)
    {
        await using NpgsqlCommand terminate = adminConnection.CreateCommand();
        terminate.CommandText =
            """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @database_name
              AND pid <> pg_backend_pid();
            """;
        terminate.Parameters.AddWithValue("database_name", databaseName);
        await terminate.ExecuteNonQueryAsync();

        await using NpgsqlCommand drop = adminConnection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName};";
        await drop.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
}
