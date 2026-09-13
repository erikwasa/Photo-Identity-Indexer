using System.Buffers.Binary;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresSimilarFaceRepositoryTests
{
    [Fact]
    public async Task Exact_scan_is_model_scoped_deterministic_and_respects_review_state_when_live_postgres_is_configured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_similar_faces_{Guid.NewGuid():N}";
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

            DateTimeOffset now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
            Guid revisionId = await SeedRevisionAsync(testBuilder.ConnectionString, now);
            const string modelIdValue = "similar-sface";
            string modelHashValue = new('e', 64);
            ModelId modelId = new(modelIdValue);
            Sha256Digest modelHash = new(modelHashValue);

            FaceOccurrenceId source = await SeedFaceAsync(
                testBuilder.ConnectionString, revisionId, 0, [1f, 0f], modelIdValue, modelHashValue, now);
            FaceOccurrenceId near = await SeedFaceAsync(
                testBuilder.ConnectionString, revisionId, 1, [0.99f, 0.10f], modelIdValue, modelHashValue, now.AddSeconds(1));
            FaceOccurrenceId far = await SeedFaceAsync(
                testBuilder.ConnectionString, revisionId, 2, [0.40f, 0.90f], modelIdValue, modelHashValue, now.AddSeconds(2));
            FaceOccurrenceId unknown = await SeedFaceAsync(
                testBuilder.ConnectionString, revisionId, 3, [0.98f, 0.15f], modelIdValue, modelHashValue, now.AddSeconds(3));
            FaceOccurrenceId rejected = await SeedFaceAsync(
                testBuilder.ConnectionString, revisionId, 4, [1f, 0.01f], modelIdValue, modelHashValue, now.AddSeconds(4));
            FaceOccurrenceId assigned = await SeedFaceAsync(
                testBuilder.ConnectionString, revisionId, 5, [1f, 0.02f], modelIdValue, modelHashValue, now.AddSeconds(5));
            _ = await SeedFaceAsync(
                testBuilder.ConnectionString,
                revisionId,
                6,
                [1f, 0f],
                "other-model",
                new string('f', 64),
                now.AddSeconds(6));

            IReviewActionRepository review = new PostgresReviewActionRepository(database);
            ReviewPerson person = await review.CreatePersonAsync("Known person", now.AddMinutes(1));
            await review.AssignAsync(source, person.Id, "test", now.AddMinutes(2));
            await review.AssignAsync(assigned, person.Id, "test", now.AddMinutes(3));
            await review.MarkUnknownAsync(unknown, "test", now.AddMinutes(4));
            await review.RejectAsync(rejected, "test", now.AddMinutes(5));

            PostgresSimilarFaceRepository repository = new(database);
            CatalogueSimilarFaceQueryResult defaultResult = Assert.IsType<CatalogueSimilarFaceQueryResult>(
                await repository.FindSimilarAsync(source, modelId, modelHash, limit: 100));

            Assert.Equal(source, defaultResult.SourceFaceId);
            Assert.Equal(modelId, defaultResult.ModelId);
            Assert.Equal(modelHash, defaultResult.ModelHash);
            Assert.False(defaultResult.IncludeUnknown);
            Assert.Equal(2, defaultResult.ScannedFaceCount);
            Assert.Equal([near, far], defaultResult.Items.Select(item => item.Face.Id).ToArray());
            Assert.True(defaultResult.Items[0].Similarity > defaultResult.Items[1].Similarity);
            Assert.All(defaultResult.Items, item => Assert.Equal(CatalogueReviewStates.Unreviewed, item.Face.State));

            CatalogueSimilarFaceQueryResult rediscovery = Assert.IsType<CatalogueSimilarFaceQueryResult>(
                await repository.FindSimilarAsync(source, modelId, modelHash, includeUnknown: true, limit: 100));
            Assert.True(rediscovery.IncludeUnknown);
            Assert.Equal(3, rediscovery.ScannedFaceCount);
            Assert.Contains(rediscovery.Items, item => item.Face.Id == unknown && item.Face.State == CatalogueReviewStates.Unknown);
            Assert.DoesNotContain(rediscovery.Items, item => item.Face.Id == rejected || item.Face.Id == assigned);

            CatalogueSimilarFaceQueryResult limited = Assert.IsType<CatalogueSimilarFaceQueryResult>(
                await repository.FindSimilarAsync(source, modelId, modelHash, limit: 1));
            Assert.Equal(near, Assert.Single(limited.Items).Face.Id);

            PostgresReviewQueryRepository reviewQuery = new(database);
            Assert.Equal(CatalogueReviewStates.Unknown, (await reviewQuery.GetFaceAsync(unknown))!.State);
            Assert.Null(await repository.FindSimilarAsync(rejected, modelId, modelHash));
            Assert.Null(await repository.FindSimilarAsync(source, modelId, new Sha256Digest(new string('a', 64))));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.FindSimilarAsync(source, modelId, modelHash, limit: 201));
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

    private static async Task<Guid> SeedRevisionAsync(string connectionString, DateTimeOffset now)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', 'similar-face-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'folder/photo.jpg', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
            VALUES (
                @revision_id, @asset_id, @revision_hash, 1000, @now,
                'image/jpeg', 1920, 1080);
            """;
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("asset_id", assetId);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("revision_hash", new string('b', 64));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
        return revisionId;
    }

    private static async Task<FaceOccurrenceId> SeedFaceAsync(
        string connectionString,
        Guid revisionId,
        int ordinal,
        float[] values,
        string modelId,
        string modelHash,
        DateTimeOffset createdAtUtc)
    {
        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        Guid cropId = Guid.NewGuid();
        EmbeddingVector vector = new(values);
        byte[] vectorBlob = new byte[values.Length * sizeof(float)];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                vectorBlob.AsSpan(index * sizeof(float), sizeof(float)),
                values[index]);
        }

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES (@face_id, @revision_id, @ordinal, @created_at);

            INSERT INTO face_observations (
                face_occurrence_id, detector_model_id, detector_model_hash,
                confidence, bounding_box_json, landmarks_json, observed_at_utc)
            VALUES (
                @face_id, 'detector', @detector_hash,
                0.90, '[0.1,0.1,0.5,0.5]'::jsonb, '[]'::jsonb, @created_at);

            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256,
                storage_path, width, height, created_at_utc)
            VALUES (
                @crop_id, @face_id, 'similar-face-test', @crop_hash,
                @crop_path, 112, 112, @created_at);

            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions,
                l2_norm, vector_blob, created_at_utc)
            VALUES (
                @crop_id, @model_id, @model_hash, @dimensions,
                @norm, @vector_blob, @created_at);
            """;
        command.Parameters.AddWithValue("face_id", Guid.Parse(faceId.ToString()));
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("ordinal", ordinal);
        command.Parameters.AddWithValue("created_at", createdAtUtc);
        command.Parameters.AddWithValue("detector_hash", new string('c', 64));
        command.Parameters.AddWithValue("crop_id", cropId);
        command.Parameters.AddWithValue("crop_hash", ordinal.ToString("x").PadLeft(64, 'd')[..64]);
        command.Parameters.AddWithValue("crop_path", $"crops/{ordinal}.jpg");
        command.Parameters.AddWithValue("model_id", modelId);
        command.Parameters.AddWithValue("model_hash", modelHash);
        command.Parameters.AddWithValue("dimensions", vector.Dimensions);
        command.Parameters.AddWithValue("norm", vector.L2Norm);
        command.Parameters.AddWithValue("vector_blob", vectorBlob);
        await command.ExecuteNonQueryAsync();
        return faceId;
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
