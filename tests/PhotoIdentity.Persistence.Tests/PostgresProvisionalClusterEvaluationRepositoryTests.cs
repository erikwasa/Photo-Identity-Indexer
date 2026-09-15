using System.Buffers.Binary;
using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresProvisionalClusterEvaluationRepositoryTests
{
    [Fact]
    public async Task Reviewed_sample_is_exact_model_bounded_and_excludes_unreviewed_or_rejected_faces_when_live_postgres_is_configured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_cluster_eval_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString) { Pooling = false };
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

            DateTimeOffset now = new(2026, 9, 13, 18, 30, 0, TimeSpan.Zero);
            Guid revisionId = await SeedRevisionAsync(testBuilder.ConnectionString, now);
            const string modelIdValue = "cluster-sface";
            string modelHashValue = new('e', 64);
            ModelId modelId = new(modelIdValue);
            Sha256Digest modelHash = new(modelHashValue);

            FaceOccurrenceId assignedA1 = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 0, [1f, 0f], modelIdValue, modelHashValue, now);
            FaceOccurrenceId assignedA2 = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 1, [0.99f, 0.05f], modelIdValue, modelHashValue, now.AddSeconds(1));
            FaceOccurrenceId assignedB = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 2, [0f, 1f], modelIdValue, modelHashValue, now.AddSeconds(2));
            FaceOccurrenceId unknown = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 3, [0.5f, 0.5f], modelIdValue, modelHashValue, now.AddSeconds(3));
            FaceOccurrenceId rejected = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 4, [0.8f, 0.2f], modelIdValue, modelHashValue, now.AddSeconds(4));
            FaceOccurrenceId unreviewed = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 5, [0.7f, 0.3f], modelIdValue, modelHashValue, now.AddSeconds(5));
            FaceOccurrenceId otherModel = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 6, [1f, 0f], "other-model", new string('f', 64), now.AddSeconds(6));

            IReviewActionRepository review = new PostgresReviewActionRepository(database);
            ReviewPerson personA = await review.CreatePersonAsync("Person A", now.AddMinutes(1));
            ReviewPerson personB = await review.CreatePersonAsync("Person B", now.AddMinutes(2));
            await review.AssignAsync(assignedA1, personA.Id, "cluster-eval:test", now.AddMinutes(3));
            await review.AssignAsync(assignedA2, personA.Id, "cluster-eval:test", now.AddMinutes(4));
            await review.AssignAsync(assignedB, personB.Id, "cluster-eval:test", now.AddMinutes(5));
            await review.MarkUnknownAsync(unknown, "cluster-eval:test", now.AddMinutes(6));
            await review.RejectAsync(rejected, "cluster-eval:test", now.AddMinutes(7));
            await review.AssignAsync(otherModel, personA.Id, "cluster-eval:test", now.AddMinutes(8));

            PostgresProvisionalClusterEvaluationRepository repository = new(database);
            IReadOnlyList<ProvisionalClusterEvaluationFace> withUnknown =
                await repository.ReadReviewedSampleAsync(modelId, modelHash, maximumFaces: 100, includeUnknown: true);

            Assert.Equal(4, withUnknown.Count);
            Assert.Equal(
                withUnknown.OrderBy(face => face.FaceOccurrenceId.ToString(), StringComparer.Ordinal).Select(face => face.FaceOccurrenceId),
                withUnknown.Select(face => face.FaceOccurrenceId));
            Assert.Contains(withUnknown, face => face.FaceOccurrenceId == assignedA1 && face.PersonId == personA.Id);
            Assert.Contains(withUnknown, face => face.FaceOccurrenceId == assignedA2 && face.PersonId == personA.Id);
            Assert.Contains(withUnknown, face => face.FaceOccurrenceId == assignedB && face.PersonId == personB.Id);
            Assert.Contains(withUnknown, face => face.FaceOccurrenceId == unknown && face.PersonId is null && face.ReviewState == CatalogueReviewStates.Unknown);
            Assert.DoesNotContain(withUnknown, face => face.FaceOccurrenceId == rejected || face.FaceOccurrenceId == unreviewed || face.FaceOccurrenceId == otherModel);
            Assert.All(withUnknown, face => Assert.Equal(revisionId.ToString("D"), face.AssetRevisionId.ToString()));
            Assert.All(withUnknown, face => Assert.Equal(2, face.Embedding.Dimensions));
            Assert.All(withUnknown, face => Assert.Equal(0.90, face.DetectorConfidence));
            Assert.All(withUnknown, face => Assert.Equal(0.25, face.FaceAreaFraction));
            Assert.All(withUnknown, face => Assert.False(face.ReviewedPersonWasMerged));
            ProvisionalClusterEvaluationFace assignedA1Export = Assert.Single(
                withUnknown.Where(face => face.FaceOccurrenceId == assignedA1));
            Assert.Equal(now.AddMinutes(3), assignedA1Export.ReviewedAtUtc);

            IReadOnlyList<ProvisionalClusterEvaluationFace> assignedOnly =
                await repository.ReadReviewedSampleAsync(modelId, modelHash, maximumFaces: 2, includeUnknown: false);
            Assert.Equal(2, assignedOnly.Count);
            Assert.All(assignedOnly, face => Assert.Equal(CatalogueReviewStates.Assigned, face.ReviewState));

            IReadOnlyList<ProvisionalClusterEvaluationFace> wrongRevision =
                await repository.ReadReviewedSampleAsync(modelId, new Sha256Digest(new string('a', 64)));
            Assert.Empty(wrongRevision);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.ReadReviewedSampleAsync(modelId, modelHash, maximumFaces: 0));
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
            VALUES (@source_id, 'test', 'cluster-evaluation-root', @now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'private/photo.jpg', @now);
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
                @crop_id, @face_id, 'cluster-evaluation-test', @crop_hash,
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
        string escaped = identifier.Replace(quoteString, quoteString + quoteString, StringComparison.Ordinal);
        return quoteString + escaped + quoteString;
    }
}
