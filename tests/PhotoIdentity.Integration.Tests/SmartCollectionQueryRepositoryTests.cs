using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionQueryRepositoryTests
{
    [Fact]
    public async Task Query_combines_people_tags_location_and_taken_date_with_and_semantics()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionQueryRepository query = new(database);

            PersonId alice = PersonId.New();
            PersonId bob = PersonId.New();
            CatalogueAssetRevision matching = await CreateRevisionAsync(catalogue, directory, "matching.jpg", 'a');
            CatalogueAssetRevision wrongLocation = await CreateRevisionAsync(catalogue, directory, "wrong-location.jpg", 'b');
            CatalogueAssetRevision missingMetadata = await CreateRevisionAsync(catalogue, directory, "missing-metadata.jpg", 'c');

            await AssignPersonAsync(database, matching.Id, alice, "Alice");
            await AssignPersonAsync(database, matching.Id, bob, "Bob");
            await AssignPersonAsync(database, wrongLocation.Id, alice, "Alice");
            await AssignPersonAsync(database, wrongLocation.Id, bob, "Bob");
            await AssignPersonAsync(database, missingMetadata.Id, alice, "Alice");
            await AssignPersonAsync(database, missingMetadata.Id, bob, "Bob");

            await tags.AddManualTagAsync(matching.Id, "Trips/Italy", "test");
            await tags.AddManualTagAsync(matching.Id, "Family", "test");
            await tags.AddManualTagAsync(wrongLocation.Id, "Trips/Italy", "test");
            await tags.AddManualTagAsync(wrongLocation.Id, "Family", "test");
            await tags.AddManualTagAsync(missingMetadata.Id, "Trips/Italy", "test");
            await tags.AddManualTagAsync(missingMetadata.Id, "Family", "test");

            await catalogue.SavePhotoMetadataAsync(
                matching.Id,
                new PhotoIdentity.Core.Sources.PhotoCaptureMetadata(
                    new DateTime(2025, 5, 5, 14, 30, 0, DateTimeKind.Unspecified),
                    null,
                    41.9028,
                    12.4964),
                DateTimeOffset.UtcNow);
            await catalogue.SavePhotoMetadataAsync(
                wrongLocation.Id,
                new PhotoIdentity.Core.Sources.PhotoCaptureMetadata(
                    new DateTime(2025, 5, 5, 14, 30, 0, DateTimeKind.Unspecified),
                    null,
                    59.3293,
                    18.0686),
                DateTimeOffset.UtcNow);

            SmartCollectionFilter filter = new(
                people: [alice, bob],
                peopleMatch: SmartCollectionMatchModes.All,
                tags: ["Trips/Italy", "Family"],
                tagMatch: SmartCollectionMatchModes.All,
                location: new SmartCollectionGeoBounds(40, 10, 44, 15),
                taken: SmartCollectionDateRange.Parse("2025/05/01-2025/05/10"));

            SmartCollectionPhotoPage result = await query.QueryAsync(filter);

            SmartCollectionPhoto item = Assert.Single(result.Items);
            Assert.Equal(matching.Id, item.RevisionId);
            Assert.Equal(1, result.Total);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Any_matching_accepts_one_requested_person_and_one_requested_tag()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionQueryRepository query = new(database);
            PersonId alice = PersonId.New();
            PersonId bob = PersonId.New();
            CatalogueAssetRevision aliceTrip = await CreateRevisionAsync(catalogue, directory, "alice-trip.jpg", 'f');
            CatalogueAssetRevision bobFamily = await CreateRevisionAsync(catalogue, directory, "bob-family.jpg", '1');

            await AssignPersonAsync(database, aliceTrip.Id, alice, "Alice");
            await AssignPersonAsync(database, bobFamily.Id, bob, "Bob");
            await tags.AddManualTagAsync(aliceTrip.Id, "Trips/Italy", "test");
            await tags.AddManualTagAsync(bobFamily.Id, "Family", "test");

            SmartCollectionPhotoPage result = await query.QueryAsync(new SmartCollectionFilter(
                people: [alice, bob],
                peopleMatch: SmartCollectionMatchModes.Any,
                tags: ["Trips/Italy", "Family"],
                tagMatch: SmartCollectionMatchModes.Any));

            Assert.Equal(2, result.Total);
            AssetRevisionId[] revisionIds = result.Items.Select(item => item.RevisionId).ToArray();
            Assert.Contains(aliceTrip.Id, revisionIds);
            Assert.Contains(bobFamily.Id, revisionIds);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Empty_people_dimension_allows_tag_only_collection()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqliteSmartCollectionQueryRepository query = new(database);
            CatalogueAssetRevision tagged = await CreateRevisionAsync(catalogue, directory, "tagged.jpg", 'd');
            _ = await CreateRevisionAsync(catalogue, directory, "untagged.jpg", 'e');
            await tags.AddManualTagAsync(tagged.Id, "Places/Sweden/Stockholm", "test");

            SmartCollectionPhotoPage result = await query.QueryAsync(
                new SmartCollectionFilter(tags: ["places/sweden/stockholm"]));

            Assert.Equal(tagged.Id, Assert.Single(result.Items).RevisionId);
            Assert.Equal(1, result.Total);
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
        DateTimeOffset now = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        CatalogueSource source = new(sourceId, "local-folder", root, now);
        CatalogueAsset asset = new(assetId, sourceId, sourceKey, now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            assetId,
            new Sha256Digest(new string(hashCharacter, 64)),
            100,
            now,
            "image/jpeg",
            100,
            100);
        return await catalogue.SaveRevisionAsync(source, asset, revision);
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
            person.CommandText = "INSERT OR IGNORE INTO people (id, display_name, created_at_utc) VALUES ($id, $name, $now);";
            person.Parameters.AddWithValue("$id", personId.ToString());
            person.Parameters.AddWithValue("$name", displayName);
            person.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await person.ExecuteNonQueryAsync();
        }

        long ordinal;
        using (SqliteCommand nextOrdinal = connection.CreateCommand())
        {
            nextOrdinal.Transaction = transaction;
            nextOrdinal.CommandText = "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM face_occurrences WHERE asset_revision_id = $revision;";
            nextOrdinal.Parameters.AddWithValue("$revision", revisionId.ToString());
            ordinal = (long)(await nextOrdinal.ExecuteScalarAsync() ?? 0L);
        }

        using (SqliteCommand face = connection.CreateCommand())
        {
            face.Transaction = transaction;
            face.CommandText = "INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc) VALUES ($id, $revision, $ordinal, $now);";
            face.Parameters.AddWithValue("$id", faceId.ToString());
            face.Parameters.AddWithValue("$revision", revisionId.ToString());
            face.Parameters.AddWithValue("$ordinal", ordinal);
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
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
