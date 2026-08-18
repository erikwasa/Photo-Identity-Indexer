using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionHiddenPersonCompatibilityTests
{
    [Fact]
    public async Task Saved_definition_keeps_hidden_person_and_re_evaluates_with_the_same_person_criterion()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqliteSmartCollectionRepository definitions = new(database, TimeProvider.System);
            SqliteSmartCollectionQueryRepository query = new(database);
            SqlitePersonSmartCollectionVisibilityRepository visibility = new(database);

            PersonId alice = PersonId.New();
            CatalogueAssetRevision revision = await CreateRevisionAsync(catalogue, directory, "alice.jpg", 'a');
            await AssignPersonAsync(database, revision.Id, alice, "Alice");

            SmartCollectionDefinition saved = await definitions.CreateAsync(
                "Alice photos",
                new SmartCollectionFilter(people: [alice]));

            await visibility.SetHiddenAsync(
                alice,
                hidden: true,
                new DateTimeOffset(2026, 8, 18, 18, 30, 0, TimeSpan.Zero));

            SmartCollectionDefinition reopened =
                await definitions.GetAsync(saved.Id) ?? throw new InvalidOperationException();
            Assert.Equal([alice.ToString()], reopened.Filter.People.Select(person => person.ToString()));

            SmartCollectionPhotoPage result = await query.QueryAsync(reopened.Filter);
            Assert.Equal(1, result.Total);
            Assert.Equal(revision.Id, Assert.Single(result.Items).RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<CatalogueAssetRevision> CreateRevisionAsync(
        SqliteAssetCatalogueRepository catalogue,
        string root,
        string sourceKey,
        char hashCharacter)
    {
        DateTimeOffset now = new(2026, 8, 18, 18, 0, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        string sourceRoot = Path.Combine(root, assetId.ToString());
        Directory.CreateDirectory(sourceRoot);
        return await catalogue.SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, now),
            new CatalogueAsset(assetId, sourceId, sourceKey, now),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(new string(hashCharacter, 64)),
                100,
                now,
                "image/jpeg",
                100,
                100));
    }

    private static async Task AssignPersonAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        PersonId personId,
        string displayName)
    {
        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand person = connection.CreateCommand())
        {
            person.Transaction = transaction;
            person.CommandText = "INSERT INTO people (id, display_name, created_at_utc) VALUES ($id, $name, $now);";
            person.Parameters.AddWithValue("$id", personId.ToString());
            person.Parameters.AddWithValue("$name", displayName);
            person.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await person.ExecuteNonQueryAsync();
        }

        using (SqliteCommand face = connection.CreateCommand())
        {
            face.Transaction = transaction;
            face.CommandText = "INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc) VALUES ($id, $revision, 0, $now);";
            face.Parameters.AddWithValue("$id", faceId.ToString());
            face.Parameters.AddWithValue("$revision", revisionId.ToString());
            face.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await face.ExecuteNonQueryAsync();
        }

        long labelId;
        using (SqliteCommand label = connection.CreateCommand())
        {
            label.Transaction = transaction;
            label.CommandText = """
                INSERT INTO person_labels (person_id, face_occurrence_id, label_kind, assigned_by, assigned_at_utc)
                VALUES ($person, $face, 'manual', 'test', $now);
                SELECT last_insert_rowid();
                """;
            label.Parameters.AddWithValue("$person", personId.ToString());
            label.Parameters.AddWithValue("$face", faceId.ToString());
            label.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            labelId = (long)(await label.ExecuteScalarAsync() ?? throw new InvalidOperationException());
        }

        using (SqliteCommand action = connection.CreateCommand())
        {
            action.Transaction = transaction;
            action.CommandText = """
                INSERT INTO review_actions (
                    face_occurrence_id, action_kind, person_id, person_label_id, actor, created_at_utc)
                VALUES ($face, 'assign', $person, $label, 'test', $now);
                """;
            action.Parameters.AddWithValue("$face", faceId.ToString());
            action.Parameters.AddWithValue("$person", personId.ToString());
            action.Parameters.AddWithValue("$label", labelId);
            action.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await action.ExecuteNonQueryAsync();
        }

        transaction.Commit();
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
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
