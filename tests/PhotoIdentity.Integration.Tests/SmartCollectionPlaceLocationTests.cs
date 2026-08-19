using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionPlaceLocationTests
{
    [Fact]
    public async Task Location_place_matches_any_ancestor_or_leaf_component()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoPlaceRepository places = new(database);
            SqliteSmartCollectionRepository smartCollections = new(database, TimeProvider.System);

            CatalogueAssetRevision stockholm = await CreateRevisionAsync(
                catalogue,
                directory,
                "stockholm.jpg",
                'a');
            await places.UpsertAsync(
                stockholm.Id,
                new PhotoPlaceTag("Places/Sweden/Stockholm region/Stockholm"),
                DateTimeOffset.UtcNow);

            CatalogueAssetRevision gothenburg = await CreateRevisionAsync(
                catalogue,
                directory,
                "gothenburg.jpg",
                'b');
            await places.UpsertAsync(
                gothenburg.Id,
                new PhotoPlaceTag("Places/Sweden/Västra Götaland/Gothenburg"),
                DateTimeOffset.UtcNow);

            CatalogueAssetRevision oslo = await CreateRevisionAsync(
                catalogue,
                directory,
                "oslo.jpg",
                'c');
            await places.UpsertAsync(
                oslo.Id,
                new PhotoPlaceTag("Places/Norway/Oslo/Oslo"),
                DateTimeOffset.UtcNow);

            SmartCollectionDefinition sweden = await smartCollections.CreateAsync(
                "Sweden",
                new SmartCollectionFilter(
                    LocationPlace: "Sweden"),
                "test");
            SmartCollectionDefinition stockholmRegion = await smartCollections.CreateAsync(
                "Stockholm region",
                new SmartCollectionFilter(
                    LocationPlace: "Sweden/Stockholm region"),
                "test");
            SmartCollectionDefinition stockholmCity = await smartCollections.CreateAsync(
                "Stockholm city",
                new SmartCollectionFilter(
                    LocationPlace: "Stockholm"),
                "test");
            SmartCollectionDefinition exactStockholm = await smartCollections.CreateAsync(
                "Exact Stockholm",
                new SmartCollectionFilter(
                    LocationPlace: "Sweden/Stockholm region/Stockholm"),
                "test");

            IReadOnlyList<CatalogueSmartCollectionPhoto> country =
                await smartCollections.QueryPhotosAsync(sweden.Id);
            Assert.Equal(2, country.Count);
            Assert.Contains(country, photo => photo.RevisionId == stockholm.Id);
            Assert.Contains(country, photo => photo.RevisionId == gothenburg.Id);

            IReadOnlyList<CatalogueSmartCollectionPhoto> region =
                await smartCollections.QueryPhotosAsync(stockholmRegion.Id);
            Assert.Single(region);
            Assert.Equal(stockholm.Id, region[0].RevisionId);

            IReadOnlyList<CatalogueSmartCollectionPhoto> leaf =
                await smartCollections.QueryPhotosAsync(stockholmCity.Id);
            Assert.Single(leaf);
            Assert.Equal(stockholm.Id, leaf[0].RevisionId);

            IReadOnlyList<CatalogueSmartCollectionPhoto> exact =
                await smartCollections.QueryPhotosAsync(exactStockholm.Id);
            Assert.Single(exact);
            Assert.Equal(stockholm.Id, exact[0].RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Location_place_is_combined_with_other_smart_collection_filters()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteAssetCatalogueRepository catalogue = new(database);
            SqlitePhotoPlaceRepository places = new(database);
            SqliteReviewRepository reviews = new(database);
            SqliteSmartCollectionRepository smartCollections = new(database, TimeProvider.System);
            DateTimeOffset now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

            CatalogueReviewPerson alice = await reviews.CreatePersonAsync("Alice", now);
            CatalogueReviewPerson bob = await reviews.CreatePersonAsync("Bob", now.AddSeconds(1));

            CatalogueAssetRevision stockholm = await CreateRevisionAsync(
                catalogue,
                directory,
                "stockholm-person.jpg",
                'd');
            await places.UpsertAsync(
                stockholm.Id,
                new PhotoPlaceTag("Places/Sweden/Stockholm region/Stockholm"),
                now);
            FaceOccurrenceId stockholmFace = FaceOccurrenceId.New();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES ($id, $revision_id, 0, $now);
                    """;
                command.Parameters.AddWithValue("$id", stockholmFace.ToString());
                command.Parameters.AddWithValue("$revision_id", stockholm.Id.ToString());
                command.Parameters.AddWithValue("$now", now.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }
            await reviews.AssignAsync(stockholmFace, alice.Id, "test", now.AddMinutes(1));

            CatalogueAssetRevision gothenburg = await CreateRevisionAsync(
                catalogue,
                directory,
                "gothenburg-person.jpg",
                'e');
            await places.UpsertAsync(
                gothenburg.Id,
                new PhotoPlaceTag("Places/Sweden/Västra Götaland/Gothenburg"),
                now);
            FaceOccurrenceId gothenburgFace = FaceOccurrenceId.New();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    VALUES ($id, $revision_id, 0, $now);
                    """;
                command.Parameters.AddWithValue("$id", gothenburgFace.ToString());
                command.Parameters.AddWithValue("$revision_id", gothenburg.Id.ToString());
                command.Parameters.AddWithValue("$now", now.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }
            await reviews.AssignAsync(gothenburgFace, bob.Id, "test", now.AddMinutes(1));

            SmartCollectionDefinition collection = await smartCollections.CreateAsync(
                "Alice in Sweden",
                new SmartCollectionFilter(
                    People: [alice.Id],
                    LocationPlace: "Sweden"),
                "test");

            IReadOnlyList<CatalogueSmartCollectionPhoto> result =
                await smartCollections.QueryPhotosAsync(collection.Id);
            CatalogueSmartCollectionPhoto photo = Assert.Single(result);
            Assert.Equal(stockholm.Id, photo.RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Persistence_normalizes_place_and_keeps_it_separate_from_GPS_bounds()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteSmartCollectionRepository repository = new(database, TimeProvider.System);

            string gpsId = Guid.NewGuid().ToString("D");
            string placeId = Guid.NewGuid().ToString("D");
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO smart_collections (
                        id, name, filter_json, created_at_utc, updated_at_utc, updated_by)
                    VALUES
                        ($gps_id, 'GPS', $gps_filter, $now, $now, 'test'),
                        ($place_id, 'Place', $place_filter, $now, $now, 'test');
                    """;
                command.Parameters.AddWithValue("$gps_id", gpsId);
                command.Parameters.AddWithValue(
                    "$gps_filter",
                    "{\"location\":{\"south\":59.0,\"west\":17.0,\"north\":60.0,\"east\":19.0}}");
                command.Parameters.AddWithValue("$place_id", placeId);
                command.Parameters.AddWithValue(
                    "$place_filter",
                    "{\"tags\":[\"Places/Sweden/Stockholm region\"]}");
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

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

    private sealed class SmartLocationApiFactory : PhotoIdentityApiTestFactory
    {
        public SmartLocationApiFactory(string databasePath)
            : base(databasePath)
        {
        }
    }
}
