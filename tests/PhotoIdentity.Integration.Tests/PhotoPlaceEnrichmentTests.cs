using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoPlaceEnrichmentTests
{
    [Fact]
    public async Task Identical_persisted_gps_reuses_cache_and_never_opens_source_files()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededRevision first = await CreateRevisionAsync(database, directory, "first.jpg", 'a');
            SeededRevision second = await CreateRevisionAsync(database, directory, "second.jpg", 'b');
            await SaveGpsAsync(database, first.RevisionId, 59.758, 18.705);
            await SaveGpsAsync(database, second.RevisionId, 59.758, 18.705);

            FakeReverseGeocoder provider = new([
                ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                    PhotoPlacePath.Parse("Sweden/Stockholm County/Norrtälje"),
                    "2688250",
                    "SE")),
            ]);
            PhotoPlaceEnrichmentService service = CreateService(database, provider);

            PhotoPlaceEnrichmentReport report = await service.ExecuteBatchAsync(limit: 10);

            Assert.Equal(2, report.Candidates);
            Assert.Equal(1, report.ProviderRequests);
            Assert.Equal(1, report.CachedResults);
            Assert.Equal(2, report.Assigned);
            Assert.Equal(1, provider.CallCount);

            SqlitePhotoPlaceRepository places = new(database, TimeProvider.System);
            CataloguePhotoPlaceState firstState = await places.GetStateAsync(first.RevisionId);
            CataloguePhotoPlaceState secondState = await places.GetStateAsync(second.RevisionId);
            Assert.Equal("Sweden/Stockholm County/Norrtälje", firstState.Place?.Value);
            Assert.Equal("automatic", firstState.Place?.SourceKind);
            Assert.Equal("Sweden/Stockholm County/Norrtälje", secondState.Place?.Value);
            Assert.Equal("automatic", secondState.Place?.SourceKind);

            PhotoPlaceEnrichmentReport rerun = await service.ExecuteBatchAsync(limit: 10);
            Assert.Equal(0, rerun.Candidates);
            Assert.Equal(1, provider.CallCount);
            Assert.False(Directory.Exists(first.SourceRoot));
            Assert.False(Directory.Exists(second.SourceRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Manual_clear_is_terminal_precedence_and_does_not_spend_provider_credit()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededRevision seeded = await CreateRevisionAsync(database, directory, "manual.jpg", 'c');
            await SaveGpsAsync(database, seeded.RevisionId, 57.6348, 18.2948);

            SqlitePhotoPlaceRepository places = new(database, TimeProvider.System);
            await places.SetManualPlaceAsync(seeded.RevisionId, "Sweden/Gotland/Visby", "test-maintainer");
            await places.ClearManualPlaceAsync(seeded.RevisionId, "test-maintainer");

            FakeReverseGeocoder provider = new([
                ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                    PhotoPlacePath.Parse("Sweden/Gotland/Visby"),
                    "2662689",
                    "SE")),
            ]);
            PhotoPlaceEnrichmentService service = CreateService(database, provider);

            PhotoPlaceEnrichmentReport report = await service.ExecuteBatchAsync(limit: 10);

            Assert.Equal(1, report.Candidates);
            Assert.Equal(1, report.SkippedManual);
            Assert.Equal(0, report.ProviderRequests);
            Assert.Equal(0, provider.CallCount);
            Assert.Null((await places.GetStateAsync(seeded.RevisionId)).Place);

            PhotoPlaceEnrichmentReport rerun = await service.ExecuteBatchAsync(limit: 10);
            Assert.Equal(0, rerun.Candidates);
            Assert.False(Directory.Exists(seeded.SourceRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Deferred_provider_attempt_remains_retryable_and_later_succeeds()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededRevision seeded = await CreateRevisionAsync(database, directory, "retry.jpg", 'd');
            await SaveGpsAsync(database, seeded.RevisionId, 59.758, 18.705);

            FakeReverseGeocoder provider = new([
                new ReverseGeocodeResponse(
                    ReverseGeocodeStatus.Deferred,
                    ErrorCode: "19",
                    ErrorMessage: "hourly credit limit",
                    StopBatch: true),
                ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                    PhotoPlacePath.Parse("Sweden/Stockholm County/Norrtälje"),
                    "2688250",
                    "SE")),
            ]);
            PhotoPlaceEnrichmentService service = CreateService(database, provider);

            PhotoPlaceEnrichmentReport first = await service.ExecuteBatchAsync(limit: 10);
            Assert.Equal(1, first.Deferred);
            Assert.True(first.StoppedEarly);
            Assert.Equal(1, provider.CallCount);

            PhotoPlaceEnrichmentReport second = await service.ExecuteBatchAsync(limit: 10);
            Assert.Equal(1, second.Assigned);
            Assert.Equal(2, provider.CallCount);

            CataloguePhotoPlaceState state = await new SqlitePhotoPlaceRepository(database, TimeProvider.System)
                .GetStateAsync(seeded.RevisionId);
            Assert.Equal("Sweden/Stockholm County/Norrtälje", state.Place?.Value);
            Assert.Equal("automatic", state.Place?.SourceKind);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT status, attempt_count
                FROM photo_place_enrichment_attempts
                WHERE asset_revision_id = $revision_id
                  AND provider = 'geonames';
                """;
            command.Parameters.AddWithValue("$revision_id", seeded.RevisionId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("succeeded", reader.GetString(0));
            Assert.Equal(2, reader.GetInt32(1));
            Assert.False(Directory.Exists(seeded.SourceRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Explicit_refresh_can_replace_previous_automatic_place_only()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            SeededRevision seeded = await CreateRevisionAsync(database, directory, "refresh.jpg", 'e');
            await SaveGpsAsync(database, seeded.RevisionId, 59.758, 18.705);

            FakeReverseGeocoder provider = new([
                ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                    PhotoPlacePath.Parse("Sweden/Stockholm County"),
                    "county",
                    "SE")),
                ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                    PhotoPlacePath.Parse("Sweden/Stockholm County/Norrtälje"),
                    "2688250",
                    "SE")),
            ]);
            PhotoPlaceEnrichmentService service = CreateService(database, provider);

            Assert.Equal(1, (await service.ExecuteBatchAsync(limit: 10)).Assigned);
            Assert.Equal(1, (await service.ExecuteBatchAsync(limit: 10, refresh: true)).Assigned);

            CataloguePhotoPlaceState state = await new SqlitePhotoPlaceRepository(database, TimeProvider.System)
                .GetStateAsync(seeded.RevisionId);
            Assert.Equal("Sweden/Stockholm County/Norrtälje", state.Place?.Value);
            Assert.Equal("automatic", state.Place?.SourceKind);
            Assert.Equal(2, provider.CallCount);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM photo_place_actions
                WHERE asset_revision_id = $revision_id
                  AND source_kind = 'automatic';
                """;
            command.Parameters.AddWithValue("$revision_id", seeded.RevisionId.ToString());
            Assert.Equal(2, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static PhotoPlaceEnrichmentService CreateService(
        SqliteCatalogueDatabase database,
        IReverseGeocoder provider)
    {
        TimeProvider clock = TimeProvider.System;
        SqlitePhotoPlaceRepository places = new(database, clock);
        return new PhotoPlaceEnrichmentService(
            provider,
            new SqlitePhotoPlaceEnrichmentRepository(database, clock),
            new SqliteAutomaticPhotoPlaceRepository(database, places, clock));
    }

    private static async Task<SeededRevision> CreateRevisionAsync(
        SqliteCatalogueDatabase database,
        string directory,
        string sourceKey,
        char hashCharacter)
    {
        string sourceRoot = Path.Combine(directory, "originals-do-not-exist", Path.GetFileNameWithoutExtension(sourceKey));
        DateTimeOffset now = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        CatalogueSource source = new(SourceId.New(), "local-folder", sourceRoot, now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, sourceKey, now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(new string(hashCharacter, 64)),
            123,
            now,
            "image/jpeg",
            100,
            100);
        CatalogueAssetRevision saved = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);
        return new SeededRevision(saved.Id, sourceRoot);
    }

    private static async Task SaveGpsAsync(
        SqliteCatalogueDatabase database,
        AssetRevisionId revisionId,
        double latitude,
        double longitude)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_capture_metadata (
                asset_revision_id, taken_at_local, utc_offset_minutes,
                latitude, longitude, extracted_at_utc)
            VALUES ($revision_id, NULL, NULL, $latitude, $longitude, $extracted_at_utc);
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$latitude", latitude);
        command.Parameters.AddWithValue("$longitude", longitude);
        command.Parameters.AddWithValue("$extracted_at_utc", "2026-08-17T00:00:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
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

    private sealed record SeededRevision(AssetRevisionId RevisionId, string SourceRoot);

    private sealed class FakeReverseGeocoder : IReverseGeocoder
    {
        private readonly Queue<ReverseGeocodeResponse> _responses;

        public FakeReverseGeocoder(IEnumerable<ReverseGeocodeResponse> responses) =>
            _responses = new Queue<ReverseGeocodeResponse>(responses);

        public string ProviderName => "geonames";

        public string ContractKey => "test-geonames-contract-v1";

        public int CallCount { get; private set; }

        public Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
            ReverseGeocodeQuery query,
            CancellationToken cancellationToken = default)
        {
            query.Validate();
            CallCount++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("The fake reverse geocoder received an unexpected call.");
            }
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
