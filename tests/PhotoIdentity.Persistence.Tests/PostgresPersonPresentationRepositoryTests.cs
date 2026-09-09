using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPersonPresentationRepositoryTests
{
    [Fact]
    public async Task Repository_PreservesPreferencesCountsAndRepresentatives_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_person_presentation_{Guid.NewGuid():N}";
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
            Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion, initialization.Health.SchemaVersion);

            PersonId alice = PersonId.New();
            PersonId bob = PersonId.New();
            AssetRevisionId aliceRevision = AssetRevisionId.New();
            AssetRevisionId bobManualRevision = AssetRevisionId.New();
            FaceOccurrenceId aliceFace = FaceOccurrenceId.New();
            await SeedPeopleAndEvidenceAsync(
                testBuilder.ConnectionString,
                alice,
                bob,
                aliceRevision,
                bobManualRevision,
                aliceFace);

            PostgresPersonPresentationRepository repository = new(database);

            IReadOnlyDictionary<PersonId, int> counts = await repository.GetActivePhotoCountsAsync();
            Assert.Equal(1, counts[alice]);
            Assert.Equal(1, counts[bob]);

            await repository.SetFavoriteAsync(alice, true, DateTimeOffset.UtcNow);
            await repository.SetHiddenAsync(bob, true, DateTimeOffset.UtcNow);
            Assert.Contains(alice, await repository.GetFavoritePersonIdsAsync());
            Assert.Contains(bob, await repository.GetHiddenPersonIdsAsync());

            CataloguePersonRepresentativeFace? fallback =
                await repository.ResolveAsync(alice);
            Assert.NotNull(fallback);
            Assert.Equal(aliceFace, fallback.FaceId);
            Assert.False(fallback.IsExplicit);

            await repository.SetFeaturedFaceAsync(alice, aliceFace, DateTimeOffset.UtcNow);
            CataloguePersonRepresentativeFace? explicitFace =
                await repository.ResolveAsync(alice);
            Assert.NotNull(explicitFace);
            Assert.Equal(aliceFace, explicitFace.FaceId);
            Assert.True(explicitFace.IsExplicit);

            IReadOnlyDictionary<PersonId, CataloguePersonRepresentativeFace> all =
                await repository.ResolveAllAsync();
            Assert.True(all[alice].IsExplicit);

            await repository.ClearFeaturedFaceAsync(alice);
            CataloguePersonRepresentativeFace? cleared =
                await repository.ResolveAsync(alice);
            Assert.NotNull(cleared);
            Assert.False(cleared.IsExplicit);

            await repository.SetFavoriteAsync(alice, false, DateTimeOffset.UtcNow);
            await repository.SetHiddenAsync(bob, false, DateTimeOffset.UtcNow);
            Assert.DoesNotContain(alice, await repository.GetFavoritePersonIdsAsync());
            Assert.DoesNotContain(bob, await repository.GetHiddenPersonIdsAsync());
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedPeopleAndEvidenceAsync(
        string connectionString,
        PersonId alice,
        PersonId bob,
        AssetRevisionId aliceRevision,
        AssetRevisionId bobManualRevision,
        FaceOccurrenceId aliceFace)
    {
        Guid sourceId = Guid.NewGuid();
        Guid aliceAsset = Guid.NewGuid();
        Guid bobAsset = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', @root_locator, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES
                (@alice_asset_id, @source_id, 'people/alice.jpg', @now),
                (@bob_asset_id, @source_id, 'people/bob.jpg', @now);

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
                (@alice_revision_id, @alice_asset_id, @alice_hash, 100, @now, 'image/jpeg', 640, 480),
                (@bob_revision_id, @bob_asset_id, @bob_hash, 100, @now, 'image/jpeg', 800, 600);

            INSERT INTO people (id, display_name, created_at_utc)
            VALUES
                (@alice_id, 'Alice', @now),
                (@bob_id, 'Bob', @now);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES (@alice_face_id, @alice_revision_id, 0, @now);

            INSERT INTO person_labels (person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
            VALUES (@alice_id, @alice_face_id, 'manual', 'test', @now)
            RETURNING id;
            """;
        seed.Parameters.AddWithValue("source_id", NpgsqlDbType.Uuid, sourceId);
        seed.Parameters.AddWithValue("root_locator", NpgsqlDbType.Text, $"person-presentation-root-{sourceId:N}");
        seed.Parameters.AddWithValue("alice_asset_id", NpgsqlDbType.Uuid, aliceAsset);
        seed.Parameters.AddWithValue("bob_asset_id", NpgsqlDbType.Uuid, bobAsset);
        seed.Parameters.AddWithValue("alice_revision_id", NpgsqlDbType.Uuid, aliceRevision.Value);
        seed.Parameters.AddWithValue("bob_revision_id", NpgsqlDbType.Uuid, bobManualRevision.Value);
        seed.Parameters.AddWithValue("alice_hash", NpgsqlDbType.Text, new string('a', 64));
        seed.Parameters.AddWithValue("bob_hash", NpgsqlDbType.Text, new string('b', 64));
        seed.Parameters.AddWithValue("alice_id", NpgsqlDbType.Uuid, alice.Value);
        seed.Parameters.AddWithValue("bob_id", NpgsqlDbType.Uuid, bob.Value);
        seed.Parameters.AddWithValue("alice_face_id", NpgsqlDbType.Uuid, aliceFace.Value);
        seed.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        long labelId = Convert.ToInt64(await seed.ExecuteScalarAsync());

        await using NpgsqlCommand actions = connection.CreateCommand();
        actions.CommandText =
            """
            INSERT INTO review_actions (
                face_occurrence_id,
                action_kind,
                person_id,
                person_label_id,
                actor,
                created_at_utc)
            VALUES (@alice_face_id, 'assign', @alice_id, @label_id, 'test', @now);

            INSERT INTO photo_person_actions (
                asset_revision_id,
                person_id,
                action_kind,
                actor,
                created_at_utc)
            VALUES (@bob_revision_id, @bob_id, 'add', 'test', @now);
            """;
        actions.Parameters.AddWithValue("alice_face_id", NpgsqlDbType.Uuid, aliceFace.Value);
        actions.Parameters.AddWithValue("alice_id", NpgsqlDbType.Uuid, alice.Value);
        actions.Parameters.AddWithValue("label_id", NpgsqlDbType.Bigint, labelId);
        actions.Parameters.AddWithValue("bob_revision_id", NpgsqlDbType.Uuid, bobManualRevision.Value);
        actions.Parameters.AddWithValue("bob_id", NpgsqlDbType.Uuid, bob.Value);
        actions.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await actions.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
