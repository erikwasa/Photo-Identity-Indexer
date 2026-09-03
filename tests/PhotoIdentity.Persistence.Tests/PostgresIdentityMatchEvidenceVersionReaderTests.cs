using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresIdentityMatchEvidenceVersionReaderTests
{
    [Fact]
    public async Task ReadAsync_PreservesEvidenceCountersAndExactModelEmbeddingScope_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_evidence_{Guid.NewGuid():N}";
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

            IIdentityMatchEvidenceVersionReader reader =
                new PostgresIdentityMatchEvidenceVersionReader(database);
            ModelId modelId = new("evidence-model");
            Sha256Digest modelHash = new(new string('a', 64));

            ReviewIdentityMatchEvidenceVersion empty =
                await reader.ReadAsync(modelId, modelHash);
            Assert.Equal(new ReviewIdentityMatchEvidenceVersion(0, 0, 0, 0), empty);

            DateTimeOffset seededAt = new(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
            Guid sourceId = Guid.NewGuid();
            Guid assetId = Guid.NewGuid();
            Guid revisionId = Guid.NewGuid();
            Guid faceId = Guid.NewGuid();
            Guid cropId = Guid.NewGuid();
            Guid assignedPersonId = Guid.NewGuid();
            Guid mergedPersonId = Guid.NewGuid();

            await using NpgsqlConnection connection = new(testBuilder.ConnectionString);
            await connection.OpenAsync();
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText =
                    """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source_id, 'test', 'evidence-root', @seeded_at);

                    INSERT INTO assets (id, source_id, source_key, created_at_utc)
                    VALUES (@asset_id, @source_id, 'photo.jpg', @seeded_at);

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
                        1200,
                        800);

                    INSERT INTO face_occurrences (
                        id,
                        asset_revision_id,
                        ordinal,
                        created_at_utc)
                    VALUES (@face_id, @revision_id, 0, @seeded_at);

                    INSERT INTO face_crops (
                        id,
                        face_occurrence_id,
                        crop_protocol,
                        content_sha256,
                        storage_path,
                        width,
                        height,
                        created_at_utc)
                    VALUES (
                        @crop_id,
                        @face_id,
                        'evidence-crop',
                        @crop_hash,
                        'crops/evidence.jpg',
                        112,
                        112,
                        @seeded_at);

                    INSERT INTO embeddings (
                        id,
                        face_crop_id,
                        model_id,
                        model_hash,
                        dimensions,
                        l2_norm,
                        vector_blob,
                        created_at_utc)
                    VALUES (
                        17,
                        @crop_id,
                        @model_id,
                        @model_hash,
                        1,
                        1.0,
                        @vector_blob,
                        @seeded_at);

                    INSERT INTO people (id, display_name, created_at_utc)
                    VALUES
                        (@assigned_person_id, 'Assigned', @seeded_at),
                        (@merged_person_id, 'Merged', @seeded_at);

                    INSERT INTO person_labels (
                        id,
                        person_id,
                        face_occurrence_id,
                        label_kind,
                        assigned_by,
                        assigned_at_utc)
                    VALUES (
                        21,
                        @assigned_person_id,
                        @face_id,
                        'manual',
                        'maintainer',
                        @seeded_at);

                    INSERT INTO review_actions (
                        id,
                        face_occurrence_id,
                        action_kind,
                        person_id,
                        person_label_id,
                        actor,
                        created_at_utc)
                    VALUES (
                        31,
                        @face_id,
                        'assign',
                        @assigned_person_id,
                        21,
                        'maintainer',
                        @seeded_at);

                    INSERT INTO identity_suggestions (
                        id,
                        face_occurrence_id,
                        suggested_person_id,
                        model_id,
                        model_hash,
                        score,
                        status,
                        created_at_utc)
                    VALUES (
                        41,
                        @face_id,
                        @assigned_person_id,
                        @model_id,
                        @model_hash,
                        0.91,
                        'accepted',
                        @seeded_at);

                    INSERT INTO identity_suggestion_review_actions (
                        id,
                        suggestion_id,
                        action_kind,
                        review_action_id,
                        actor,
                        created_at_utc)
                    VALUES (
                        51,
                        41,
                        'accept',
                        31,
                        'maintainer',
                        @seeded_at);

                    INSERT INTO person_maintenance_actions (
                        id,
                        action_kind,
                        person_id,
                        previous_display_name,
                        target_person_id,
                        new_display_name,
                        actor,
                        created_at_utc,
                        reversible)
                    VALUES (
                        61,
                        'merge',
                        @merged_person_id,
                        'Merged',
                        @assigned_person_id,
                        'Assigned',
                        'maintainer',
                        @seeded_at,
                        FALSE);
                    """;
                seed.Parameters.AddWithValue("source_id", sourceId);
                seed.Parameters.AddWithValue("asset_id", assetId);
                seed.Parameters.AddWithValue("revision_id", revisionId);
                seed.Parameters.AddWithValue("revision_hash", new string('b', 64));
                seed.Parameters.AddWithValue("face_id", faceId);
                seed.Parameters.AddWithValue("crop_id", cropId);
                seed.Parameters.AddWithValue("crop_hash", new string('c', 64));
                seed.Parameters.AddWithValue("model_id", modelId.ToString());
                seed.Parameters.AddWithValue("model_hash", modelHash.ToString());
                seed.Parameters.AddWithValue("vector_blob", new byte[] { 1, 2, 3, 4 });
                seed.Parameters.AddWithValue("assigned_person_id", assignedPersonId);
                seed.Parameters.AddWithValue("merged_person_id", mergedPersonId);
                seed.Parameters.AddWithValue("seeded_at", seededAt);
                await seed.ExecuteNonQueryAsync();
            }

            ReviewIdentityMatchEvidenceVersion actual =
                await reader.ReadAsync(modelId, modelHash);
            Assert.Equal(new ReviewIdentityMatchEvidenceVersion(31, 51, 61, 17), actual);

            ReviewIdentityMatchEvidenceVersion otherRevision = await reader.ReadAsync(
                modelId,
                new Sha256Digest(new string('d', 64)));
            Assert.Equal(new ReviewIdentityMatchEvidenceVersion(31, 51, 61, 0), otherRevision);

            ReviewIdentityMatchEvidenceVersion afterAutomaticAssignments =
                ReviewIdentityMatchEvidenceVersions.ExpectedAfterAutomaticAssignments(actual, 2);
            Assert.Equal(new ReviewIdentityMatchEvidenceVersion(33, 53, 61, 17), afterAutomaticAssignments);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => Task.FromResult(
                    ReviewIdentityMatchEvidenceVersions.ExpectedAfterAutomaticAssignments(actual, -1)));
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
