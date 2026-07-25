using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteIdentityCatalogueRepositoryTests
{
    [Fact]
    public async Task Save_human_label_round_trips_without_model_derived_rows()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            FaceOccurrenceId occurrenceId = await SeedOccurrenceAsync(database);
            SqliteIdentityCatalogueRepository repository = new(database);
            DateTimeOffset now = new(2026, 7, 25, 21, 30, 0, TimeSpan.Zero);
            CataloguePerson person = new(PersonId.New(), "Ada Lovelace", now);
            HumanLabelAssignment assignment = new(
                person.Id,
                occurrenceId,
                "confirmed",
                "human:test",
                now,
                "Reviewed against the original photograph.");

            CatalogueHumanLabel persisted = await repository.SaveHumanLabelAsync(person, assignment);

            Assert.Equal(person, await repository.GetPersonAsync(person.Id));
            Assert.Equal(persisted, await repository.GetHumanLabelAsync(persisted.Id));
            Assert.Equal([persisted], await repository.GetHumanLabelsAsync(occurrenceId));
            Assert.Equal(person.Id, persisted.PersonId);
            Assert.Equal(occurrenceId, persisted.FaceOccurrenceId);
            Assert.Equal(assignment.LabelKind, persisted.LabelKind);
            Assert.Equal(assignment.AssignedBy, persisted.AssignedBy);
            Assert.Equal(assignment.AssignedAtUtc, persisted.AssignedAtUtc);
            Assert.Equal(assignment.Note, persisted.Note);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(0, await CountAsync(connection, "face_observations"));
            Assert.Equal(0, await CountAsync(connection, "face_crops"));
            Assert.Equal(0, await CountAsync(connection, "embeddings"));
            Assert.Equal(0, await CountAsync(connection, "identity_suggestions"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_human_label_is_idempotent_and_refreshes_assignment_metadata()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            FaceOccurrenceId occurrenceId = await SeedOccurrenceAsync(database);
            SqliteIdentityCatalogueRepository repository = new(database);
            DateTimeOffset now = new(2026, 7, 25, 21, 35, 0, TimeSpan.Zero);
            CataloguePerson person = new(PersonId.New(), "Grace Hopper", now);
            HumanLabelAssignment first = new(
                person.Id,
                occurrenceId,
                "confirmed",
                "human:first",
                now,
                "Initial review.");
            CatalogueHumanLabel initiallyPersisted = await repository.SaveHumanLabelAsync(person, first);
            HumanLabelAssignment corrected = new(
                person.Id,
                occurrenceId,
                first.LabelKind,
                "human:second",
                now.AddMinutes(5),
                "Confirmed after a second review.");

            CatalogueHumanLabel persistedCorrection = await repository.SaveHumanLabelAsync(person, corrected);

            Assert.Equal(initiallyPersisted.Id, persistedCorrection.Id);
            Assert.Equal(corrected.AssignedBy, persistedCorrection.AssignedBy);
            Assert.Equal(corrected.AssignedAtUtc, persistedCorrection.AssignedAtUtc);
            Assert.Equal(corrected.Note, persistedCorrection.Note);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await CountAsync(connection, "people"));
            Assert.Equal(1, await CountAsync(connection, "person_labels"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_suggestion_versions_by_model_hash_and_preserves_review_status()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            FaceOccurrenceId occurrenceId = await SeedOccurrenceAsync(database);
            SqliteIdentityCatalogueRepository repository = new(database);
            DateTimeOffset now = new(2026, 7, 25, 21, 40, 0, TimeSpan.Zero);
            CataloguePerson person = new(PersonId.New(), "Katherine Johnson", now);
            await repository.SavePersonAsync(person);
            ModelId modelId = new("identity-cosine-v1");
            Sha256Digest firstHash = new(new string('a', 64));
            IdentitySuggestionDraft first = new(
                occurrenceId,
                person.Id,
                modelId,
                firstHash,
                0.81,
                "pending",
                now);
            CatalogueIdentitySuggestion initiallyPersisted = await repository.SaveSuggestionAsync(first);
            CatalogueIdentitySuggestion reviewed = Assert.IsType<CatalogueIdentitySuggestion>(
                await repository.UpdateSuggestionStatusAsync(initiallyPersisted.Id, "accepted"));
            IdentitySuggestionDraft rerun = new(
                occurrenceId,
                person.Id,
                modelId,
                firstHash,
                0.93,
                "pending",
                now.AddMinutes(5));

            CatalogueIdentitySuggestion persistedRerun = await repository.SaveSuggestionAsync(rerun);
            IdentitySuggestionDraft revisedModel = new(
                occurrenceId,
                person.Id,
                modelId,
                new Sha256Digest(new string('b', 64)),
                0.89,
                "pending",
                now.AddMinutes(10));
            CatalogueIdentitySuggestion persistedRevision = await repository.SaveSuggestionAsync(revisedModel);

            Assert.Equal(reviewed.Id, persistedRerun.Id);
            Assert.Equal(rerun.Score, persistedRerun.Score);
            Assert.Equal("accepted", persistedRerun.Status);
            Assert.Equal(first.CreatedAtUtc, persistedRerun.CreatedAtUtc);
            Assert.NotEqual(persistedRerun.Id, persistedRevision.Id);
            Assert.Equal(revisedModel.ModelHash, persistedRevision.ModelHash);

            IReadOnlyList<CatalogueIdentitySuggestion> suggestions = await repository.GetSuggestionsAsync(occurrenceId);
            Assert.Equal(2, suggestions.Count);
            Assert.Contains(persistedRerun, suggestions);
            Assert.Contains(persistedRevision, suggestions);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(2, await CountAsync(connection, "identity_suggestions"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_human_label_rolls_back_new_person_when_occurrence_is_missing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteIdentityCatalogueRepository repository = new(database);
            DateTimeOffset now = new(2026, 7, 25, 21, 45, 0, TimeSpan.Zero);
            CataloguePerson person = new(PersonId.New(), "Missing occurrence", now);
            HumanLabelAssignment assignment = new(
                person.Id,
                FaceOccurrenceId.New(),
                "confirmed",
                "human:test",
                now);

            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => repository.SaveHumanLabelAsync(person, assignment));

            Assert.Equal(19, exception.SqliteErrorCode);
            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(0, await CountAsync(connection, "people"));
            Assert.Equal(0, await CountAsync(connection, "person_labels"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Save_person_round_trips_merge_target_and_rejects_self_merge()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteIdentityCatalogueRepository repository = new(database);
            DateTimeOffset now = new(2026, 7, 25, 21, 50, 0, TimeSpan.Zero);
            CataloguePerson target = new(PersonId.New(), "Canonical person", now);
            await repository.SavePersonAsync(target);
            CataloguePerson duplicate = new(PersonId.New(), "Duplicate person", now, target.Id);

            CataloguePerson persisted = await repository.SavePersonAsync(duplicate);

            Assert.Equal(duplicate, persisted);
            Assert.Equal(target.Id, persisted.MergedIntoPersonId);
            Assert.Throws<ArgumentException>(
                () => new CataloguePerson(target.Id, target.DisplayName, now, target.Id));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<FaceOccurrenceId> SeedOccurrenceAsync(SqliteCatalogueDatabase database)
    {
        DateTimeOffset now = new(2026, 7, 25, 21, 25, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(
            sourceId,
            "local-folder",
            Path.Combine(Path.GetTempPath(), sourceId.ToString()),
            now);
        CatalogueAsset asset = new(assetId, sourceId, "photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string('f', 64)),
            1234,
            now,
            "image/jpeg",
            640,
            480);
        SqliteAssetCatalogueRepository assetRepository = new(database);
        CatalogueAssetRevision persistedRevision = await assetRepository.SaveRevisionAsync(source, asset, revision);
        FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($id, $asset_revision_id, 0, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$id", occurrenceId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", persistedRevision.Id.ToString());
        command.Parameters.AddWithValue("$created_at_utc", now.ToString("O"));
        await command.ExecuteNonQueryAsync();
        return occurrenceId;
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
