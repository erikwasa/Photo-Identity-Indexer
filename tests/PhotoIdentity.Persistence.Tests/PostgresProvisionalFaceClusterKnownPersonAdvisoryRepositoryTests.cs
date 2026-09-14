using System.Buffers.Binary;
using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresProvisionalFaceClusterKnownPersonAdvisoryRepositoryTests
{
    [Fact]
    public async Task Current_advisory_is_exact_model_policy_scoped_and_does_not_resurrect_rejected_pairs_when_live_postgres_is_configured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_cluster_advisory_{Guid.NewGuid():N}";
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

            DateTimeOffset now = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
            const string modelIdValue = "cluster-advisory-sface";
            string modelHashValue = new('a', 64);
            string otherModelHashValue = new('b', 64);
            ModelId modelId = new(modelIdValue);
            Sha256Digest modelHash = new(modelHashValue);
            Guid revisionId = await SeedRevisionAsync(testBuilder.ConnectionString, now);
            FaceOccurrenceId[] faces = Enumerable.Range(1, 4)
                .Select(index => FaceOccurrenceId.From(Guid.Parse($"00000000-0000-0000-0000-{index:D12}")))
                .ToArray();

            for (int index = 0; index < faces.Length; index++)
            {
                await SeedFaceAsync(
                    testBuilder.ConnectionString,
                    revisionId,
                    faces[index],
                    index,
                    [1f, 0f],
                    modelIdValue,
                    modelHashValue,
                    now.AddSeconds(index));
            }

            PostgresProvisionalFaceClusterRepository clusterRepository = new(database);
            ProvisionalFaceClusterPolicy clusterPolicy = ProvisionalFaceClusterPolicies.InitialDbscan;
            ProvisionalFaceClusterRun run = await clusterRepository.StartAsync(
                modelId,
                modelHash,
                clusterPolicy,
                includeUnknown: false,
                requestedBy: "cluster-advisory:test",
                requestedAtUtc: now.AddMinutes(1));
            IReadOnlyList<ProvisionalFaceClusterInputFace> snapshot =
                await clusterRepository.ReadInputSnapshotAsync(run);
            ProvisionalFaceClusterComputation computation = await new ProvisionalFaceDbscanClusterer()
                .ComputeAsync(snapshot, clusterPolicy);
            Assert.True(await clusterRepository.CompleteAsync(run, computation, now.AddMinutes(2)));
            string clusterKey = Assert.Single(
                await clusterRepository.ListCurrentGroupsAsync(
                    modelId,
                    modelHash,
                    clusterPolicy.Version,
                    includeUnknown: false,
                    maximumGroups: 10)).DerivedClusterKey;

            Guid alice = Guid.Parse("00000000-0000-0000-0000-000000000101");
            Guid bob = Guid.Parse("00000000-0000-0000-0000-000000000102");
            await SeedSuggestionsAsync(
                testBuilder.ConnectionString,
                faces,
                alice,
                bob,
                modelIdValue,
                modelHashValue,
                otherModelHashValue,
                now.AddMinutes(3));

            PostgresIdentitySuggestionPolicyRepository identityPolicies = new(database);
            PostgresProvisionalFaceClusterKnownPersonAdvisoryRepository repository = new(
                database,
                identityPolicies,
                new FixedTimeProvider(now.AddMinutes(4)));

            ProvisionalFaceClusterKnownPersonAdvisory advisory = Assert.IsType<ProvisionalFaceClusterKnownPersonAdvisory>(
                await repository.GetCurrentAsync(
                    modelId,
                    modelHash,
                    clusterPolicy.Version,
                    includeUnknown: false,
                    clusterKey));

            Assert.Equal(ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Strong, advisory.Status);
            Assert.Equal(PersonId.From(alice), advisory.Candidate!.PersonId);
            Assert.Equal(3, advisory.Candidate.SupportCount);
            Assert.Equal(4, advisory.RankedEvidenceCount);
            Assert.Equal(3, advisory.QualifyingEvidenceCount);
            Assert.Equal(clusterPolicy.Version, advisory.ClusterPolicyVersion);
            Assert.Equal(modelHash, advisory.ModelHash);
            Assert.Equal(ProvisionalFaceClusterKnownPersonAdvisoryPolicy.Initial.Version, advisory.AdvisoryPolicyVersion);
            Assert.False(advisory.CanonicalAssignmentAllowed);

            Assert.Null(await repository.GetCurrentAsync(
                modelId,
                modelHash,
                "different-cluster-policy",
                includeUnknown: false,
                clusterKey));
            Assert.Null(await repository.GetCurrentAsync(
                modelId,
                new Sha256Digest(otherModelHashValue),
                clusterPolicy.Version,
                includeUnknown: false,
                clusterKey));
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
            VALUES (@source_id, 'test', 'cluster-advisory-root', @now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'private/cluster-advisory.jpg', @now);
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
        command.Parameters.AddWithValue("revision_hash", new string('c', 64));
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
                @crop_id, @face_id, 'cluster-advisory-test', @crop_hash,
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
        command.Parameters.AddWithValue("detector_hash", new string('d', 64));
        command.Parameters.AddWithValue("crop_id", cropId);
        command.Parameters.AddWithValue("crop_hash", ordinal.ToString("x").PadLeft(64, 'e')[..64]);
        command.Parameters.AddWithValue("crop_path", $"crops/cluster-advisory-{ordinal}.jpg");
        command.Parameters.AddWithValue("model_id", modelId);
        command.Parameters.AddWithValue("model_hash", modelHash);
        command.Parameters.AddWithValue("dimensions", vector.Dimensions);
        command.Parameters.AddWithValue("norm", vector.L2Norm);
        command.Parameters.AddWithValue("vector_blob", vectorBlob);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedSuggestionsAsync(
        string connectionString,
        IReadOnlyList<FaceOccurrenceId> faces,
        Guid alice,
        Guid bob,
        string modelId,
        string modelHash,
        string otherModelHash,
        DateTimeOffset now)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO people (id, display_name, created_at_utc)
            VALUES
                (@alice, 'Alice Advisory', @now),
                (@bob, 'Bob Competitor', @now);

            INSERT INTO identity_suggestions (
                id, face_occurrence_id, suggested_person_id, model_id, model_hash,
                score, status, created_at_utc)
            VALUES
                (1001, @face1, @alice, @model_id, @model_hash, 0.66, 'pending', @now),
                (1002, @face2, @alice, @model_id, @model_hash, 0.64, 'pending', @now),
                (1003, @face3, @alice, @model_id, @model_hash, 0.62, 'pending', @now),
                (1004, @face4, @bob, @model_id, @model_hash, 0.40, 'pending', @now),
                (1005, @face4, @alice, @model_id, @model_hash, 0.95, 'rejected', @now),
                (1006, @face4, @alice, @model_id, @other_model_hash, 0.99, 'pending', @now);

            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id, model_id, model_hash, rank, suggestion_id,
                score_margin, generated_at_utc)
            VALUES
                (@face1, @model_id, @model_hash, 1, 1001, 0.05, @now),
                (@face2, @model_id, @model_hash, 1, 1002, 0.04, @now),
                (@face3, @model_id, @model_hash, 1, 1003, 0.03, @now),
                (@face4, @model_id, @model_hash, 1, 1004, 0.01, @now),
                (@face4, @model_id, @other_model_hash, 1, 1006, 0.50, @now);
            """;
        command.Parameters.AddWithValue("alice", alice);
        command.Parameters.AddWithValue("bob", bob);
        command.Parameters.AddWithValue("face1", faces[0].Value);
        command.Parameters.AddWithValue("face2", faces[1].Value);
        command.Parameters.AddWithValue("face3", faces[2].Value);
        command.Parameters.AddWithValue("face4", faces[3].Value);
        command.Parameters.AddWithValue("model_id", modelId);
        command.Parameters.AddWithValue("model_hash", modelHash);
        command.Parameters.AddWithValue("other_model_hash", otherModelHash);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
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
