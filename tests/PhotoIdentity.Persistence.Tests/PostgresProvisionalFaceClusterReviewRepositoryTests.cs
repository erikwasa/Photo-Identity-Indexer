using System.Buffers.Binary;
using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresProvisionalFaceClusterReviewRepositoryTests
{
    [Fact]
    public async Task Review_groups_persist_not_same_feedback_and_replacement_clustering_respects_it_when_live_postgres_is_configured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_cluster_review_{Guid.NewGuid():N}";
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
            Assert.True(await TableExistsAsync(
                testBuilder.ConnectionString,
                "provisional_face_not_same_constraints"));

            DateTimeOffset now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
            Guid revisionId = await SeedRevisionAsync(testBuilder.ConnectionString, now);
            const string modelIdValue = "cluster-review-sface";
            string modelHashValue = new('a', 64);
            ModelId modelId = new(modelIdValue);
            Sha256Digest modelHash = new(modelHashValue);
            ProvisionalFaceClusterPolicy policy = ProvisionalFaceClusterPolicies.InitialDbscan;

            ProvisionalFaceClusterInputFace[] seeded = new ProvisionalFaceClusterInputFace[6];
            for (int index = 0; index < seeded.Length; index++)
            {
                FaceOccurrenceId faceId = FaceOccurrenceId.From(
                    Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"));
                await SeedFaceAsync(
                    testBuilder.ConnectionString,
                    revisionId,
                    faceId,
                    index,
                    [1f, 0f],
                    modelIdValue,
                    modelHashValue,
                    now.AddSeconds(index));
                seeded[index] = new(
                    faceId,
                    AssetRevisionId.From(revisionId),
                    "unreviewed",
                    new EmbeddingVector([1f, 0f]));
            }

            PostgresProvisionalFaceClusterRepository clusterRepository = new(database);
            PostgresProvisionalFaceClusterReviewRepository reviewRepository = new(database);
            ProvisionalFaceDbscanClusterer clusterer = new();

            ProvisionalFaceClusterRun initial = await clusterRepository.StartAsync(
                modelId,
                modelHash,
                policy,
                includeUnknown: false,
                requestedBy: "cluster-review:test",
                requestedAtUtc: now.AddMinutes(1));
            IReadOnlyList<ProvisionalFaceClusterInputFace> initialFaces =
                await clusterRepository.ReadInputSnapshotAsync(initial);
            Assert.Equal(6, initialFaces.Count);
            ProvisionalFaceClusterComputation initialComputation = await clusterer.ComputeAsync(
                initialFaces,
                policy);
            Assert.True(await clusterRepository.CompleteAsync(
                initial,
                initialComputation,
                now.AddMinutes(2)));

            ProvisionalFaceClusterReviewGroupPage initialGroups = await reviewRepository.ListCurrentGroupsAsync(
                modelId,
                modelHash,
                policy.Version,
                includeUnknown: false,
                offset: 0,
                limit: 10);
            ProvisionalFaceClusterReviewGroup initialGroup = Assert.Single(initialGroups.Items);
            Assert.Equal(1, initialGroups.Total);
            Assert.Equal(6, initialGroup.MemberCount);
            Assert.Equal(6, initialGroup.CoreCount);
            Assert.Equal(3, initialGroup.RepresentativeFaceIds.Count);

            IReadOnlyList<ProvisionalFaceClusterReviewMember> members =
                await reviewRepository.ListCurrentGroupMembersAsync(
                    modelId,
                    modelHash,
                    policy.Version,
                    includeUnknown: false,
                    initialGroup.DerivedClusterKey,
                    offset: 0,
                    limit: 10);
            Assert.Equal(6, members.Count);
            Assert.All(members, member => Assert.Equal(ProvisionalFaceClusterMemberRole.Core, member.Role));

            long canonicalActionCount = await CountReviewActionsAsync(testBuilder.ConnectionString);
            FaceOccurrenceId anchor = seeded[0].FaceOccurrenceId;
            FaceOccurrenceId[] exceptions =
            [
                seeded[3].FaceOccurrenceId,
                seeded[4].FaceOccurrenceId,
                seeded[5].FaceOccurrenceId,
            ];
            int recorded = await reviewRepository.RecordNotSameAsync(
                modelId,
                modelHash,
                policy.Version,
                includeUnknown: false,
                initialGroup.DerivedClusterKey,
                anchor,
                exceptions,
                "cluster-review:test",
                now.AddMinutes(3));
            Assert.Equal(3, recorded);
            Assert.Equal(canonicalActionCount, await CountReviewActionsAsync(testBuilder.ConnectionString));

            IReadOnlyList<ProvisionalFaceNotSameConstraint> constraints =
                await reviewRepository.ListNotSameConstraintsAsync(
                    initialFaces.Select(face => face.FaceOccurrenceId).ToArray());
            Assert.Equal(3, constraints.Count);
            Assert.All(constraints, constraint => Assert.Equal(anchor, constraint.LeftFaceOccurrenceId));

            ProvisionalFaceClusterRun replacement = await clusterRepository.StartAsync(
                modelId,
                modelHash,
                policy,
                includeUnknown: false,
                requestedBy: "cluster-review:test",
                requestedAtUtc: now.AddMinutes(4));
            IReadOnlyList<ProvisionalFaceClusterInputFace> replacementFaces =
                await clusterRepository.ReadInputSnapshotAsync(replacement);
            IReadOnlyList<ProvisionalFaceNotSameConstraint> replacementConstraints =
                await reviewRepository.ListNotSameConstraintsAsync(
                    replacementFaces.Select(face => face.FaceOccurrenceId).ToArray());
            ProvisionalFaceClusterComputation replacementComputation = await clusterer.ComputeAsync(
                replacementFaces,
                policy,
                replacementConstraints);
            Assert.Equal(2, replacementComputation.ClusterCount);
            Assert.Equal(0, replacementComputation.NoiseCount);
            Assert.True(await clusterRepository.CompleteAsync(
                replacement,
                replacementComputation,
                now.AddMinutes(5)));

            ProvisionalFaceClusterReviewGroupPage replacementGroups = await reviewRepository.ListCurrentGroupsAsync(
                modelId,
                modelHash,
                policy.Version,
                includeUnknown: false,
                offset: 0,
                limit: 10);
            Assert.Equal(2, replacementGroups.Total);
            Assert.All(replacementGroups.Items, group => Assert.Equal(3, group.MemberCount));
            Assert.Equal(canonicalActionCount, await CountReviewActionsAsync(testBuilder.ConnectionString));
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

    private static async Task<bool> TableExistsAsync(string connectionString, string table)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@table_name) IS NOT NULL;";
        command.Parameters.AddWithValue("table_name", table);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountReviewActionsAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM review_actions;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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
            VALUES (@source_id, 'test', 'cluster-review-root', @now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'private/cluster-review.jpg', @now);
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

    private static async Task SeedFaceAsync(
        string connectionString,
        Guid revisionId,
        FaceOccurrenceId faceId,
        int ordinal,
        float[] values,
        string modelId,
        string modelHash,
        DateTimeOffset createdAtUtc)
    {
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
                @crop_id, @face_id, 'cluster-review-test', @crop_hash,
                @crop_path, 112, 112, @created_at);
            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions,
                l2_norm, vector_blob, created_at_utc)
            VALUES (
                @crop_id, @model_id, @model_hash, @dimensions,
                @norm, @vector_blob, @created_at);
            """;
        command.Parameters.AddWithValue("face_id", faceId.Value);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("ordinal", ordinal);
        command.Parameters.AddWithValue("created_at", createdAtUtc);
        command.Parameters.AddWithValue("detector_hash", new string('c', 64));
        command.Parameters.AddWithValue("crop_id", cropId);
        command.Parameters.AddWithValue("crop_hash", ordinal.ToString("x").PadLeft(64, 'd')[..64]);
        command.Parameters.AddWithValue("crop_path", $"crops/cluster-review-{ordinal}.jpg");
        command.Parameters.AddWithValue("model_id", modelId);
        command.Parameters.AddWithValue("model_hash", modelHash);
        command.Parameters.AddWithValue("dimensions", vector.Dimensions);
        command.Parameters.AddWithValue("norm", vector.L2Norm);
        command.Parameters.AddWithValue("vector_blob", vectorBlob);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier)
    {
        const char quote = (char)34;
        string quoteString = quote.ToString();
        string escaped = identifier.Replace(quoteString, quoteString + quoteString, StringComparison.Ordinal);
        return quoteString + escaped + quoteString;
    }
}
