using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity.Persistence.Tests;

public sealed class PostgresPhotoDetailsRepositoryTests
{
    [Fact]
    public async Task GetAsync_CombinesConfirmedManualAndMetadataDetails_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_details_{Guid.NewGuid():N}";
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

            AssetRevisionId revisionId = AssetRevisionId.From(Guid.NewGuid());
            PersonId confirmedPersonId = PersonId.From(Guid.NewGuid());
            PersonId manualPersonId = PersonId.From(Guid.NewGuid());
            await SeedDetailsAsync(
                testBuilder.ConnectionString,
                Guid.Parse(revisionId.ToString()),
                Guid.Parse(confirmedPersonId.ToString()),
                Guid.Parse(manualPersonId.ToString()));

            PostgresPhotoCaptureMetadataRepository capture = new(database);
            PhotoCaptureMetadata metadata = new(
                takenAtLocal: new DateTime(2026, 9, 9, 16, 0, 0),
                utcOffset: TimeSpan.FromHours(2),
                latitude: 59.3293,
                longitude: 18.0686,
                cameraMake: "Contoso",
                rawTags: [new PhotoMetadataTag("EXIF", "Make", "Contoso")]);
            await capture.SavePhotoMetadataAsync(
                revisionId,
                metadata,
                DateTimeOffset.UtcNow);
            PostgresExtendedPhotoMetadataRepository extended = new(database);
            await extended.SaveAsync(revisionId, metadata);

            PostgresPhotoDetailsRepository repository = new(database);
            PhotoDetails details = await repository.GetAsync(revisionId)
                ?? throw new InvalidOperationException("Seeded revision details were not found.");

            Assert.Equal("details/photo.jpg", details.SourceKey);
            Assert.Equal(2, details.People.Count);
            PhotoDetailsPerson confirmed = Assert.Single(
                details.People,
                person => person.PersonId == confirmedPersonId);
            Assert.Equal("Confirmed Person", confirmed.DisplayName);
            Assert.Equal(1, confirmed.ConfirmedFaceCount);
            Assert.False(confirmed.ManualPresence);

            PhotoDetailsPerson manual = Assert.Single(
                details.People,
                person => person.PersonId == manualPersonId);
            Assert.Equal("Manual Person", manual.DisplayName);
            Assert.Equal(0, manual.ConfirmedFaceCount);
            Assert.True(manual.ManualPresence);

            Assert.NotNull(details.CaptureMetadata);
            Assert.Equal(59.3293, details.CaptureMetadata.Latitude);
            Assert.NotNull(details.ExtendedMetadata);
            Assert.Equal("Contoso", details.ExtendedMetadata.CameraMake);
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText =
                $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedDetailsAsync(
        string connectionString,
        Guid revisionId,
        Guid confirmedPersonId,
        Guid manualPersonId)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        Guid faceId = Guid.NewGuid();
        long labelId;
        DateTimeOffset now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'local-folder', @root_locator, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc)
            VALUES (@asset_id, @source_id, 'details/photo.jpg', @now);

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
                @content_sha256,
                789,
                @now,
                'image/jpeg',
                1600,
                1200);

            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES (@face_id, @revision_id, 0, @now);

            INSERT INTO people (id, display_name, created_at_utc)
            VALUES
                (@confirmed_person_id, 'Confirmed Person', @now),
                (@manual_person_id, 'Manual Person', @now);

            INSERT INTO person_labels (
                person_id,
                face_occurrence_id,
                label_kind,
                assigned_by,
                assigned_at_utc)
            VALUES (
                @confirmed_person_id,
                @face_id,
                'manual',
                'maintainer',
                @now)
            RETURNING id;
            """;
        seed.Parameters.AddWithValue("source_id", sourceId);
        seed.Parameters.AddWithValue("root_locator", $"details-root-{sourceId:N}");
        seed.Parameters.AddWithValue("asset_id", assetId);
        seed.Parameters.AddWithValue("revision_id", revisionId);
        seed.Parameters.AddWithValue("content_sha256", new string('c', 64));
        seed.Parameters.AddWithValue("face_id", faceId);
        seed.Parameters.AddWithValue("confirmed_person_id", confirmedPersonId);
        seed.Parameters.AddWithValue("manual_person_id", manualPersonId);
        seed.Parameters.AddWithValue("now", now);
        labelId = (long)(await seed.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The test person label was not inserted."));

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
            VALUES (
                @face_id,
                'assign',
                @confirmed_person_id,
                @label_id,
                'maintainer',
                @now);

            INSERT INTO photo_person_actions (
                asset_revision_id,
                person_id,
                action_kind,
                actor,
                created_at_utc)
            VALUES (
                @revision_id,
                @manual_person_id,
                'add',
                'maintainer',
                @now);
            """;
        actions.Parameters.AddWithValue("face_id", faceId);
        actions.Parameters.AddWithValue("confirmed_person_id", confirmedPersonId);
        actions.Parameters.AddWithValue("label_id", labelId);
        actions.Parameters.AddWithValue("revision_id", revisionId);
        actions.Parameters.AddWithValue("manual_person_id", manualPersonId);
        actions.Parameters.AddWithValue("now", now);
        await actions.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
