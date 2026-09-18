using System.Buffers.Binary;
using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresProvisionalFaceClusterRepositoryTests
{
    [Fact]
    public async Task Cluster_runs_publish_atomically_resume_after_restart_and_refresh_on_new_or_reversed_evidence_when_live_postgres_is_configured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_cluster_run_{Guid.NewGuid():N}";
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
            Assert.True(await TableExistsAsync(testBuilder.ConnectionString, "provisional_face_cluster_runs"));
            Assert.True(await TableExistsAsync(testBuilder.ConnectionString, "provisional_face_cluster_members"));
            Assert.True(await TableExistsAsync(testBuilder.ConnectionString, "provisional_face_cluster_current"));

            DateTimeOffset now = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
            Guid revisionId = await SeedRevisionAsync(testBuilder.ConnectionString, now);
            const string modelIdValue = "cluster-production-sface";
            string modelHashValue = new('e', 64);
            ModelId modelId = new(modelIdValue);
            Sha256Digest modelHash = new(modelHashValue);

            FaceOccurrenceId first = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 0, [1f, 0f], modelIdValue, modelHashValue, now);
            FaceOccurrenceId second = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 1, [0.99f, 0.03f], modelIdValue, modelHashValue, now.AddSeconds(1));
            FaceOccurrenceId third = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 2, [0.98f, -0.04f], modelIdValue, modelHashValue, now.AddSeconds(2));
            FaceOccurrenceId unknown = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 3, [0f, 1f], modelIdValue, modelHashValue, now.AddSeconds(3));
            FaceOccurrenceId rejected = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 4, [1f, 0f], modelIdValue, modelHashValue, now.AddSeconds(4));
            FaceOccurrenceId assigned = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 5, [1f, 0f], modelIdValue, modelHashValue, now.AddSeconds(5));
            _ = await SeedFaceAsync(testBuilder.ConnectionString, revisionId, 6, [1f, 0f], "other-model", new string('f', 64), now.AddSeconds(6));

            IReviewActionRepository review = new PostgresReviewActionRepository(database);
            ReviewPerson person = await review.CreatePersonAsync("Cluster Assigned Person", now.AddMinutes(1));
            await review.MarkUnknownAsync(unknown, "cluster:test", now.AddMinutes(2));
            await review.RejectAsync(rejected, "cluster:test", now.AddMinutes(3));
            await review.AssignAsync(assigned, person.Id, "cluster:test", now.AddMinutes(4));
            long canonicalActionCount = await CountReviewActionsAsync(testBuilder.ConnectionString);

            PostgresProvisionalFaceClusterRepository repository = new(database);
            ProvisionalFaceClusterPolicy policy = ProvisionalFaceClusterPolicies.InitialDbscan;
            ProvisionalFaceClusterRun initial = await repository.StartAsync(
                modelId,
                modelHash,
                policy,
                includeUnknown: false,
                requestedBy: "cluster:test",
                requestedAtUtc: now.AddMinutes(5));
            Assert.Equal(3, initial.TargetCount);

            // Simulate process restart before any work is claimed. A fresh repository finds and
            // resumes the same durable run rather than creating another run.
            PostgresProvisionalFaceClusterRepository afterRestart = new(database);
            ProvisionalFaceClusterRun resumed = Assert.IsType<ProvisionalFaceClusterRun>(
                await afterRestart.GetNextActiveAsync());
            Assert.Equal(initial.Id, resumed.Id);
            IReadOnlyList<ProvisionalFaceClusterInputFace> initialFaces =
                await afterRestart.ReadInputSnapshotAsync(resumed);
            Assert.Equal(3, initialFaces.Count);
            Assert.DoesNotContain(initialFaces, face =>
                face.FaceOccurrenceId == unknown ||
                face.FaceOccurrenceId == rejected ||
                face.FaceOccurrenceId == assigned);

            ProvisionalFaceDbscanClusterer clusterer = new();
            ProvisionalFaceClusterComputation initialComputation = await clusterer.ComputeAsync(
                initialFaces,
                policy);
            Assert.True(await afterRestart.CompleteAsync(
                resumed,
                initialComputation,
                now.AddMinutes(6)));
            ProvisionalFaceClusterRun initialCompleted = Assert.IsType<ProvisionalFaceClusterRun>(
                await afterRestart.GetLatestAsync(modelId, modelHash, policy.Version, includeUnknown: false));
            Assert.Equal(ProvisionalFaceClusterRunStatuses.Completed, initialCompleted.Status);
            Assert.Equal(1, initialCompleted.ClusterCount);
            Assert.Equal(0, initialCompleted.NoiseCount);
            ProvisionalFaceClusterGroupSummary initialGroup = Assert.Single(
                await afterRestart.ListCurrentGroupsAsync(modelId, modelHash, policy.Version, includeUnknown: false));
            Assert.Equal(3, initialGroup.MemberCount);
            Assert.Equal(canonicalActionCount, await CountReviewActionsAsync(testBuilder.ConnectionString));

            // A newly analysed exact-model face invalidates current derived evidence. Automatic
            // refresh starts a replacement run containing old noise/members plus the new face.
            FaceOccurrenceId fourth = await SeedFaceAsync(
                testBuilder.ConnectionString,
                revisionId,
                7,
                [0.97f, 0.02f],
                modelIdValue,
                modelHashValue,
                now.AddMinutes(7));
            Assert.False(await afterRestart.EvidenceStillMatchesAsync(initialCompleted));
            ProvisionalFaceClusterRun refresh = Assert.IsType<ProvisionalFaceClusterRun>(
                await afterRestart.TryStartNextRefreshAsync(
                    "cluster:auto",
                    now.AddMinutes(8),
                    TimeSpan.FromSeconds(30)));
            Assert.Equal(4, refresh.TargetCount);
            IReadOnlyList<ProvisionalFaceClusterInputFace> refreshedFaces =
                await afterRestart.ReadInputSnapshotAsync(refresh);
            Assert.Contains(refreshedFaces, face => face.FaceOccurrenceId == fourth);
            ProvisionalFaceClusterComputation refreshedComputation = await clusterer.ComputeAsync(
                refreshedFaces,
                policy);
            Assert.True(await afterRestart.CompleteAsync(
                refresh,
                refreshedComputation,
                now.AddMinutes(9)));
            ProvisionalFaceClusterRun refreshCompleted = Assert.IsType<ProvisionalFaceClusterRun>(
                await afterRestart.GetLatestAsync(modelId, modelHash, policy.Version, includeUnknown: false));
            Assert.Equal(refresh.Id, refreshCompleted.Id);
            Assert.Equal(ProvisionalFaceClusterRunStatuses.Completed, refreshCompleted.Status);
            Assert.Equal(4, Assert.Single(
                await afterRestart.ListCurrentGroupsAsync(modelId, modelHash, policy.Version, includeUnknown: false)).MemberCount);
            Assert.Equal(canonicalActionCount, await CountReviewActionsAsync(testBuilder.ConnectionString));

            // Unknown participation is a separate explicit scope; the canonical Unknown action is
            // preserved and does not leak into the default scope.
            ProvisionalFaceClusterRun withUnknown = await afterRestart.StartAsync(
                modelId,
                modelHash,
                policy,
                includeUnknown: true,
                requestedBy: "cluster:test",
                requestedAtUtc: now.AddMinutes(10));
            Assert.Equal(5, withUnknown.TargetCount);
            IReadOnlyList<ProvisionalFaceClusterInputFace> withUnknownFaces =
                await afterRestart.ReadInputSnapshotAsync(withUnknown);
            Assert.Contains(withUnknownFaces, face =>
                face.FaceOccurrenceId == unknown && face.ReviewState == CatalogueReviewStates.Unknown);
            Assert.DoesNotContain(withUnknownFaces, face => face.FaceOccurrenceId == rejected || face.FaceOccurrenceId == assigned);
            Assert.Equal(canonicalActionCount, await CountReviewActionsAsync(testBuilder.ConnectionString));

            // Reversal mutates an existing review row without allocating a new action ID. The
            // review-mutation component of the evidence version must still invalidate the current
            // default-scope run so the formerly rejected face can be reconsidered later.
            await ReverseReviewAsync(
                testBuilder.ConnectionString,
                rejected,
                "reject",
                now.AddMinutes(11));
            Assert.False(await afterRestart.EvidenceStillMatchesAsync(refreshCompleted));
            Assert.Equal(canonicalActionCount, await CountReviewActionsAsync(testBuilder.ConnectionString));

            // Automatic replacement waits for a quiet period after canonical review churn. The
            // timestamp comes from the durable review-mutation evidence version, so a repository
            // restart cannot reset the debounce and trigger an immediate expensive rebuild.
            Assert.Null(await afterRestart.TryStartNextRefreshAsync(
                "cluster:auto",
                now.AddMinutes(11).AddSeconds(20),
                TimeSpan.FromSeconds(30)));
            PostgresProvisionalFaceClusterRepository afterDebounceRestart = new(database);
            Assert.Null(await afterDebounceRestart.TryStartNextRefreshAsync(
                "cluster:auto",
                now.AddMinutes(11).AddSeconds(29),
                TimeSpan.FromSeconds(30)));
            ProvisionalFaceClusterRun reviewRefresh = Assert.IsType<ProvisionalFaceClusterRun>(
                await afterDebounceRestart.TryStartNextRefreshAsync(
                    "cluster:auto",
                    now.AddMinutes(11).AddSeconds(31),
                    TimeSpan.FromSeconds(30)));
            Assert.Equal(5, reviewRefresh.TargetCount);
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

    private static async Task ReverseReviewAsync(
        string connectionString,
        FaceOccurrenceId faceOccurrenceId,
        string actionKind,
        DateTimeOffset reversedAtUtc)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE review_actions
            SET reversed_at_utc = @reversed_at_utc
            WHERE face_occurrence_id = @face_id
              AND action_kind = @action_kind
              AND reversed_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("reversed_at_utc", reversedAtUtc);
        command.Parameters.AddWithValue("face_id", faceOccurrenceId.Value);
        command.Parameters.AddWithValue("action_kind", actionKind);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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
            VALUES (@source_id, 'test', 'cluster-production-root', @now);
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
                @crop_id, @face_id, 'cluster-production-test', @crop_hash,
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
