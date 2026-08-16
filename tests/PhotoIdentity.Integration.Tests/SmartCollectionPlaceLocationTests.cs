using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionPlaceLocationTests
{
    [Fact]
    public async Task Named_place_matches_exact_canonical_ancestor_without_global_leaf_matching()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoPlaceRepository places = new(database, TimeProvider.System);
            SqliteSmartCollectionQueryRepository query = new(database);

            CatalogueAssetRevision stockholm = await CreateRevisionAsync(catalogue, directory, "stockholm.jpg", 'a');
            CatalogueAssetRevision norrtalje = await CreateRevisionAsync(catalogue, directory, "norrtalje.jpg", 'b');
            CatalogueAssetRevision illinoisSpringfield = await CreateRevisionAsync(catalogue, directory, "springfield-us.jpg", 'c');

            await places.SetManualPlaceAsync(stockholm.Id, "Sweden/Stockholm region/Stockholm", "test");
            await places.SetManualPlaceAsync(norrtalje.Id, "Sweden/Stockholm region/Norrtälje", "test");
            await places.SetManualPlaceAsync(illinoisSpringfield.Id, "USA/Illinois/Stockholm", "test");

            SmartCollectionPhotoPage sweden = await query.QueryAsync(
                new SmartCollectionFilter(locationPlace: "Sweden"));
            Assert.Equal(2, sweden.Total);
            Assert.Contains(sweden.Items, photo => photo.RevisionId == stockholm.Id);
            Assert.Contains(sweden.Items, photo => photo.RevisionId == norrtalje.Id);
            Assert.DoesNotContain(sweden.Items, photo => photo.RevisionId == illinoisSpringfield.Id);

            SmartCollectionPhotoPage region = await query.QueryAsync(
                new SmartCollectionFilter(locationPlace: "Sweden/Stockholm region"));
            Assert.Equal(2, region.Total);

            SmartCollectionPhotoPage exactLocality = await query.QueryAsync(
                new SmartCollectionFilter(locationPlace: "Sweden/Stockholm region/Stockholm"));
            Assert.Equal(stockholm.Id, Assert.Single(exactLocality.Items).RevisionId);

            SmartCollectionPhotoPage bareLeaf = await query.QueryAsync(
                new SmartCollectionFilter(locationPlace: "Stockholm"));
            Assert.Empty(bareLeaf.Items);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Named_place_and_gps_compose_with_people_tags_and_taken_date()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoPlaceRepository places = new(database, TimeProvider.System);
            SqlitePhotoTagRepository tags = new(database, TimeProvider.System);
            SqlitePhotoPersonRepository people = new(database, TimeProvider.System);
            SqliteSmartCollectionQueryRepository query = new(database);

            PersonId ada = PersonId.New();
            CatalogueAssetRevision matching = await CreateRevisionAsync(catalogue, directory, "matching.jpg", 'd');
            CatalogueAssetRevision wrongPlace = await CreateRevisionAsync(catalogue, directory, "wrong-place.jpg", 'e');
            CatalogueAssetRevision wrongGps = await CreateRevisionAsync(catalogue, directory, "wrong-gps.jpg", 'f');

            await SeedPersonAsync(database, ada, "Ada");
            foreach (CatalogueAssetRevision revision in new[] { matching, wrongPlace, wrongGps })
            {
                await people.AddManualPersonAsync(revision.Id, ada, "test");
                await tags.AddManualTagAsync(revision.Id, "Trips/Family", "test");
            }

            await places.SetManualPlaceAsync(matching.Id, "Sweden/Stockholm region/Norrtälje", "test");
            await places.SetManualPlaceAsync(wrongPlace.Id, "Finland/Uusimaa/Helsinki", "test");
            await places.SetManualPlaceAsync(wrongGps.Id, "Sweden/Stockholm region/Norrtälje", "test");

            await catalogue.SavePhotoMetadataAsync(
                matching.Id,
                new PhotoCaptureMetadata(new DateTime(2025, 7, 10, 12, 0, 0), null, 59.7580, 18.7050),
                DateTimeOffset.UtcNow);
            await catalogue.SavePhotoMetadataAsync(
                wrongPlace.Id,
                new PhotoCaptureMetadata(new DateTime(2025, 7, 10, 12, 0, 0), null, 59.7580, 18.7050),
                DateTimeOffset.UtcNow);
            await catalogue.SavePhotoMetadataAsync(
                wrongGps.Id,
                new PhotoCaptureMetadata(new DateTime(2025, 7, 10, 12, 0, 0), null, 60.1699, 24.9384),
                DateTimeOffset.UtcNow);

            SmartCollectionFilter filter = new(
                people: [ada],
                tags: ["Trips/Family"],
                location: new SmartCollectionGeoBounds(59.0, 17.0, 60.0, 19.0),
                taken: SmartCollectionDateRange.Parse("2025/07/01-2025/07/31"),
                locationPlace: "Sweden/Stockholm region");

            SmartCollectionPhotoPage result = await query.QueryAsync(filter);
            Assert.Equal(matching.Id, Assert.Single(result.Items).RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Schema_fourteen_promotes_v1_saved_definitions_and_preserves_legacy_places_filter()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            string gpsId = Guid.NewGuid().ToString("D");
            string placeId = Guid.NewGuid().ToString("D");
            string now = new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero).ToString("O");
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand rewind = connection.CreateCommand();
                rewind.CommandText = """
                    DROP INDEX IF EXISTS ix_smart_collections_name;
                    DROP TABLE smart_collections;
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
                    INSERT INTO smart_collections VALUES (
                        $gps_id, 'legacy gps', 'Legacy GPS', 1,
                        '{"people":[],"peopleMatch":"all","tags":[],"tagMatch":"all","location":{"south":59.0,"west":17.0,"north":60.0,"east":19.0},"taken":null}',
                        $now, $now);
                    INSERT INTO smart_collections VALUES (
                        $place_id, 'legacy place', 'Legacy Place', 1,
                        '{"people":[],"peopleMatch":"all","tags":["places/sweden/stockholm region"],"tagMatch":"all","location":null,"taken":null}',
                        $now, $now);
                    DELETE FROM schema_migrations WHERE version = 14;
                    PRAGMA user_version = 13;
                    """;
                rewind.Parameters.AddWithValue("$gps_id", gpsId);
                rewind.Parameters.AddWithValue("$place_id", placeId);
                rewind.Parameters.AddWithValue("$now", now);
                await rewind.ExecuteNonQueryAsync();
            }

            await database.InitializeAsync();

            await using (SqliteConnection verify = await database.OpenConnectionAsync())
            {
                Assert.Equal(14L, await ScalarLongAsync(verify, "PRAGMA user_version;"));
                Assert.Equal(2L, await ScalarLongAsync(
                    verify,
                    "SELECT COUNT(*) FROM smart_collections WHERE filter_schema_version = 2;"));
                Assert.Equal(1L, await ScalarLongAsync(
                    verify,
                    "SELECT COUNT(*) FROM schema_migrations WHERE version = 14;"));
                Assert.Equal(1L, await ScalarLongAsync(
                    verify,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'photo_capture_metadata';"));
                Assert.Equal(1L, await ScalarLongAsync(
                    verify,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'photo_person_actions';"));
                Assert.Equal(1L, await ScalarLongAsync(
                    verify,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'photo_place_actions';"));
            }

            SqliteSmartCollectionRepository repository = new(database, TimeProvider.System);
            SmartCollectionDefinition gps = await repository.GetAsync(SmartCollectionId.From(Guid.Parse(gpsId)))
                ?? throw new InvalidOperationException();
            Assert.NotNull(gps.Filter.Location);
            Assert.Null(gps.Filter.LocationPlace);
            Assert.Equal(59.0, gps.Filter.Location.South);

            SmartCollectionDefinition place = await repository.GetAsync(SmartCollectionId.From(Guid.Parse(placeId)))
                ?? throw new InvalidOperationException();
            Assert.Empty(place.Filter.Tags);
            Assert.Equal("places/sweden/stockholm region", place.Filter.LocationPlace);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Smart_collection_API_accepts_named_location_and_rejects_places_as_generic_tags()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SmartLocationApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            SmartCollectionDefinitionRequest request = new(
                "Stockholm",
                Location: new SmartCollectionLocationRequest(
                    South: 59,
                    West: 17,
                    North: 60,
                    East: 19,
                    Place: "Sweden/Stockholm region"));
            using HttpResponseMessage create = await client.PostAsJsonAsync("/api/smart-collections", request);
            create.EnsureSuccessStatusCode();
            SmartCollectionDefinitionResponse created =
                await create.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException();
            Assert.Equal("sweden/stockholm region", created.Filter.Location?.Place);
            Assert.Equal(59, created.Filter.Location?.South);

            using HttpResponseMessage rejected = await client.PostAsJsonAsync(
                "/api/smart-collections/query",
                new SmartCollectionQueryRequest(Tags: ["Places/Sweden"]));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
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
        DateTimeOffset now = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        SourceId sourceId = SourceId.New();
        AssetId assetId = AssetId.New();
        string sourceRoot = Path.Combine(root, "originals-do-not-exist", assetId.ToString());
        CatalogueAssetRevision revision = await catalogue.SaveRevisionAsync(
            new CatalogueSource(sourceId, "local-folder", sourceRoot, now),
            new CatalogueAsset(assetId, sourceId, sourceKey, now),
            new CatalogueAssetRevision(
                AssetRevisionId.New(),
                assetId,
                new Sha256Digest(new string(hashCharacter, 64)),
                100,
                now,
                "image/jpeg",
                1000,
                800));
        Assert.False(Directory.Exists(sourceRoot));
        return revision;
    }

    private static async Task SeedPersonAsync(
        SqliteCatalogueDatabase database,
        PersonId personId,
        string displayName)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
            VALUES ($id, $name, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", personId.ToString());
        command.Parameters.AddWithValue("$name", displayName);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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

    private sealed class SmartLocationApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public SmartLocationApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
