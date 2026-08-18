using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PersonFeaturedFaceMergeTests
{
    [Fact]
    public async Task Merge_keeps_the_survivors_explicit_featured_face()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteReviewRepository review = new(database);
            SqlitePersonFeaturedFaceRepository featured = new(database);
            SqlitePersonMaintenanceRepository maintenance = new(database);
            DateTimeOffset now = new(2026, 8, 18, 21, 0, 0, TimeSpan.Zero);

            CatalogueReviewPerson source = await review.CreatePersonAsync("Alice duplicate", now);
            CatalogueReviewPerson target = await review.CreatePersonAsync("Alice", now.AddSeconds(1));
            FaceOccurrenceId sourceFace = await CreateAssignedFaceAsync(
                database, review, directory, source.Id, "source.jpg", 'a', now.AddMinutes(1));
            FaceOccurrenceId targetFace = await CreateAssignedFaceAsync(
                database, review, directory, target.Id, "target.jpg", 'b', now.AddMinutes(2));

            await featured.SetFeaturedFaceAsync(source.Id, sourceFace, now.AddMinutes(3));
            await featured.SetFeaturedFaceAsync(target.Id, targetFace, now.AddMinutes(4));

            await maintenance.MergeAsync(
                source.Id,
                target.Id,
                confirmIrreversible: true,
                actor: "test",
                createdAtUtc: now.AddMinutes(5));

            CataloguePersonRepresentativeFace? representative = await featured.ResolveAsync(target.Id);
            Assert.NotNull(representative);
            Assert.Equal(targetFace, representative.FaceId);
            Assert.True(representative.IsExplicit);
            Assert.Equal(0, await CountFeaturedRowsAsync(database, source.Id));
            Assert.Equal(1, await CountFeaturedRowsAsync(database, target.Id));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Merge_carries_a_valid_source_featured_face_when_the_survivor_has_no_explicit_choice()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteReviewRepository review = new(database);
            SqlitePersonFeaturedFaceRepository featured = new(database);
            SqlitePersonMaintenanceRepository maintenance = new(database);
            DateTimeOffset now = new(2026, 8, 18, 21, 30, 0, TimeSpan.Zero);

            CatalogueReviewPerson source = await review.CreatePersonAsync("Bob duplicate", now);
            CatalogueReviewPerson target = await review.CreatePersonAsync("Bob", now.AddSeconds(1));
            FaceOccurrenceId sourceFace = await CreateAssignedFaceAsync(
                database, review, directory, source.Id, "source.jpg", 'c', now.AddMinutes(1));
            await CreateAssignedFaceAsync(
                database, review, directory, target.Id, "target.jpg", 'd', now.AddMinutes(2));

            await featured.SetFeaturedFaceAsync(source.Id, sourceFace, now.AddMinutes(3));

            await maintenance.MergeAsync(
                source.Id,
                target.Id,
                confirmIrreversible: true,
                actor: "test",
                createdAtUtc: now.AddMinutes(4));

            CataloguePersonRepresentativeFace? representative = await featured.ResolveAsync(target.Id);
            Assert.NotNull(representative);
            Assert.Equal(sourceFace, representative.FaceId);
            Assert.True(representative.IsExplicit);
            Assert.Equal(0, await CountFeaturedRowsAsync(database, source.Id));
            Assert.Equal(1, await CountFeaturedRowsAsync(database, target.Id));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Merge_discards_a_stale_source_featured_face_and_uses_the_survivors_automatic_fallback()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteReviewRepository review = new(database);
            SqlitePersonFeaturedFaceRepository featured = new(database);
            SqlitePersonMaintenanceRepository maintenance = new(database);
            DateTimeOffset now = new(2026, 8, 18, 22, 0, 0, TimeSpan.Zero);

            CatalogueReviewPerson source = await review.CreatePersonAsync("Carol duplicate", now);
            CatalogueReviewPerson target = await review.CreatePersonAsync("Carol", now.AddSeconds(1));
            CatalogueReviewPerson other = await review.CreatePersonAsync("Other", now.AddSeconds(2));
            FaceOccurrenceId staleFace = await CreateAssignedFaceAsync(
                database, review, directory, source.Id, "stale.jpg", 'e', now.AddMinutes(1));
            FaceOccurrenceId targetFallback = await CreateAssignedFaceAsync(
                database, review, directory, target.Id, "target.jpg", 'f', now.AddMinutes(2));

            await featured.SetFeaturedFaceAsync(source.Id, staleFace, now.AddMinutes(3));
            await review.AssignAsync(staleFace, other.Id, "test", now.AddMinutes(4));

            await maintenance.MergeAsync(
                source.Id,
                target.Id,
                confirmIrreversible: true,
                actor: "test",
                createdAtUtc: now.AddMinutes(5));

            CataloguePersonRepresentativeFace? representative = await featured.ResolveAsync(target.Id);
            Assert.NotNull(representative);
            Assert.Equal(targetFallback, representative.FaceId);
            Assert.False(representative.IsExplicit);
            Assert.Equal(0, await CountFeaturedRowsAsync(database, source.Id));
            Assert.Equal(0, await CountFeaturedRowsAsync(database, target.Id));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<FaceOccurrenceId> CreateAssignedFaceAsync(
        SqliteCatalogueDatabase database,
        SqliteReviewRepository review,
        string root,
        PersonId personId,
        string sourceKey,
        char hashCharacter,
        DateTimeOffset createdAtUtc)
    {
        SqliteAssetCatalogueRepository catalogue = new(database);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        string sourceRoot = Path.Combine(root, assetId.ToString());
        Directory.CreateDirectory(sourceRoot);
        CatalogueAssetRevision revision = await catalogue.SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, createdAtUtc),
            new CatalogueAsset(assetId, sourceId, sourceKey, createdAtUtc),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(new string(hashCharacter, 64)),
                100,
                createdAtUtc,
                "image/jpeg",
                100,
                100));

        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($id, $revision_id, 0, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$id", faceId.ToString());
        command.Parameters.AddWithValue("$revision_id", revision.Id.ToString());
        command.Parameters.AddWithValue("$created_at_utc", createdAtUtc.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync();

        await review.AssignAsync(faceId, personId, "test", createdAtUtc.AddSeconds(1));
        return faceId;
    }

    private static async Task<long> CountFeaturedRowsAsync(
        SqliteCatalogueDatabase database,
        PersonId personId)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM person_featured_faces WHERE person_id = $person_id;";
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
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
