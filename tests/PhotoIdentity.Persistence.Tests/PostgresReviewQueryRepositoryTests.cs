using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresReviewQueryRepositoryTests
{
    [Fact]
    public async Task Queries_preserve_review_state_scope_navigation_and_reversal_when_live_postgres_is_configured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_review_query_{Guid.NewGuid():N}";
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
            Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion, initialization.Health.SchemaVersion);

            Guid sourceId = Guid.NewGuid();
            Guid assetId = Guid.NewGuid();
            Guid revisionId = Guid.NewGuid();
            Guid firstFaceId = Guid.NewGuid();
            Guid secondFaceId = Guid.NewGuid();
            DateTimeOffset seededAt = new(2026, 9, 3, 21, 30, 0, TimeSpan.Zero);

            await using NpgsqlConnection connection = new(testBuilder.ConnectionString);
            await connection.OpenAsync();
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText =
                    """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source_id, 'test', 'audit-root', @seeded_at);

                    INSERT INTO assets (id, source_id, source_key, created_at_utc)
                    VALUES (@asset_id, @source_id, 'folder/photo.jpg', @seeded_at);

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
                        123,
                        @seeded_at,
                        'image/jpeg',
                        1920,
                        1080);

                    INSERT INTO face_occurrences (
                        id,
                        asset_revision_id,
                        ordinal,
                        created_at_utc)
                    VALUES
                        (@first_face_id, @revision_id, 0, @first_face_created_at),
                        (@second_face_id, @revision_id, 1, @second_face_created_at);

                    INSERT INTO face_observations (
                        face_occurrence_id,
                        detector_model_id,
                        detector_model_hash,
                        confidence,
                        bounding_box_json,
                        landmarks_json,
                        observed_at_utc)
                    VALUES
                        (
                            @first_face_id,
                            'detector',
                            @detector_hash,
                            0.90,
                            '{}'::jsonb,
                            '{}'::jsonb,
                            @first_face_created_at),
                        (
                            @second_face_id,
                            'detector',
                            @detector_hash,
                            0.20,
                            '{}'::jsonb,
                            '{}'::jsonb,
                            @second_face_created_at);

                    INSERT INTO face_crops (
                        id,
                        face_occurrence_id,
                        crop_protocol,
                        content_sha256,
                        storage_path,
                        width,
                        height,
                        created_at_utc)
                    VALUES
                        (
                            @first_crop_id,
                            @first_face_id,
                            'audit-crop',
                            @first_crop_hash,
                            'crops/first.jpg',
                            112,
                            112,
                            @first_face_created_at),
                        (
                            @second_crop_id,
                            @second_face_id,
                            'audit-crop',
                            @second_crop_hash,
                            'crops/second.jpg',
                            112,
                            112,
                            @second_face_created_at);
                    """;
                seed.Parameters.AddWithValue("source_id", sourceId);
                seed.Parameters.AddWithValue("asset_id", assetId);
                seed.Parameters.AddWithValue("revision_id", revisionId);
                seed.Parameters.AddWithValue("revision_hash", new string('a', 64));
                seed.Parameters.AddWithValue("first_face_id", firstFaceId);
                seed.Parameters.AddWithValue("second_face_id", secondFaceId);
                seed.Parameters.AddWithValue("first_crop_id", Guid.NewGuid());
                seed.Parameters.AddWithValue("second_crop_id", Guid.NewGuid());
                seed.Parameters.AddWithValue("first_crop_hash", new string('b', 64));
                seed.Parameters.AddWithValue("second_crop_hash", new string('c', 64));
                seed.Parameters.AddWithValue("detector_hash", new string('d', 64));
                seed.Parameters.AddWithValue("seeded_at", seededAt);
                seed.Parameters.AddWithValue("first_face_created_at", seededAt.AddMinutes(1));
                seed.Parameters.AddWithValue("second_face_created_at", seededAt.AddMinutes(2));
                await seed.ExecuteNonQueryAsync();
            }

            IReviewActionRepository review = new PostgresReviewActionRepository(database);
            ReviewPerson assignedPerson = await review.CreatePersonAsync(
                "Assigned Person",
                seededAt.AddMinutes(3));
            ReviewPerson otherPerson = await review.CreatePersonAsync(
                "Other Person",
                seededAt.AddMinutes(3));

            FaceOccurrenceId firstFace = FaceOccurrenceId.From(firstFaceId);
            FaceOccurrenceId secondFace = FaceOccurrenceId.From(secondFaceId);
            ReviewAction firstAssignment = await review.AssignAsync(
                firstFace,
                assignedPerson.Id,
                "maintainer",
                seededAt.AddMinutes(4));
            ReviewAction secondAssignment = await review.AssignAsync(
                secondFace,
                assignedPerson.Id,
                "maintainer",
                seededAt.AddMinutes(5));

            const string modelId = "audit-suggestion";
            string modelHash = new('e', 64);
            long firstSuggestionId = await InsertSuggestionAsync(
                connection,
                firstFaceId,
                assignedPerson.Id,
                modelId,
                modelHash,
                0.93,
                seededAt.AddMinutes(6));
            long secondSuggestionId = await InsertSuggestionAsync(
                connection,
                secondFaceId,
                otherPerson.Id,
                modelId,
                modelHash,
                0.88,
                seededAt.AddMinutes(6));

            await using (NpgsqlCommand rankings = connection.CreateCommand())
            {
                rankings.CommandText =
                    """
                    INSERT INTO identity_suggestion_rankings (
                        face_occurrence_id,
                        model_id,
                        model_hash,
                        rank,
                        suggestion_id,
                        score_margin,
                        generated_at_utc)
                    VALUES
                        (@first_face_id, @model_id, @model_hash, 1, @first_suggestion_id, 0.40, @generated_at_utc),
                        (@second_face_id, @model_id, @model_hash, 1, @second_suggestion_id, 0.25, @generated_at_utc);
                    """;
                rankings.Parameters.AddWithValue("first_face_id", firstFaceId);
                rankings.Parameters.AddWithValue("second_face_id", secondFaceId);
                rankings.Parameters.AddWithValue("model_id", modelId);
                rankings.Parameters.AddWithValue("model_hash", modelHash);
                rankings.Parameters.AddWithValue("first_suggestion_id", firstSuggestionId);
                rankings.Parameters.AddWithValue("second_suggestion_id", secondSuggestionId);
                rankings.Parameters.AddWithValue("generated_at_utc", seededAt.AddMinutes(6));
                await rankings.ExecuteNonQueryAsync();
            }

            PostgresReviewQueryRepository query = new(database);
            CatalogueReviewFace first = Assert.IsType<CatalogueReviewFace>(await query.GetFaceAsync(firstFace));
            Assert.Equal("assigned", first.State);
            Assert.Equal(assignedPerson.Id, first.Person?.Id);
            Assert.Equal(firstAssignment.Id, first.ActiveActionId);
            Assert.Equal("photo.jpg", first.PhotoName);
            Assert.Equal("crops/first.jpg", first.CropStoragePath);
            Assert.Equal(0.90, first.Confidence);
            Assert.Equal(1920, first.PhotoWidth);
            Assert.Equal(AssetRevisionId.From(revisionId), first.RevisionId);
            Assert.Null(await query.GetFaceAsync(FaceOccurrenceId.New()));
            Assert.Equal(2, (await query.GetPeopleAsync()).Count);
            CatalogueReviewFacePage page = await query.GetFacesAsync(limit: 1, state: "assigned");
            Assert.Equal(2, page.Total);
            Assert.Equal(secondFace, Assert.Single(page.Items).Id);
            Assert.Equal(firstFace, Assert.Single((await query.GetFacesAsync(offset: 1, limit: 1, state: "all")).Items).Id);
            CatalogueReviewFaceNavigation navigation = Assert.IsType<CatalogueReviewFaceNavigation>(await query.GetNavigationAsync(secondFace));
            Assert.Null(navigation.PreviousFaceId);
            Assert.Equal(firstFace, navigation.NextFaceId);
            Assert.Equal(1, navigation.Position);
            Assert.Equal(2, navigation.Total);
            Assert.Null(await query.GetNavigationAsync(secondFace, state: "unreviewed"));
            Assert.Equal(2, (await query.GetFacesAsync(state: "all", modelId: new ModelId(modelId), modelHash: new Sha256Digest(modelHash))).Total);
            Assert.Equal(0, (await query.GetFacesAsync(state: "all", modelId: new ModelId(modelId), modelHash: new Sha256Digest(new string('f', 64)))).Total);
            CatalogueReviewModelRevision model = Assert.Single((await query.GetOptionsAsync()).ModelRevisions);
            Assert.Equal(2, model.FaceCount);
            Assert.Equal(new ModelId(modelId), model.ModelId);
            Assert.Empty((await query.GetOptionsAsync()).ProcessingRuns);

            ProcessingRunId runId = ProcessingRunId.New();
            await using (NpgsqlCommand extra = connection.CreateCommand())
            {
                extra.CommandText = """
                    INSERT INTO processing_runs (id, status, configuration_json, started_at_utc)
                    VALUES (@run, 'running', '{}', @now);
                    INSERT INTO processing_jobs (id, processing_run_id, asset_revision_id, status, available_at_utc, idempotency_key)
                    VALUES (@job, @run, @revision, 'queued', @now, 'review-query-test');
                    INSERT INTO face_observations (face_occurrence_id, detector_model_id, detector_model_hash, confidence, bounding_box_json, landmarks_json, observed_at_utc)
                    VALUES (@face, 'new-detector', @hash, 0.99, '[0.1,0.2,0.3,0.4]', '{}', @now);
                    INSERT INTO face_crops (id, face_occurrence_id, crop_protocol, content_sha256, storage_path, width, height, created_at_utc)
                    VALUES (@crop, @face, 'new-protocol', @hash, 'crops/latest.jpg', 112, 112, @now);
                    """;
                extra.Parameters.AddWithValue("run", Guid.Parse(runId.ToString()));
                extra.Parameters.AddWithValue("job", Guid.NewGuid());
                extra.Parameters.AddWithValue("revision", revisionId);
                extra.Parameters.AddWithValue("face", firstFaceId);
                extra.Parameters.AddWithValue("crop", Guid.NewGuid());
                extra.Parameters.AddWithValue("hash", new string('f', 64));
                extra.Parameters.AddWithValue("now", seededAt.AddMinutes(7));
                await extra.ExecuteNonQueryAsync();
            }
            Assert.Equal(2, (await query.GetFacesAsync(state: "all", processingRunId: runId)).Total);
            Assert.Equal(0, (await query.GetFacesAsync(state: "all", processingRunId: ProcessingRunId.New())).Total);
            CatalogueReviewProcessingRun run = Assert.Single((await query.GetOptionsAsync()).ProcessingRuns);
            Assert.Equal(runId, run.Id);
            Assert.Equal(2, run.FaceCount);
            Assert.Null(run.CompletedAtUtc);
            first = (await query.GetFaceAsync(firstFace))!;
            Assert.Equal("crops/latest.jpg", first.CropStoragePath);
            Assert.Equal(0.99, first.Confidence);
            Assert.Contains("0.3", first.BoundingBoxJson);

            await review.MarkUnknownAsync(firstFace, "test", seededAt.AddMinutes(7));
            Assert.Equal("unknown", (await query.GetFaceAsync(firstFace))!.State);
            Assert.Single((await query.GetFacesAsync(state: "unknown")).Items);
            await review.RejectAsync(firstFace, "test", seededAt.AddMinutes(8));
            Assert.Single((await query.GetFacesAsync(state: "rejected")).Items);
            await review.UndoLatestAsync(firstFace, "test", seededAt.AddMinutes(9));
            Assert.Equal("unknown", (await query.GetFaceAsync(firstFace))!.State);
            await review.UndoLatestAsync(firstFace, "test", seededAt.AddMinutes(10));
            Assert.Equal("assigned", (await query.GetFaceAsync(firstFace))!.State);
            await review.UndoLatestAsync(firstFace, "test", seededAt.AddMinutes(11));
            Assert.Equal("unreviewed", (await query.GetFaceAsync(firstFace))!.State);
            Assert.Null((await query.GetFaceAsync(firstFace))!.Person);
            Assert.Single((await query.GetFacesAsync()).Items);
            await Assert.ThrowsAsync<ArgumentException>(() => query.GetFacesAsync(modelId: new ModelId(modelId)));
            await Assert.ThrowsAsync<ArgumentException>(() => query.GetFacesAsync(state: "invalid"));
            await Assert.ThrowsAsync<ArgumentException>(() => query.GetNavigationAsync(firstFace, sort: "invalid"));
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetFaceAsync(firstFace, cancelled.Token));
        }
        finally
        {
            await using (NpgsqlCommand terminateConnections = adminConnection.CreateCommand())
            {
                terminateConnections.CommandText =
                    """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid();
                    """;
                terminateConnections.Parameters.AddWithValue("database_name", databaseName);
                await terminateConnections.ExecuteNonQueryAsync();
            }

            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName};";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> InsertSuggestionAsync(
        NpgsqlConnection connection,
        Guid faceOccurrenceId,
        PersonId personId,
        string modelId,
        string modelHash,
        double score,
        DateTimeOffset createdAtUtc)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO identity_suggestions (
                face_occurrence_id,
                suggested_person_id,
                model_id,
                model_hash,
                score,
                status,
                created_at_utc)
            VALUES (
                @face_occurrence_id,
                @person_id,
                @model_id,
                @model_hash,
                @score,
                'pending',
                @created_at_utc)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("face_occurrence_id", faceOccurrenceId);
        command.Parameters.AddWithValue("person_id", Guid.Parse(personId.ToString()));
        command.Parameters.AddWithValue("model_id", modelId);
        command.Parameters.AddWithValue("model_hash", modelHash);
        command.Parameters.AddWithValue("score", score);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc.ToUniversalTime());
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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
