using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresIdentityMatchRegenerationRepositoryTests
{
    [Fact]
    public async Task RunAndTargetState_IsDurableReclaimableAndEvidenceGuarded_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_regeneration_{Guid.NewGuid():N}";
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

            ModelId modelId = new("regeneration-model");
            Sha256Digest modelHash = new(new string('a', 64));
            DateTimeOffset now = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
            Seed seed = await SeedThreeFacesAsync(
                testBuilder.ConnectionString,
                modelId,
                modelHash,
                now);

            IIdentityMatchRegenerationRepository repository =
                new PostgresIdentityMatchRegenerationRepository(database);
            ReviewIdentityMatchRegenerationRun run = await repository.StartAsync(
                modelId,
                modelHash,
                policyVersion: 3,
                requestedBy: "maintainer",
                requestedAtUtc: now.AddMinutes(1));

            Assert.Equal(ReviewIdentityMatchRegenerationStatuses.Pending, run.Status);
            Assert.Equal(2, run.TargetCount);
            Assert.Equal(new ReviewIdentityMatchEvidenceVersion(31, 0, 0, 30), run.EvidenceVersion);
            Assert.Equal("maintainer", run.RequestedBy);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.StartAsync(
                modelId,
                modelHash,
                policyVersion: 3,
                requestedBy: "maintainer",
                requestedAtUtc: now.AddMinutes(2)));

            ReviewIdentityMatchRegenerationTarget first =
                Assert.IsType<ReviewIdentityMatchRegenerationTarget>(
                    await repository.ClaimNextTargetAsync(run.Id, now.AddMinutes(3)));
            Assert.Equal(ReviewIdentityMatchRegenerationTargetStatuses.Running, first.Status);
            Assert.NotEqual(seed.ReviewedFaceId, first.FaceOccurrenceId);

            IIdentityMatchRegenerationRepository recreated =
                new PostgresIdentityMatchRegenerationRepository(database);
            ReviewIdentityMatchRegenerationTarget reclaimed =
                Assert.IsType<ReviewIdentityMatchRegenerationTarget>(
                    await recreated.ClaimNextTargetAsync(run.Id, now.AddMinutes(4)));
            Assert.Equal(first.FaceOccurrenceId, reclaimed.FaceOccurrenceId);
            Assert.Equal(first.Ordinal, reclaimed.Ordinal);

            await recreated.CompleteTargetAsync(
                run.Id,
                reclaimed.FaceOccurrenceId,
                suggestionCount: 2,
                now.AddMinutes(5));
            await recreated.CompleteTargetAsync(
                run.Id,
                reclaimed.FaceOccurrenceId,
                suggestionCount: 2,
                now.AddMinutes(6));

            ReviewIdentityMatchRegenerationTarget second =
                Assert.IsType<ReviewIdentityMatchRegenerationTarget>(
                    await recreated.ClaimNextTargetAsync(run.Id, now.AddMinutes(7)));
            Assert.NotEqual(reclaimed.FaceOccurrenceId, second.FaceOccurrenceId);
            Assert.NotEqual(seed.ReviewedFaceId, second.FaceOccurrenceId);
            await recreated.FailTargetAsync(
                run.Id,
                second.FaceOccurrenceId,
                "synthetic scoring failure",
                now.AddMinutes(8));

            Assert.Null(await recreated.ClaimNextTargetAsync(run.Id, now.AddMinutes(9)));
            ReviewIdentityMatchRegenerationRun progressed =
                Assert.IsType<ReviewIdentityMatchRegenerationRun>(
                    await recreated.GetLatestAsync(modelId, modelHash));
            Assert.Equal(2, progressed.ProcessedTargetCount);
            Assert.Equal(1, progressed.SuggestedTargetCount);
            Assert.Equal(2, progressed.SuggestionCount);
            Assert.Equal(1, progressed.ErrorCount);

            await recreated.CompleteRunAsync(
                run.Id,
                automaticallyAssignedCount: 1,
                now.AddMinutes(10));
            ReviewIdentityMatchRegenerationRun completed =
                Assert.IsType<ReviewIdentityMatchRegenerationRun>(
                    await new PostgresIdentityMatchRegenerationRepository(database)
                        .GetLatestAsync(modelId, modelHash));
            Assert.Equal(ReviewIdentityMatchRegenerationStatuses.Completed, completed.Status);
            Assert.Equal(1, completed.AutomaticallyAssignedCount);
            Assert.False(completed.IsActive);

            ReviewIdentityMatchRegenerationRun secondRun = await recreated.StartAsync(
                modelId,
                modelHash,
                policyVersion: 3,
                requestedBy: "maintainer",
                requestedAtUtc: now.AddMinutes(11));
            Assert.Equal(2, secondRun.TargetCount);

            await AddEmbeddingEvidenceAsync(
                testBuilder.ConnectionString,
                seed.RevisionId,
                modelId,
                modelHash,
                now.AddMinutes(12));

            Assert.Null(await recreated.ClaimNextTargetAsync(secondRun.Id, now.AddMinutes(13)));
            ReviewIdentityMatchRegenerationRun stale =
                Assert.IsType<ReviewIdentityMatchRegenerationRun>(
                    await recreated.GetLatestAsync(modelId, modelHash));
            Assert.Equal(ReviewIdentityMatchRegenerationStatuses.Stale, stale.Status);
            Assert.NotNull(stale.CompletedAtUtc);
            Assert.Contains("evidence changed", stale.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.False(await recreated.EvidenceStillMatchesAsync(stale));
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

    private static async Task<Seed> SeedThreeFacesAsync(
        string connectionString,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset now)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        Guid face1 = Guid.NewGuid();
        Guid face2 = Guid.NewGuid();
        Guid face3 = Guid.NewGuid();
        Guid crop1 = Guid.NewGuid();
        Guid crop2 = Guid.NewGuid();
        Guid crop3 = Guid.NewGuid();
        Guid personId = Guid.NewGuid();

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', 'regeneration-root', @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'photo.jpg', @now);

            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
            VALUES (
                @revision_id, @asset_id, @revision_hash, 1000, @now, 'image/jpeg', 1600, 900);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES
                (@face_1, @revision_id, 0, @now),
                (@face_2, @revision_id, 1, @now),
                (@face_3, @revision_id, 2, @now);

            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256, storage_path, width, height, created_at_utc)
            VALUES
                (@crop_1, @face_1, 'test-crop', @crop_hash_1, 'crops/1.jpg', 112, 112, @now),
                (@crop_2, @face_2, 'test-crop', @crop_hash_2, 'crops/2.jpg', 112, 112, @now),
                (@crop_3, @face_3, 'test-crop', @crop_hash_3, 'crops/3.jpg', 112, 112, @now);

            INSERT INTO embeddings (
                id, face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES
                (10, @crop_1, @model_id, @model_hash, 1, 1.0, @vector_1, @now),
                (20, @crop_2, @model_id, @model_hash, 1, 1.0, @vector_2, @now),
                (30, @crop_3, @model_id, @model_hash, 1, 1.0, @vector_3, @now);

            INSERT INTO people (id, display_name, created_at_utc)
            VALUES (@person_id, 'Reviewed', @now);

            INSERT INTO person_labels (
                id, person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
            VALUES (21, @person_id, @face_3, 'manual', 'maintainer', @now);

            INSERT INTO review_actions (
                id, face_occurrence_id, action_kind, person_id, person_label_id, actor, created_at_utc)
            VALUES (31, @face_3, 'assign', @person_id, 21, 'maintainer', @now);
            """;
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("asset_id", assetId);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("revision_hash", new string('b', 64));
        command.Parameters.AddWithValue("face_1", face1);
        command.Parameters.AddWithValue("face_2", face2);
        command.Parameters.AddWithValue("face_3", face3);
        command.Parameters.AddWithValue("crop_1", crop1);
        command.Parameters.AddWithValue("crop_2", crop2);
        command.Parameters.AddWithValue("crop_3", crop3);
        command.Parameters.AddWithValue("crop_hash_1", new string('c', 64));
        command.Parameters.AddWithValue("crop_hash_2", new string('d', 64));
        command.Parameters.AddWithValue("crop_hash_3", new string('e', 64));
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("vector_1", new byte[] { 1 });
        command.Parameters.AddWithValue("vector_2", new byte[] { 2 });
        command.Parameters.AddWithValue("vector_3", new byte[] { 3 });
        command.Parameters.AddWithValue("person_id", personId);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();

        return new Seed(revisionId, FaceOccurrenceId.From(face3));
    }

    private static async Task AddEmbeddingEvidenceAsync(
        string connectionString,
        Guid revisionId,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset now)
    {
        Guid faceId = Guid.NewGuid();
        Guid cropId = Guid.NewGuid();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES (@face_id, @revision_id, 3, @now);

            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256, storage_path, width, height, created_at_utc)
            VALUES (
                @crop_id, @face_id, 'test-crop', @crop_hash, 'crops/4.jpg', 112, 112, @now);

            INSERT INTO embeddings (
                id, face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES (40, @crop_id, @model_id, @model_hash, 1, 1.0, @vector, @now);
            """;
        command.Parameters.AddWithValue("face_id", faceId);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("crop_id", cropId);
        command.Parameters.AddWithValue("crop_hash", new string('f', 64));
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("vector", new byte[] { 4 });
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();
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

    private sealed record Seed(Guid RevisionId, FaceOccurrenceId ReviewedFaceId);
}
