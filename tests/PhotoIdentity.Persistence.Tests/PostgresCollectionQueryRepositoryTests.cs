using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresCollectionQueryRepositoryTests
{
    private static readonly ModelId SuggestionModelId = new("collection-model");
    private static readonly Sha256Digest SuggestionModelHash = new(new string('d', 64));

    [Fact]
    public async Task QueryPhotosAsync_ReturnsConfirmedAndSuggestionBackedCollections_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_collection_{Guid.NewGuid():N}";
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

            PersonId confirmedPersonId = PersonId.From(Guid.NewGuid());
            PersonId suggestedPersonId = PersonId.From(Guid.NewGuid());
            AssetRevisionId confirmedRevisionId = AssetRevisionId.From(Guid.NewGuid());
            AssetRevisionId suggestedRevisionId = AssetRevisionId.From(Guid.NewGuid());
            await SeedCollectionAsync(
                testBuilder.ConnectionString,
                Guid.Parse(confirmedPersonId.ToString()),
                Guid.Parse(suggestedPersonId.ToString()),
                Guid.Parse(confirmedRevisionId.ToString()),
                Guid.Parse(suggestedRevisionId.ToString()));

            PostgresCollectionQueryRepository repository = new(database);
            CollectionPhotoPage confirmed = await repository.QueryPhotosAsync(
                [confirmedPersonId],
                CollectionMatchModes.All);

            CollectionPhoto confirmedPhoto = Assert.Single(confirmed.Items);
            Assert.Equal(confirmedRevisionId, confirmedPhoto.RevisionId);
            Assert.Equal(CollectionReviewStates.Assigned, confirmed.ReviewState);
            CollectionPersonMatch confirmedMatch = Assert.Single(confirmedPhoto.People);
            Assert.Equal(confirmedPersonId, confirmedMatch.PersonId);
            Assert.Equal(1, confirmedMatch.ConfirmedFaceCount);
            Assert.Equal(0, confirmedMatch.SuggestedFaceCount);

            CollectionPhotoPage suggested = await repository.QueryPhotosAsync(
                [suggestedPersonId],
                CollectionMatchModes.All,
                new CollectionSuggestionPolicy(SuggestionModelId, SuggestionModelHash, 0.5),
                CollectionReviewStates.Unreviewed);

            CollectionPhoto suggestedPhoto = Assert.Single(suggested.Items);
            Assert.Equal(suggestedRevisionId, suggestedPhoto.RevisionId);
            Assert.Equal(CollectionReviewStates.Unreviewed, suggested.ReviewState);
            CollectionPersonMatch suggestedMatch = Assert.Single(suggestedPhoto.People);
            Assert.Equal(suggestedPersonId, suggestedMatch.PersonId);
            Assert.Equal(0, suggestedMatch.ConfirmedFaceCount);
            Assert.Equal(1, suggestedMatch.SuggestedFaceCount);
            Assert.Equal(0.91, suggestedMatch.MaximumSuggestionScore);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedCollectionAsync(
        string connectionString,
        Guid confirmedPersonId,
        Guid suggestedPersonId,
        Guid confirmedRevisionId,
        Guid suggestedRevisionId)
    {
        Guid sourceId = Guid.NewGuid();
        Guid confirmedAssetId = Guid.NewGuid();
        Guid suggestedAssetId = Guid.NewGuid();
        Guid confirmedFaceId = Guid.NewGuid();
        Guid suggestedFaceId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'local-folder', @root_locator, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES
                (@confirmed_asset_id, @source_id, 'collection/confirmed.jpg', @now),
                (@suggested_asset_id, @source_id, 'collection/suggested.jpg', @now);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES
                (
                    @confirmed_revision_id,
                    @confirmed_asset_id,
                    @confirmed_hash,
                    100,
                    @now,
                    'image/jpeg',
                    1000,
                    800),
                (
                    @suggested_revision_id,
                    @suggested_asset_id,
                    @suggested_hash,
                    100,
                    @later,
                    'image/jpeg',
                    1200,
                    900);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES
                (@confirmed_face_id, @confirmed_revision_id, 0, @now),
                (@suggested_face_id, @suggested_revision_id, 0, @now);

            INSERT INTO face_observations (
                face_occurrence_id,
                detector_model_id,
                detector_model_hash,
                confidence,
                bounding_box_json,
                landmarks_json,
                observed_at_utc)
            VALUES
                (@confirmed_face_id, 'detector', @detector_hash, 0.95, '{}'::jsonb, '{}'::jsonb, @now),
                (@suggested_face_id, 'detector', @detector_hash, 0.87, '{}'::jsonb, '{}'::jsonb, @now);

            INSERT INTO people (id, display_name, created_at_utc)
            VALUES
                (@confirmed_person_id, 'Confirmed Person', @now),
                (@suggested_person_id, 'Suggested Person', @now);

            WITH inserted_label AS (
                INSERT INTO person_labels (
                    person_id,
                    face_occurrence_id,
                    label_kind,
                    assigned_by,
                    assigned_at_utc)
                VALUES (
                    @confirmed_person_id,
                    @confirmed_face_id,
                    'manual',
                    'maintainer',
                    @now)
                RETURNING id
            )
            INSERT INTO review_actions (
                face_occurrence_id,
                action_kind,
                person_id,
                person_label_id,
                actor,
                created_at_utc)
            SELECT
                @confirmed_face_id,
                'assign',
                @confirmed_person_id,
                id,
                'maintainer',
                @now
            FROM inserted_label;

            WITH inserted_suggestion AS (
                INSERT INTO identity_suggestions (
                    face_occurrence_id,
                    suggested_person_id,
                    model_id,
                    model_hash,
                    score,
                    status,
                    created_at_utc)
                VALUES (
                    @suggested_face_id,
                    @suggested_person_id,
                    @model_id,
                    @model_hash,
                    0.91,
                    'pending',
                    @now)
                RETURNING id
            )
            INSERT INTO identity_suggestion_rankings (
                face_occurrence_id,
                model_id,
                model_hash,
                rank,
                suggestion_id,
                score_margin,
                generated_at_utc)
            SELECT
                @suggested_face_id,
                @model_id,
                @model_hash,
                1,
                id,
                0.10,
                @now
            FROM inserted_suggestion;
            """;
        seed.Parameters.AddWithValue("source_id", sourceId);
        seed.Parameters.AddWithValue("root_locator", $"collection-root-{sourceId:N}");
        seed.Parameters.AddWithValue("confirmed_asset_id", confirmedAssetId);
        seed.Parameters.AddWithValue("suggested_asset_id", suggestedAssetId);
        seed.Parameters.AddWithValue("confirmed_revision_id", confirmedRevisionId);
        seed.Parameters.AddWithValue("suggested_revision_id", suggestedRevisionId);
        seed.Parameters.AddWithValue("confirmed_hash", new string('e', 64));
        seed.Parameters.AddWithValue("suggested_hash", new string('f', 64));
        seed.Parameters.AddWithValue("confirmed_face_id", confirmedFaceId);
        seed.Parameters.AddWithValue("suggested_face_id", suggestedFaceId);
        seed.Parameters.AddWithValue("detector_hash", new string('1', 64));
        seed.Parameters.AddWithValue("confirmed_person_id", confirmedPersonId);
        seed.Parameters.AddWithValue("suggested_person_id", suggestedPersonId);
        seed.Parameters.AddWithValue("model_id", SuggestionModelId.ToString());
        seed.Parameters.AddWithValue("model_hash", SuggestionModelHash.ToString());
        seed.Parameters.AddWithValue("now", now);
        seed.Parameters.AddWithValue("later", now.AddMinutes(1));
        await seed.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
