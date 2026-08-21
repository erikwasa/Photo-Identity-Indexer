using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PersonPhotoCountRepositoryTests
{
    [Fact]
    public async Task Distinct_photo_count_combines_face_evidence_and_manual_presence_without_double_counting()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
            CatalogueSource source = new(SourceId.New(), "local-folder", directory, now);

            CatalogueAssetRevision first = await SaveRevisionAsync(database, source, "first.jpg", now);
            CatalogueAssetRevision second = await SaveRevisionAsync(database, source, "second.jpg", now.AddMinutes(1));
            IReadOnlyList<FaceOccurrenceId> faces = await AddFacesAsync(database, first.Id, count: 2, now);

            SqliteReviewRepository review = new(database);
            CatalogueReviewPerson person = await review.CreatePersonAsync("Ada", now);
            await review.AssignAsync(faces[0], person.Id, "human:test", now.AddMinutes(2));
            await review.AssignAsync(faces[1], person.Id, "human:test", now.AddMinutes(3));

            SqlitePhotoPersonRepository manual = new(database, TimeProvider.System);
            await manual.AddManualPersonAsync(first.Id, person.Id, "human:test");
            await manual.AddManualPersonAsync(second.Id, person.Id, "human:test");

            SqlitePersonPhotoCountRepository countsRepository = new(database);
            IReadOnlyDictionary<PersonId, int> counts = await countsRepository.GetActivePhotoCountsAsync();

            Assert.Equal(2, counts[person.Id]);

            await manual.RemoveManualPersonAsync(second.Id, person.Id, "human:test");
            counts = await countsRepository.GetActivePhotoCountsAsync();

            Assert.Equal(1, counts[person.Id]);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<CatalogueAssetRevision> SaveRevisionAsync(
        SqliteCatalogueDatabase database,
        CatalogueSource source,
        string sourceKey,
        DateTimeOffset observedAtUtc)
    {
        byte[] content = System.Text.Encoding.UTF8.GetBytes(sourceKey);
        CatalogueAsset asset = new(AssetId.New(), source.Id, sourceKey, observedAtUtc);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()),
            content.LongLength,
            observedAtUtc,
            "image/jpeg",
            100,
            100);
        return await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(source, asset, revision);
    }

    private static async Task<IReadOnlyList<FaceOccurrenceId>> AddFacesAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        int count,
        DateTimeOffset now)
    {
        List<FaceOccurrenceId> ids = [];
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            FaceOccurrenceId faceId = FaceOccurrenceId.New();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($id, $revision_id, $ordinal, $created_at_utc);
                """;
            command.Parameters.AddWithValue("$id", faceId.ToString());
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            command.Parameters.AddWithValue("$ordinal", ordinal);
            command.Parameters.AddWithValue("$created_at_utc", now.AddSeconds(ordinal).ToString("O"));
            await command.ExecuteNonQueryAsync();
            ids.Add(faceId);
        }

        return ids;
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
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
