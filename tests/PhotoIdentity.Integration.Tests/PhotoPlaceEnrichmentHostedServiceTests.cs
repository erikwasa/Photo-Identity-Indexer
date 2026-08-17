using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoPlaceEnrichmentHostedServiceTests
{
    [Fact]
    public async Task Automatic_cycle_assigns_pending_gps_and_enforces_safe_provider_delay()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            AssetRevisionId revisionId = await CreateRevisionWithGpsAsync(database, directory);

            TimeProvider clock = TimeProvider.System;
            SqlitePhotoPlaceRepository places = new(database, clock);
            PhotoPlaceEnrichmentService enrichment = new(
                new SuccessfulGeocoder(),
                new SqlitePhotoPlaceEnrichmentRepository(database, clock),
                new SqliteAutomaticPhotoPlaceRepository(database, places, clock));
            GeoNamesAutomaticEnrichmentConfiguration automatic = new(
                enabled: null,
                minimumRequestIntervalMilliseconds: 1_000,
                idlePollIntervalMilliseconds: 1_000);
            PhotoPlaceEnrichmentWorkerState workerState = new();
            PhotoPlaceEnrichmentHostedService worker = new(
                new GeoNamesReverseGeocodingConfiguration(
                    "configured-test-user",
                    baseUrl: null,
                    language: null,
                    minimumRequestIntervalMilliseconds: 0),
                automatic,
                enrichment,
                workerState,
                clock,
                NullLogger<PhotoPlaceEnrichmentHostedService>.Instance);

            PhotoPlaceEnrichmentWorkerCycleResult cycle = await worker.RunOnceAsync();

            Assert.NotNull(cycle.Report);
            Assert.Equal(1, cycle.Report.Candidates);
            Assert.Equal(1, cycle.Report.ProviderRequests);
            Assert.Equal(1, cycle.Report.Assigned);
            Assert.Equal(
                TimeSpan.FromMilliseconds(GeoNamesAutomaticEnrichmentConfiguration.SafeMinimumRequestIntervalMilliseconds),
                cycle.Delay);
            Assert.Equal(
                GeoNamesAutomaticEnrichmentConfiguration.SafeMinimumRequestIntervalMilliseconds,
                automatic.MinimumRequestIntervalMilliseconds);

            CataloguePhotoPlaceState state = await places.GetStateAsync(revisionId);
            Assert.NotNull(state.Place);
            Assert.Equal("Sweden/Stockholm County/Stockholm", state.Place.Value);
            Assert.Equal("automatic", state.Place.SourceKind);

            PhotoPlaceEnrichmentWorkerSnapshot snapshot = workerState.GetSnapshot();
            Assert.Equal("running", snapshot.State);
            Assert.NotNull(snapshot.LastActivityAtUtc);
            Assert.NotNull(snapshot.NextAttemptAtUtc);
            Assert.True(snapshot.NextAttemptAtUtc > snapshot.LastActivityAtUtc);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Automatic_cycle_becomes_idle_when_no_gps_work_remains()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            TimeProvider clock = TimeProvider.System;
            SqlitePhotoPlaceRepository places = new(database, clock);
            PhotoPlaceEnrichmentService enrichment = new(
                new SuccessfulGeocoder(),
                new SqlitePhotoPlaceEnrichmentRepository(database, clock),
                new SqliteAutomaticPhotoPlaceRepository(database, places, clock));
            GeoNamesAutomaticEnrichmentConfiguration automatic = new(
                enabled: true,
                minimumRequestIntervalMilliseconds: null,
                idlePollIntervalMilliseconds: 1_000);
            PhotoPlaceEnrichmentWorkerState workerState = new();
            PhotoPlaceEnrichmentHostedService worker = new(
                new GeoNamesReverseGeocodingConfiguration(
                    "configured-test-user",
                    baseUrl: null,
                    language: null,
                    minimumRequestIntervalMilliseconds: 0),
                automatic,
                enrichment,
                workerState,
                clock,
                NullLogger<PhotoPlaceEnrichmentHostedService>.Instance);

            PhotoPlaceEnrichmentWorkerCycleResult cycle = await worker.RunOnceAsync();

            Assert.NotNull(cycle.Report);
            Assert.Equal(0, cycle.Report.Candidates);
            Assert.Equal(TimeSpan.FromSeconds(1), cycle.Delay);
            Assert.Equal("idle", workerState.GetSnapshot().State);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Automatic_cycle_can_be_disabled_without_touching_the_queue()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            AssetRevisionId revisionId = await CreateRevisionWithGpsAsync(database, directory);

            TimeProvider clock = TimeProvider.System;
            CountingGeocoder provider = new();
            SqlitePhotoPlaceRepository places = new(database, clock);
            PhotoPlaceEnrichmentService enrichment = new(
                provider,
                new SqlitePhotoPlaceEnrichmentRepository(database, clock),
                new SqliteAutomaticPhotoPlaceRepository(database, places, clock));
            GeoNamesAutomaticEnrichmentConfiguration automatic = new(
                enabled: false,
                minimumRequestIntervalMilliseconds: null,
                idlePollIntervalMilliseconds: 1_000);
            PhotoPlaceEnrichmentWorkerState workerState = new();
            PhotoPlaceEnrichmentHostedService worker = new(
                new GeoNamesReverseGeocodingConfiguration(
                    "configured-test-user",
                    baseUrl: null,
                    language: null,
                    minimumRequestIntervalMilliseconds: 0),
                automatic,
                enrichment,
                workerState,
                clock,
                NullLogger<PhotoPlaceEnrichmentHostedService>.Instance);

            PhotoPlaceEnrichmentWorkerCycleResult cycle = await worker.RunOnceAsync();

            Assert.Null(cycle.Report);
            Assert.Equal(0, provider.RequestCount);
            Assert.Equal("disabled", workerState.GetSnapshot().State);
            Assert.Null((await places.GetStateAsync(revisionId)).Place);

            IReadOnlyList<CataloguePlaceEnrichmentCandidate> pending =
                await new SqlitePhotoPlaceEnrichmentRepository(database, clock).GetCandidatesAsync(
                    provider.ProviderName,
                    provider.ContractKey,
                    limit: 10,
                    refresh: false);
            Assert.Single(pending);
            Assert.Equal(revisionId, pending[0].RevisionId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<AssetRevisionId> CreateRevisionWithGpsAsync(
        SqliteCatalogueDatabase database,
        string directory)
    {
        DateTimeOffset now = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
        CatalogueSource source = new(
            SourceId.New(),
            "local-folder",
            Path.Combine(directory, "source"),
            now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, "gps-photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(new string('b', 64)),
            123,
            now,
            "image/jpeg",
            100,
            100);
        CatalogueAssetRevision saved = await new SqliteAssetCatalogueRepository(database)
            .SaveRevisionAsync(source, asset, revision);

        await using SqliteConnection connection = await database.OpenConnectionAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_capture_metadata (
                asset_revision_id, taken_at_local, utc_offset_minutes,
                latitude, longitude, extracted_at_utc)
            VALUES ($revision_id, NULL, NULL, 59.3293, 18.0686, $extracted_at_utc);
            """;
        command.Parameters.AddWithValue("$revision_id", saved.Id.ToString());
        command.Parameters.AddWithValue("$extracted_at_utc", now.ToString("O"));
        await command.ExecuteNonQueryAsync();
        return saved.Id;
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

    private sealed class SuccessfulGeocoder : IReverseGeocoder
    {
        public string ProviderName => "geonames";

        public string ContractKey => "automatic-worker-test-v1";

        public Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
            ReverseGeocodeQuery query,
            CancellationToken cancellationToken = default)
        {
            query.Validate();
            return Task.FromResult(ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                PhotoPlacePath.Parse("Sweden/Stockholm County/Stockholm"),
                ProviderResultId: "2673730",
                CountryCode: "SE")));
        }
    }

    private sealed class CountingGeocoder : IReverseGeocoder
    {
        public int RequestCount { get; private set; }

        public string ProviderName => "geonames";

        public string ContractKey => "automatic-worker-disabled-test-v1";

        public Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
            ReverseGeocodeQuery query,
            CancellationToken cancellationToken = default)
        {
            query.Validate();
            RequestCount++;
            return Task.FromResult(ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                PhotoPlacePath.Parse("Sweden/Stockholm County/Stockholm"),
                ProviderResultId: "2673730",
                CountryCode: "SE")));
        }
    }
}
