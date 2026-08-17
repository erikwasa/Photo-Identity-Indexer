using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionPlaceApplicationTests
{
    [Fact]
    public async Task Named_place_filter_matches_canonical_node_and_descendants_not_duplicate_leaf_names()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            AssetRevisionId stockholm = await CreateRevisionAsync(database, directory, "stockholm.jpg", 'a');
            AssetRevisionId visby = await CreateRevisionAsync(database, directory, "visby.jpg", 'b');
            AssetRevisionId otherStockholm = await CreateRevisionAsync(database, directory, "other-stockholm.jpg", 'c');

            SqlitePhotoPlaceRepository places = new(database, TimeProvider.System);
            await places.SetManualPlaceAsync(stockholm, "Sweden/Stockholm", "test");
            await places.SetManualPlaceAsync(visby, "Sweden/Gotland/Visby", "test");
            await places.SetManualPlaceAsync(otherStockholm, "United States/Maine/Stockholm", "test");

            SqliteSmartCollectionQueryRepository query = new(database);
            SmartCollectionPhotoPage sweden = await query.QueryAsync(new SmartCollectionFilter(
                location: new SmartCollectionLocation("Sweden")));
            Assert.Equal(2, sweden.Total);
            Assert.Contains(sweden.Items, item => item.RevisionId == stockholm);
            Assert.Contains(sweden.Items, item => item.RevisionId == visby);
            Assert.DoesNotContain(sweden.Items, item => item.RevisionId == otherStockholm);

            SmartCollectionPhotoPage exactHierarchy = await query.QueryAsync(new SmartCollectionFilter(
                location: new SmartCollectionLocation("Sweden/Stockholm")));
            Assert.Equal(stockholm, Assert.Single(exactHierarchy.Items).RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Named_place_and_gps_bounds_are_both_required_when_combined()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            AssetRevisionId inside = await CreateRevisionAsync(database, directory, "inside.jpg", 'd');
            AssetRevisionId outside = await CreateRevisionAsync(database, directory, "outside.jpg", 'e');

            SqlitePhotoPlaceRepository places = new(database, TimeProvider.System);
            await places.SetManualPlaceAsync(inside, "Sweden/Stockholm", "test");
            await places.SetManualPlaceAsync(outside, "Sweden/Stockholm", "test");
            await SetCoordinatesAsync(database, inside, 59.33, 18.07);
            await SetCoordinatesAsync(database, outside, 65.58, 22.15);

            SmartCollectionFilter filter = new(
                tags: ["Places/Sweden", "Family"],
                location: new SmartCollectionLocation(
                    "Sweden",
                    new SmartCollectionGeoBounds(58, 17, 60, 19)));
            Assert.Single(filter.Tags);
            Assert.Equal("family", filter.Tags[0]);

            SmartCollectionPhotoPage page = await new SqliteSmartCollectionQueryRepository(database).QueryAsync(filter);
            Assert.Equal(inside, Assert.Single(page.Items).RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Saved_filter_v1_remains_readable_and_new_named_place_definition_uses_v2()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            string id = Guid.NewGuid().ToString("D");
            string now = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    CREATE TABLE smart_collections (
                        id TEXT NOT NULL PRIMARY KEY,
                        normalized_name TEXT NOT NULL UNIQUE,
                        display_name TEXT NOT NULL,
                        filter_schema_version INTEGER NOT NULL CHECK (filter_schema_version = 1),
                        filter_json TEXT NOT NULL,
                        created_at_utc TEXT NOT NULL,
                        updated_at_utc TEXT NOT NULL,
                        CHECK (length(normalized_name) BETWEEN 1 AND 120),
                        CHECK (length(display_name) BETWEEN 1 AND 120),
                        CHECK (length(filter_json) > 0));
                    INSERT INTO smart_collections (
                        id, normalized_name, display_name, filter_schema_version, filter_json, created_at_utc, updated_at_utc)
                    VALUES ($id, 'legacy', 'Legacy', 1,
                        '{"people":[],"peopleMatch":"all","tags":[],"tagMatch":"all","location":{"south":58,"west":17,"north":60,"east":19},"taken":null}',
                        $now, $now);
                    """;
                seed.Parameters.AddWithValue("$id", id);
                seed.Parameters.AddWithValue("$now", now);
                await seed.ExecuteNonQueryAsync();
            }

            SqliteSmartCollectionRepository repository = new(database, TimeProvider.System);
            SmartCollectionDefinition legacy = Assert.Single(await repository.ListAsync());
            Assert.NotNull(legacy.Filter.Location?.Bounds);
            Assert.Null(legacy.Filter.Location?.Place);
            Assert.Equal(58, legacy.Filter.Location!.Bounds!.South);

            SmartCollectionDefinition created = await repository.CreateAsync(
                "Sweden",
                new SmartCollectionFilter(location: new SmartCollectionLocation("Sweden")));
            Assert.Equal("places/sweden", created.Filter.Location?.Place);

            await using SqliteConnection verify = await database.OpenConnectionAsync();
            using SqliteCommand versions = verify.CreateCommand();
            versions.CommandText = "SELECT GROUP_CONCAT(filter_schema_version, ',') FROM smart_collections ORDER BY normalized_name;";
            string stored = Assert.IsType<string>(await versions.ExecuteScalarAsync());
            Assert.Contains('1', stored);
            Assert.Contains('2', stored);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<AssetRevisionId> CreateRevisionAsync(SqliteCatalogueDatabase database, string sourceRoot, string sourceKey, char hashCharacter)
    {
        DateTimeOffset now = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, sourceKey, now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(), asset.Id, new Sha256Digest(new string(hashCharacter, 64)), 123,
            now, "image/jpeg", 100, 100);
        return (await new SqliteAssetCatalogueRepository(database).SaveRevisionAsync(source, asset, revision)).Id;
    }

    private static async Task SetCoordinatesAsync(SqliteCatalogueDatabase database, AssetRevisionId revisionId, double latitude, double longitude)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_capture_metadata (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                taken_at_local TEXT NULL,
                utc_offset_minutes INTEGER NULL,
                latitude REAL NULL,
                longitude REAL NULL,
                extracted_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE);
            INSERT INTO photo_capture_metadata (
                asset_revision_id, taken_at_local, utc_offset_minutes, latitude, longitude, extracted_at_utc)
            VALUES ($revision_id, NULL, NULL, $latitude, $longitude, $now);
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$latitude", latitude);
        command.Parameters.AddWithValue("$longitude", longitude);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PhotoIdentity.Integration.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
