using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class ExactDuplicateRepositoryTests
{
    [Fact]
    public async Task Inventory_groups_only_verified_current_revisions_without_merging_source_copies()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-duplicates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "catalogue.db");

        try
        {
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            SourceId sourceId = SourceId.New();
            DateTimeOffset observed = new(2026, 9, 25, 18, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(sourceId, "local-folder", directory, observed);
            SqliteArchiveSourceObservationRepository observations = new(database);
            Sha256Digest duplicateHash = new(new string('a', 64));
            Sha256Digest otherHash = new(new string('b', 64));
            Sha256Digest changedHash = new(new string('c', 64));

            ArchiveSourceObservationWriteResult first = await observations.RecordScanObservationAsync(
                source,
                Asset(sourceId, "one/photo.jpg", observed, AssetAvailability.Local),
                duplicateHash,
                observed);
            ArchiveSourceObservationWriteResult second = await observations.RecordScanObservationAsync(
                source,
                Asset(sourceId, "two/copy.jpg", observed, AssetAvailability.Local),
                duplicateHash,
                observed);
            ArchiveSourceObservationWriteResult third = await observations.RecordScanObservationAsync(
                source,
                Asset(sourceId, "three/other.jpg", observed, AssetAvailability.Local),
                otherHash,
                observed);
            await observations.RecordScanObservationAsync(
                source,
                Asset(sourceId, "four/unverified.jpg", observed, AssetAvailability.OnlineOnly),
                verifiedContentHash: null,
                observed);

            Assert.NotEqual(first.AssetId, second.AssetId);
            Assert.NotEqual(first.RevisionId, second.RevisionId);
            Assert.NotEqual(second.AssetId, third.AssetId);

            AssetRevisionId firstRevisionId = first.RevisionId
                ?? throw new InvalidOperationException("Verified observation did not create a revision.");
            FaceOccurrenceId reviewedFaceId = await SeedFaceAsync(database, firstRevisionId, observed);
            SqliteReviewRepository reviewRepository = new(database);
            CatalogueReviewPerson reviewedPerson = await reviewRepository.CreatePersonAsync("Ada", observed);
            await reviewRepository.AssignAsync(
                reviewedFaceId,
                reviewedPerson.Id,
                "human:test",
                observed.AddSeconds(1));

            int facesBeforeRead = await CountAsync(database, "face_occurrences");
            int peopleBeforeRead = await CountAsync(database, "people");
            int labelsBeforeRead = await CountAsync(database, "person_labels");
            int reviewActionsBeforeRead = await CountAsync(database, "review_actions");

            SqliteExactDuplicateRepository repository = new(database);
            IReadOnlyList<ExactDuplicateGroup> groups = await repository.GetGroupsAsync(sourceId);

            ExactDuplicateGroup group = Assert.Single(groups);
            Assert.Equal(duplicateHash, group.ContentHash);
            Assert.Equal(2, group.Copies.Count);
            Assert.Contains(group.Copies, copy => copy.AssetId == first.AssetId && copy.RevisionId == first.RevisionId);
            Assert.Contains(group.Copies, copy => copy.AssetId == second.AssetId && copy.RevisionId == second.RevisionId);
            Assert.DoesNotContain(group.Copies, copy => copy.AssetId == third.AssetId);

            int assetsBeforeRead = await CountAsync(database, "assets");
            int revisionsBeforeRead = await CountAsync(database, "asset_revisions");
            _ = await repository.GetGroupsAsync(sourceId);
            Assert.Equal(assetsBeforeRead, await CountAsync(database, "assets"));
            Assert.Equal(revisionsBeforeRead, await CountAsync(database, "asset_revisions"));
            Assert.Equal(facesBeforeRead, await CountAsync(database, "face_occurrences"));
            Assert.Equal(peopleBeforeRead, await CountAsync(database, "people"));
            Assert.Equal(labelsBeforeRead, await CountAsync(database, "person_labels"));
            Assert.Equal(reviewActionsBeforeRead, await CountAsync(database, "review_actions"));
            CatalogueReviewFace reviewedFace = Assert.IsType<CatalogueReviewFace>(
                await reviewRepository.GetFaceAsync(reviewedFaceId));
            Assert.Equal(CatalogueReviewStates.Assigned, reviewedFace.State);
            Assert.Equal(reviewedPerson, reviewedFace.Person);
            await AssertContentHashIndexIsNonUniqueAsync(database);

            await observations.RecordScanObservationAsync(
                source,
                Asset(sourceId, "two/copy.jpg", observed, AssetAvailability.OnlineOnly),
                verifiedContentHash: null,
                observed.AddMinutes(1));
            groups = await repository.GetGroupsAsync(sourceId);
            Assert.Single(groups);

            await MarkMissingAsync(database, second.AssetId, observed.AddMinutes(2));
            groups = await repository.GetGroupsAsync(sourceId);
            group = Assert.Single(groups);
            Assert.Contains(group.Copies, copy => copy.AssetId == second.AssetId && copy.IsMissing);

            ArchiveSourceObservationWriteResult changed = await observations.RecordScanObservationAsync(
                source,
                Asset(sourceId, "one/photo.jpg", observed.AddMinutes(3), AssetAvailability.Local, sizeBytes: 124),
                changedHash,
                observed.AddMinutes(3));
            Assert.True(changed.NewRevision);
            Assert.NotEqual(first.RevisionId, changed.RevisionId);

            groups = await repository.GetGroupsAsync(sourceId);
            Assert.Empty(groups);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SourceAsset Asset(
        SourceId sourceId,
        string key,
        DateTimeOffset lastWrite,
        AssetAvailability availability,
        long sizeBytes = 123) =>
        new(
            new SourceAssetReference(sourceId, key),
            key,
            "image/jpeg",
            sizeBytes,
            lastWrite,
            availability);

    private static async Task<int> CountAsync(SqliteCatalogueDatabase database, string table)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<FaceOccurrenceId> SeedFaceAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        DateTimeOffset createdAt)
    {
        FaceOccurrenceId faceId = FaceOccurrenceId.New();
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($face_id, $revision_id, 0, $created_at_utc);
            """;
        command.Parameters.AddWithValue("$face_id", faceId.ToString());
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$created_at_utc", createdAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync();
        return faceId;
    }

    private static async Task MarkMissingAsync(
        SqliteCatalogueDatabase database,
        AssetId assetId,
        DateTimeOffset deletedAt)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE assets
            SET deleted_at_utc = $deleted_at_utc
            WHERE id = $asset_id;
            """;
        command.Parameters.AddWithValue("$deleted_at_utc", deletedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertContentHashIndexIsNonUniqueAsync(SqliteCatalogueDatabase database)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA index_list('asset_revisions');";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!string.Equals(reader.GetString(1), "ix_asset_revisions_content_sha256", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Equal(0L, reader.GetInt64(2));
            return;
        }

        Assert.Fail("Expected ix_asset_revisions_content_sha256 to exist.");
    }
}
