using System.Buffers.Binary;
using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresIdentityMultiEvidenceAutoAssignmentTests
{
    [Fact]
    public async Task ApplyAsync_AssignsValidatedMediumCandidateWithFreshIndependentEvidence_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_multi_auto_{Guid.NewGuid():N}";
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

            DateTimeOffset now = new(2026, 9, 15, 20, 0, 0, TimeSpan.Zero);
            ModelId modelId = new("multi-auto-sface");
            Sha256Digest modelHash = new(new string('a', 64));
            Guid personId = Guid.NewGuid();
            await SeedPersonAsync(testBuilder.ConnectionString, personId, now);

            FaceOccurrenceId reference1 = await SeedFaceAsync(
                testBuilder.ConnectionString, "reference-1.jpg", 1, [1f, 0f], modelId, modelHash, now);
            FaceOccurrenceId reference2 = await SeedFaceAsync(
                testBuilder.ConnectionString, "reference-2.jpg", 2, [0.95f, 0.05f], modelId, modelHash, now.AddSeconds(1));
            await SeedLegacyConfirmedAsync(
                testBuilder.ConnectionString,
                personId,
                [reference1, reference2],
                now.AddSeconds(2));

            FaceOccurrenceId target = await SeedFaceAsync(
                testBuilder.ConnectionString, "target.jpg", 10, [0.8f, 0.6f], modelId, modelHash, now.AddSeconds(3));
            FaceOccurrenceId support1 = await SeedFaceAsync(
                testBuilder.ConnectionString, "support-1.jpg", 11, [0.81f, 0.59f], modelId, modelHash, now.AddSeconds(4));
            FaceOccurrenceId support2 = await SeedFaceAsync(
                testBuilder.ConnectionString, "support-2.jpg", 12, [0.79f, 0.61f], modelId, modelHash, now.AddSeconds(5));
            FaceOccurrenceId support3 = await SeedFaceAsync(
                testBuilder.ConnectionString, "support-3.jpg", 13, [0.82f, 0.58f], modelId, modelHash, now.AddSeconds(6));
            FaceOccurrenceId[] clusterFaces = [target, support1, support2, support3];

            PostgresProvisionalFaceClusterRepository clusterRepository = new(database);
            ProvisionalFaceClusterRun clusterRun = await clusterRepository.StartAsync(
                modelId,
                modelHash,
                ProvisionalFaceClusterPolicies.InitialDbscan,
                includeUnknown: true,
                requestedBy: "multi-auto:test",
                requestedAtUtc: now.AddMinutes(1));
            IReadOnlyList<ProvisionalFaceClusterInputFace> snapshot =
                await clusterRepository.ReadInputSnapshotAsync(clusterRun);
            Assert.Equal(4, snapshot.Count);
            const string clusterKey = "multi-evidence-test-cluster";
            ProvisionalFaceClusterComputation computation = new(
                EvaluatedFaceCount: 4,
                ClusterCount: 1,
                NoiseCount: 0,
                Memberships: clusterFaces
                    .Select(face => new ProvisionalFaceClusterComputedMembership(
                        face,
                        clusterKey,
                        ProvisionalFaceClusterMemberRole.Core))
                    .ToArray());
            Assert.True(await clusterRepository.CompleteAsync(
                clusterRun,
                computation,
                now.AddMinutes(2)));

            await SeedRankOneSuggestionsAsync(
                testBuilder.ConnectionString,
                clusterFaces,
                personId,
                modelId,
                modelHash,
                now.AddMinutes(3));

            PostgresIdentityMultiEvidenceAutoAssignmentPolicyRepository multiPolicies = new(
                database,
                new FixedTimeProvider(now.AddMinutes(4)));
            ReviewIdentityMultiEvidenceAutoAssignmentConfiguration enabled = await multiPolicies.UpdateAsync(
                modelId,
                modelHash,
                enabled: true,
                actor: "maintainer");
            Assert.True(enabled.Enabled);

            IIdentityMatchRegenerationRepository regeneration =
                new PostgresIdentityMatchRegenerationRepository(database);
            ReviewIdentityMatchRegenerationRun activeRun = await regeneration.StartAsync(
                modelId,
                modelHash,
                policyVersion: 2,
                requestedBy: "maintainer",
                requestedAtUtc: now.AddMinutes(5));
            Assert.True(activeRun.IsActive);

            ReviewIdentitySuggestionPolicy identityPolicy = new(
                Version: 2,
                AutoAssignEnabled: true,
                HighScoreThreshold: 0.70,
                HighMarginThreshold: 0.10,
                MediumScoreThreshold: 0.50,
                UpdatedBy: "maintainer",
                UpdatedAtUtc: now.AddMinutes(4));
            PostgresIdentityAutoAssignmentService service = new(
                database,
                new FixedTimeProvider(now.AddMinutes(6)));

            ReviewIdentityAutoAssignmentSummary summary = await service.ApplyAsync(
                modelId,
                modelHash,
                identityPolicy);

            Assert.Equal(4, summary.CandidateCount);
            Assert.Equal(4, summary.AssignedCount);
            Assert.Equal(0, summary.SkippedCount);

            IReviewActionRepository reviewActions = new PostgresReviewActionRepository(database);
            IReadOnlyList<ReviewAction> targetHistory = await reviewActions.GetActionsAsync(target);
            ReviewAction targetAssignment = Assert.Single(targetHistory);
            Assert.Equal(ReviewActionKinds.Assign, targetAssignment.Kind);
            Assert.Equal(PersonId.From(personId), targetAssignment.PersonId);
            Assert.Equal(
                PostgresIdentityAutoAssignmentService.MultiEvidenceAutomaticActor,
                targetAssignment.Actor);
            string note = Assert.IsType<string>(targetAssignment.Note);
            Assert.Contains(
                ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial.Version,
                note,
                StringComparison.Ordinal);
            Assert.Contains("independent-reference-support-count=2", note, StringComparison.Ordinal);
            Assert.Contains(clusterRun.Id.ToString(), note, StringComparison.Ordinal);
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

    private static async Task SeedPersonAsync(
        string connectionString,
        Guid personId,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO people (id, display_name, created_at_utc)
            VALUES (@person_id, 'Multi Evidence Person', @now);
            """;
        command.Parameters.AddWithValue("person_id", personId);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<FaceOccurrenceId> SeedFaceAsync(
        string connectionString,
        string sourceKey,
        int discriminator,
        float[] values,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset now)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
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

        char revisionCharacter = "23456789abcdef"[discriminator % 14];
        char cropCharacter = "abcdef01234567"[discriminator % 14];
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', @root, @now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, @source_key, @now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
            VALUES (
                @revision_id, @asset_id, @revision_hash, 1000, @now,
                'image/jpeg', 1920, 1080);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES (@face_id, @revision_id, 0, @now);
            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256,
                storage_path, width, height, created_at_utc)
            VALUES (
                @crop_id, @face_id, 'multi-auto-test', @crop_hash,
                @crop_path, 112, 112, @now);
            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions,
                l2_norm, vector_blob, created_at_utc)
            VALUES (
                @crop_id, @model_id, @model_hash, @dimensions,
                @norm, @vector_blob, @now);
            """;
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("root", $"multi-auto-root-{discriminator}");
        command.Parameters.AddWithValue("asset_id", assetId);
        command.Parameters.AddWithValue("source_key", sourceKey);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("revision_hash", new string(revisionCharacter, 64));
        command.Parameters.AddWithValue("face_id", faceId.Value);
        command.Parameters.AddWithValue("crop_id", cropId);
        command.Parameters.AddWithValue("crop_hash", new string(cropCharacter, 64));
        command.Parameters.AddWithValue("crop_path", $"crops/{sourceKey}");
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("dimensions", vector.Dimensions);
        command.Parameters.AddWithValue("norm", vector.L2Norm);
        command.Parameters.AddWithValue("vector_blob", vectorBlob);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
        return faceId;
    }

    private static async Task SeedLegacyConfirmedAsync(
        string connectionString,
        Guid personId,
        IReadOnlyList<FaceOccurrenceId> faces,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        for (int index = 0; index < faces.Count; index++)
        {
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO person_labels (
                    person_id, face_occurrence_id, label_kind,
                    assigned_by, assigned_at_utc)
                VALUES (@person_id, @face_id, 'confirmed', 'fixture', @now);
                """;
            command.Parameters.AddWithValue("person_id", personId);
            command.Parameters.AddWithValue("face_id", faces[index].Value);
            command.Parameters.AddWithValue("now", now.AddSeconds(index));
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRankOneSuggestionsAsync(
        string connectionString,
        IReadOnlyList<FaceOccurrenceId> faces,
        Guid personId,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        for (int index = 0; index < faces.Count; index++)
        {
            long suggestionId = 8000 + index;
            double score = index == 0 ? 0.60 : 0.62 + (index * 0.01);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO identity_suggestions (
                    id, face_occurrence_id, suggested_person_id,
                    model_id, model_hash, score, status, created_at_utc)
                VALUES (
                    @id, @face_id, @person_id,
                    @model_id, @model_hash, @score, 'pending', @now);
                INSERT INTO identity_suggestion_rankings (
                    face_occurrence_id, model_id, model_hash,
                    rank, suggestion_id, score_margin, generated_at_utc)
                VALUES (
                    @face_id, @model_id, @model_hash,
                    1, @id, @margin, @now);
                """;
            command.Parameters.AddWithValue("id", suggestionId);
            command.Parameters.AddWithValue("face_id", faces[index].Value);
            command.Parameters.AddWithValue("person_id", personId);
            command.Parameters.AddWithValue("model_id", modelId.ToString());
            command.Parameters.AddWithValue("model_hash", modelHash.ToString());
            command.Parameters.AddWithValue("score", score);
            command.Parameters.AddWithValue("margin", 0.05);
            command.Parameters.AddWithValue("now", now.AddSeconds(index));
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        const char quote = (char)34;
        string quoteString = quote.ToString();
        string escaped = identifier.Replace(quoteString, quoteString + quoteString, StringComparison.Ordinal);
        return quoteString + escaped + quoteString;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
