using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPersonAuditRepositoryTests
{
    [Fact]
    public async Task GetFacesAsync_PreservesAssignmentAndSuggestionAuditSemantics_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_audit_{Guid.NewGuid():N}";
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

            IPersonAuditRepository audit = new PostgresPersonAuditRepository(database);

            PersonAuditPage withoutModel = Assert.IsType<PersonAuditPage>(
                await audit.GetFacesAsync(assignedPerson.Id));
            Assert.Equal(2, withoutModel.Total);
            Assert.Equal(0, withoutModel.DisagreementCount);
            Assert.All(withoutModel.Items, item => Assert.Null(item.TopSuggestion));
            Assert.Equal(secondAssignment.Id, withoutModel.Items[0].AssignmentActionId);
            Assert.Equal(firstAssignment.Id, withoutModel.Items[1].AssignmentActionId);

            ModelId exactModelId = new(modelId);
            Sha256Digest exactModelHash = new(modelHash);
            PersonAuditPage withModel = Assert.IsType<PersonAuditPage>(
                await audit.GetFacesAsync(
                    assignedPerson.Id,
                    exactModelId,
                    exactModelHash,
                    sort: PersonAuditSorts.DisagreementFirst));
            Assert.Equal(2, withModel.Total);
            Assert.Equal(1, withModel.DisagreementCount);
            Assert.True(withModel.Items[0].SuggestionDisagrees);
            Assert.Equal(secondFace, withModel.Items[0].Id);
            Assert.Equal("photo.jpg", withModel.Items[0].PhotoName);
            Assert.Equal("image/jpeg", withModel.Items[0].MediaType);
            Assert.Equal(1920, withModel.Items[0].PhotoWidth);
            Assert.Equal(1080, withModel.Items[0].PhotoHeight);
            Assert.Equal("crops/second.jpg", withModel.Items[0].CropStoragePath);
            Assert.Equal(0.20, withModel.Items[0].Confidence);
            Assert.Equal(otherPerson.Id, withModel.Items[0].TopSuggestion?.Person.Id);
            Assert.False(withModel.Items[1].SuggestionDisagrees);
            Assert.Equal(assignedPerson.Id, withModel.Items[1].TopSuggestion?.Person.Id);

            PersonAuditPage disagreements = Assert.IsType<PersonAuditPage>(
                await audit.GetFacesAsync(
                    assignedPerson.Id,
                    exactModelId,
                    exactModelHash,
                    disagreementsOnly: true));
            PersonAuditFace disagreement = Assert.Single(disagreements.Items);
            Assert.Equal(secondFace, disagreement.Id);
            Assert.Equal(1, disagreements.Total);
            Assert.Equal(1, disagreements.DisagreementCount);

            PersonAuditPage confidenceSorted = Assert.IsType<PersonAuditPage>(
                await audit.GetFacesAsync(
                    assignedPerson.Id,
                    exactModelId,
                    exactModelHash,
                    sort: PersonAuditSorts.ConfidenceAscending));
            Assert.Equal(secondFace, confidenceSorted.Items[0].Id);
            Assert.Equal(firstFace, confidenceSorted.Items[1].Id);

            Assert.Null(await audit.GetFacesAsync(PersonId.New()));
            await Assert.ThrowsAsync<ArgumentException>(
                () => audit.GetFacesAsync(
                    assignedPerson.Id,
                    exactModelId,
                    modelHash: null));
            await Assert.ThrowsAsync<ArgumentException>(
                () => audit.GetFacesAsync(
                    assignedPerson.Id,
                    disagreementsOnly: true));
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
