using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class LocalFolderCatalogueScannerTests
{
    [Fact]
    public async Task Repeated_scans_deduplicate_unchanged_files_and_create_changed_revisions()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceDirectory = Path.Combine(directory, "source");
            Directory.CreateDirectory(sourceDirectory);
            string photoPath = Path.Combine(sourceDirectory, "photo.jpg");
            await File.WriteAllBytesAsync(photoPath, [1, 2, 3]);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SourceId sourceId = SourceId.New();
            LocalFolderAssetSource source = new(sourceId, sourceDirectory);
            CatalogueSource catalogueSource = new(
                sourceId,
                "local-folder",
                sourceDirectory,
                Utc(10));
            SqliteSourceCatalogueScanner scanner = new(database);

            SourceCatalogueScanSummary first = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(),
                Utc(10));
            SourceCatalogueScanSummary repeated = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(),
                Utc(11));

            Assert.Equal(1, first.NewRevisionCount);
            Assert.Equal(0, repeated.NewRevisionCount);
            Assert.Equal(1, repeated.UnchangedFileCount);
            CatalogueAsset asset = Assert.Single(await scanner.GetAssetsAsync(sourceId));
            Assert.False(asset.IsDeleted);
            Assert.Equal(Utc(11), asset.LastSeenAtUtc);
            Assert.Single(await scanner.GetRevisionsAsync(asset.Id));

            await File.WriteAllBytesAsync(photoPath, [4, 5, 6, 7]);
            SourceCatalogueScanSummary changed = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(),
                Utc(12));

            Assert.Equal(1, changed.NewRevisionCount);
            IReadOnlyList<CatalogueAsset> assets = await scanner.GetAssetsAsync(sourceId);
            Assert.Equal(asset.Id, Assert.Single(assets).Id);
            Assert.Equal(2, (await scanner.GetRevisionsAsync(asset.Id)).Count);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Missing_files_are_marked_deleted_without_removing_labels_or_revisions()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceDirectory = Path.Combine(directory, "source");
            Directory.CreateDirectory(sourceDirectory);
            string photoPath = Path.Combine(sourceDirectory, "photo.png");
            await File.WriteAllBytesAsync(photoPath, [8, 9, 10]);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SourceId sourceId = SourceId.New();
            LocalFolderAssetSource source = new(sourceId, sourceDirectory);
            CatalogueSource catalogueSource = new(
                sourceId,
                "local-folder",
                sourceDirectory,
                Utc(10));
            SqliteSourceCatalogueScanner scanner = new(database);
            await scanner.ScanAsync(source, catalogueSource, new SourceScanOptions(), Utc(10));

            CatalogueAsset asset = Assert.Single(await scanner.GetAssetsAsync(sourceId));
            CatalogueAssetRevision revision = Assert.Single(await scanner.GetRevisionsAsync(asset.Id));
            await SeedHumanLabelAsync(database, revision.Id);

            File.Delete(photoPath);
            SourceCatalogueScanSummary deleted = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(),
                Utc(11));

            Assert.Equal(1, deleted.MarkedDeletedCount);
            CatalogueAsset persisted = Assert.Single(await scanner.GetAssetsAsync(sourceId));
            Assert.True(persisted.IsDeleted);
            Assert.Equal(Utc(11), persisted.DeletedAtUtc);
            Assert.Empty(await scanner.GetAssetsAsync(sourceId, includeDeleted: false));
            Assert.Single(await scanner.GetRevisionsAsync(asset.Id));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await CountAsync(connection, "person_labels"));
            Assert.Equal(1, await CountAsync(connection, "face_occurrences"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task SeedHumanLabelAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, 0, $now);
            INSERT INTO people (id, display_name, created_at_utc)
                VALUES ($person_id, 'Ada', $now);
            INSERT INTO person_labels (
                person_id,
                face_occurrence_id,
                label_kind,
                assigned_by,
                assigned_at_utc)
                VALUES ($person_id, $face_id, 'confirmed', 'human', $now);
            """;
        command.Parameters.AddWithValue("$face_id", FaceOccurrenceId.New().ToString());
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$person_id", PersonId.New().ToString());
        command.Parameters.AddWithValue("$now", Utc(10).ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 7, 26, hour, 0, 0, TimeSpan.Zero);

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
