using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresIdentityMatchRegenerationExecutionTests
{
    [Fact]
    public async Task ScoreAndAutoAssignAsync_PreserveExactModelCanonicalSemantics_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_regen_exec_{Guid.NewGuid():N}";
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

            ModelId modelId = new("regen-exec-model");
            Sha256Digest modelHash = new(new string('a', 64));
            DateTimeOffset seededAt = new(2026, 9, 7, 19, 30, 0, TimeSpan.Zero);
            Guid sourceId = Guid.NewGuid();
            Guid personA = Guid.NewGuid();
            Guid personB = Guid.NewGuid();
            FaceSeed target = FaceSeed.Create("target.jpg", ordinal: 0);
            FaceSeed exemplarA = FaceSeed.Create("a.jpg", ordinal: 0);
            FaceSeed exemplarB = FaceSeed.Create("b.jpg", ordinal: 0);

            await using NpgsqlConnection connection = new(testBuilder.ConnectionString);
            await connection.OpenAsync();
            await using (NpgsqlCommand seed = connection.CreateCommand())
            {
                seed.CommandText =
                    """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES (@source_id, 'test', 'regen-exec-root', @seeded_at);

                    INSERT INTO people (id, display_name, created_at_utc)
                    VALUES
                        (@person_a, 'Alpha', @seeded_at),
                        (@person_b, 'Beta', @seeded_at);
                    """;
                seed.Parameters.AddWithValue("source_id", sourceId);
                seed.Parameters.AddWithValue("seeded_at", seededAt);
                seed.Parameters.AddWithValue("person_a", personA);
                seed.Parameters.AddWithValue("person_b", personB);
                await seed.ExecuteNonQueryAsync();
            }

            await SeedFaceAsync(
                connection,
                sourceId,
                target,
                modelId,
                modelHash,
                new float[] { 1f, 0f },
                embeddingId: 101,
                seededAt);
            await SeedFaceAsync(
                connection,
                sourceId,
                exemplarA,
                modelId,
                modelHash,
                new float[] { 1f, 0f },
                embeddingId: 102,
                seededAt);
            await SeedFaceAsync(
                connection,
                sourceId,
                exemplarB,
                modelId,
                modelHash,
                new float[] { 0f, 1f },
                embeddingId: 103,
                seededAt);

            await using (NpgsqlCommand labels = connection.CreateCommand())
            {
                labels.CommandText =
                    """
                    INSERT INTO person_labels (
                        person_id,
                        face_occurrence_id,
                        label_kind,
                        assigned_by,
                        assigned_at_utc)
                    VALUES
                        (@person_a, @face_a, 'confirmed', 'fixture', @seeded_at),
                        (@person_b, @face_b, 'confirmed', 'fixture', @seeded_at);
                    """;
                labels.Parameters.AddWithValue("person_a", personA);
                labels.Parameters.AddWithValue("person_b", personB);
                labels.Parameters.AddWithValue("face_a", exemplarA.FaceId);
                labels.Parameters.AddWithValue("face_b", exemplarB.FaceId);
                labels.Parameters.AddWithValue("seeded_at", seededAt);
                await labels.ExecuteNonQueryAsync();
            }

            IIdentityMatchRegenerationRepository runs =
                new PostgresIdentityMatchRegenerationRepository(database);
            ReviewIdentityMatchRegenerationRun run = await runs.StartAsync(
                modelId,
                modelHash,
                policyVersion: 2,
                requestedBy: "maintainer",
                requestedAtUtc: seededAt.AddMinutes(1));
            Assert.Equal(1, run.TargetCount);

            IIdentityMatchRegenerationScorer scorer =
                new PostgresIdentityMatchRegenerationScorer(database);
            await scorer.PrepareRunAsync(run);
            await scorer.PrepareRunAsync(run);
            int suggestionCount = await scorer.ScoreTargetAsync(
                modelId,
                modelHash,
                target.FaceOccurrenceId);
            Assert.Equal(2, suggestionCount);
            await scorer.ReleaseRunAsync(run.Id);

            IReviewSuggestionRepository suggestions =
                new PostgresReviewSuggestionRepository(database);
            IReadOnlyList<ReviewIdentitySuggestion> ranked =
                await suggestions.GetSuggestionsAsync(target.FaceOccurrenceId);
            Assert.Equal(2, ranked.Count);
            Assert.Equal(PersonId.From(personA), ranked[0].Person.Id);
            Assert.Equal(1, ranked[0].Rank);
            Assert.Equal(1d, ranked[0].Score, precision: 10);
            Assert.Equal(1d, Assert.IsType<double>(ranked[0].ScoreMargin), precision: 10);
            Assert.Equal(PersonId.From(personB), ranked[1].Person.Id);
            Assert.Equal(2, ranked[1].Rank);

            ReviewIdentitySuggestionPolicy policy = new(
                Version: 2,
                AutoAssignEnabled: true,
                HighScoreThreshold: 0.70,
                HighMarginThreshold: 0.10,
                MediumScoreThreshold: 0.50,
                UpdatedBy: "maintainer",
                UpdatedAtUtc: seededAt);
            IIdentityAutoAssignmentService autoAssignment =
                new PostgresIdentityAutoAssignmentService(database);
            ReviewIdentityAutoAssignmentSummary summary = await autoAssignment.ApplyAsync(
                modelId,
                modelHash,
                policy);
            Assert.Equal(new ReviewIdentityAutoAssignmentSummary(1, 1, 0), summary);

            IReadOnlyList<ReviewIdentitySuggestion> afterAssignment =
                await suggestions.GetSuggestionsAsync(target.FaceOccurrenceId);
            Assert.Equal(ReviewSuggestionStatuses.Accepted, afterAssignment[0].Status);
            Assert.Equal(ReviewSuggestionActionKinds.Accept, afterAssignment[0].LatestAction?.Kind);
            Assert.Equal(
                PostgresIdentityAutoAssignmentService.AutomaticActor,
                afterAssignment[0].LatestAction?.Actor);

            IReviewActionRepository reviewActions =
                new PostgresReviewActionRepository(database);
            IReadOnlyList<ReviewAction> history =
                await reviewActions.GetActionsAsync(target.FaceOccurrenceId);
            ReviewAction assignment = Assert.Single(history);
            Assert.Equal(ReviewActionKinds.Assign, assignment.Kind);
            Assert.Equal(PersonId.From(personA), assignment.PersonId);
            Assert.Equal(PostgresIdentityAutoAssignmentService.AutomaticActor, assignment.Actor);

            ReviewIdentityAutoAssignmentSummary repeated = await autoAssignment.ApplyAsync(
                modelId,
                modelHash,
                policy);
            Assert.Equal(new ReviewIdentityAutoAssignmentSummary(0, 0, 0), repeated);

            ReviewIdentitySuggestionPolicy disabled = policy with
            {
                Version = 3,
                AutoAssignEnabled = false,
            };
            ReviewIdentityAutoAssignmentSummary disabledSummary = await autoAssignment.ApplyAsync(
                modelId,
                modelHash,
                disabled);
            Assert.Equal(new ReviewIdentityAutoAssignmentSummary(0, 0, 0), disabledSummary);
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

    private static async Task SeedFaceAsync(
        NpgsqlConnection connection,
        Guid sourceId,
        FaceSeed face,
        ModelId modelId,
        Sha256Digest modelHash,
        float[] vector,
        long embeddingId,
        DateTimeOffset seededAt)
    {
        double norm = Math.Sqrt(vector.Sum(value => value * value));
        byte[] vectorBlob = new byte[checked(vector.Length * sizeof(float))];
        Buffer.BlockCopy(vector, 0, vectorBlob, 0, vectorBlob.Length);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, @source_key, @seeded_at);

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
            VALUES (@face_id, @revision_id, @ordinal, @seeded_at);

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
                'regen-exec-crop',
                @crop_hash,
                @crop_path,
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
                @embedding_id,
                @crop_id,
                @model_id,
                @model_hash,
                @dimensions,
                @l2_norm,
                @vector_blob,
                @seeded_at);
            """;
        command.Parameters.AddWithValue("asset_id", face.AssetId);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("source_key", face.SourceKey);
        command.Parameters.AddWithValue("seeded_at", seededAt);
        command.Parameters.AddWithValue("revision_id", face.RevisionId);
        command.Parameters.AddWithValue("revision_hash", face.RevisionHash);
        command.Parameters.AddWithValue("face_id", face.FaceId);
        command.Parameters.AddWithValue("ordinal", face.Ordinal);
        command.Parameters.AddWithValue("crop_id", face.CropId);
        command.Parameters.AddWithValue("crop_hash", face.CropHash);
        command.Parameters.AddWithValue("crop_path", $"crops/{face.SourceKey}");
        command.Parameters.AddWithValue("embedding_id", embeddingId);
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("dimensions", vector.Length);
        command.Parameters.AddWithValue("l2_norm", norm);
        command.Parameters.AddWithValue("vector_blob", vectorBlob);
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

    private sealed record FaceSeed(
        Guid AssetId,
        Guid RevisionId,
        FaceOccurrenceId FaceOccurrenceId,
        Guid FaceId,
        Guid CropId,
        string SourceKey,
        int Ordinal,
        string RevisionHash,
        string CropHash)
    {
        public static FaceSeed Create(string sourceKey, int ordinal)
        {
            Guid faceId = Guid.NewGuid();
            return new FaceSeed(
                Guid.NewGuid(),
                Guid.NewGuid(),
                FaceOccurrenceId.From(faceId),
                faceId,
                Guid.NewGuid(),
                sourceKey,
                ordinal,
                new string(sourceKey[0] is >= 'a' and <= 'f' ? sourceKey[0] : 'd', 64),
                new string(sourceKey[0] is >= 'a' and <= 'f' ? sourceKey[0] : 'e', 64));
        }
    }
}
