using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Core.Tags;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoPlaceEnrichmentOperatorReportingTests
{
    [Fact]
    public async Task Authorization_failure_is_actionable_without_exposing_provider_message_or_username()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            AssetRevisionId revisionId = await CreateRevisionWithGpsAsync(database, directory);

            const string privateUsername = "private-maintainer-account";
            IReverseGeocoder provider = new AuthorizationFailureGeocoder(privateUsername);
            TimeProvider clock = TimeProvider.System;
            SqlitePhotoPlaceRepository places = new(database, clock);
            PhotoPlaceEnrichmentService service = new(
                provider,
                new SqlitePhotoPlaceEnrichmentRepository(database, clock),
                new SqliteAutomaticPhotoPlaceRepository(database, places, clock));

            PhotoPlaceEnrichmentReport report = await service.ExecuteBatchAsync(limit: 5);

            Assert.Equal(1, report.Candidates);
            Assert.Equal(1, report.ProviderRequests);
            Assert.Equal(1, report.Failed);
            Assert.True(report.StoppedEarly);
            Assert.Equal("10", report.StopReasonCode);
            Assert.Contains("enable Free Web Services", report.StopReasonMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateUsername, report.StopReasonMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw-provider-secret", report.StopReasonMessage, StringComparison.OrdinalIgnoreCase);
            PhotoPlaceEnrichmentIssue issue = Assert.Single(report.Issues!);
            Assert.Equal(revisionId.ToString(), issue.RevisionId);
            Assert.Equal("failed", issue.Outcome);
            Assert.Equal("10", issue.ProviderCode);
            Assert.DoesNotContain(privateUsername, issue.Message, StringComparison.OrdinalIgnoreCase);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT status, last_error_code, last_error_message
                FROM photo_place_enrichment_attempts
                WHERE asset_revision_id = $revision_id
                  AND provider = 'geonames';
                """;
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("failed", reader.GetString(0));
            Assert.Equal("10", reader.GetString(1));
            Assert.Contains(privateUsername, reader.GetString(2), StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task No_result_is_reported_separately_and_does_not_spend_credits_again_on_normal_runs()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            AssetRevisionId revisionId = await CreateRevisionWithGpsAsync(database, directory);

            IReverseGeocoder provider = new NoResultGeocoder();
            TimeProvider clock = TimeProvider.System;
            SqlitePhotoPlaceRepository places = new(database, clock);
            PhotoPlaceEnrichmentService service = new(
                provider,
                new SqlitePhotoPlaceEnrichmentRepository(database, clock),
                new SqliteAutomaticPhotoPlaceRepository(database, places, clock));

            PhotoPlaceEnrichmentReport first = await service.ExecuteBatchAsync(limit: 5);

            Assert.Equal(1, first.Candidates);
            Assert.Equal(1, first.ProviderRequests);
            Assert.Equal(1, first.NoResult);
            Assert.Equal(0, first.Failed);
            Assert.False(first.StoppedEarly);
            PhotoPlaceEnrichmentIssue issue = Assert.Single(first.Issues!);
            Assert.Equal(revisionId.ToString(), issue.RevisionId);
            Assert.Equal("no-result", issue.Outcome);
            Assert.Equal("15", issue.ProviderCode);
            Assert.Contains("no nearby populated place", issue.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw-provider-message", issue.Message, StringComparison.OrdinalIgnoreCase);

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT status, last_error_code, completed_at_utc
                    FROM photo_place_enrichment_attempts
                    WHERE asset_revision_id = $revision_id
                      AND provider = 'geonames';
                    """;
                command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("skipped", reader.GetString(0));
                Assert.Equal("15", reader.GetString(1));
                Assert.False(reader.IsDBNull(2));
            }

            PhotoPlaceEnrichmentReport second = await service.ExecuteBatchAsync(limit: 5);
            Assert.Equal(0, second.Candidates);
            Assert.Equal(0, second.ProviderRequests);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Provider_place_hierarchy_longer_than_ordinary_tag_limit_is_persisted_and_assigned()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();
            AssetRevisionId revisionId = await CreateRevisionWithGpsAsync(database, directory);

            TimeProvider clock = TimeProvider.System;
            SqlitePhotoPlaceRepository places = new(database, clock);
            PhotoPlaceEnrichmentService service = new(
                new LongHierarchyGeocoder(),
                new SqlitePhotoPlaceEnrichmentRepository(database, clock),
                new SqliteAutomaticPhotoPlaceRepository(database, places, clock));

            string ordinaryTooLong = "Family/" + new string('x', PhotoTagPath.MaximumValueLength - "Family/".Length + 1);
            Assert.Throws<ArgumentException>(() => PhotoTagPath.Parse(ordinaryTooLong));
            Assert.True(LongHierarchyGeocoder.Place.Length > PhotoTagPath.MaximumValueLength);
            Assert.True(LongHierarchyGeocoder.CanonicalPlace.Length > PhotoTagPath.MaximumValueLength);
            Assert.True(LongHierarchyGeocoder.CanonicalPlace.Length <= PhotoPlacePath.MaximumCanonicalValueLength);

            PhotoPlaceEnrichmentReport report = await service.ExecuteBatchAsync(limit: 5);

            Assert.Equal(1, report.Candidates);
            Assert.Equal(1, report.ProviderRequests);
            Assert.Equal(1, report.Assigned);
            Assert.Equal(0, report.Failed);
            Assert.Equal(0, report.NoResult);

            CataloguePhotoPlaceState state = await places.GetStateAsync(revisionId);
            Assert.NotNull(state.Place);
            Assert.Equal(LongHierarchyGeocoder.Place, state.Place.Value);
            Assert.Equal("automatic", state.Place.SourceKind);

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    (SELECT length(display_name)
                     FROM photo_tags
                     WHERE normalized_name = $normalized_place),
                    (SELECT length(place_value)
                     FROM photo_place_reverse_geocode_cache
                     WHERE provider = 'geonames'),
                    (SELECT length(place_value)
                     FROM photo_place_enrichment_attempts
                     WHERE asset_revision_id = $revision_id
                       AND provider = 'geonames');
                """;
            command.Parameters.AddWithValue("$normalized_place", LongHierarchyGeocoder.CanonicalPlace.ToLowerInvariant());
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetInt64(0) > PhotoTagPath.MaximumValueLength);
            Assert.True(reader.GetInt64(1) > PhotoTagPath.MaximumValueLength);
            Assert.True(reader.GetInt64(2) > PhotoTagPath.MaximumValueLength);
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
        DateTimeOffset now = new(2026, 8, 17, 20, 0, 0, TimeSpan.Zero);
        CatalogueSource source = new(
            SourceId.New(),
            "local-folder",
            Path.Combine(directory, "original-does-not-exist"),
            now);
        CatalogueAsset asset = new(AssetId.New(), source.Id, "gps-photo.jpg", now);
        CatalogueAssetRevision revision = new(
            AssetRevisionId.New(),
            asset.Id,
            new Sha256Digest(new string('a', 64)),
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
            VALUES ($revision_id, NULL, NULL, 59.758, 18.705, $extracted_at_utc);
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

    private sealed class AuthorizationFailureGeocoder(string username) : IReverseGeocoder
    {
        public string ProviderName => "geonames";

        public string ContractKey => "operator-reporting-test-v1";

        public Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
            ReverseGeocodeQuery query,
            CancellationToken cancellationToken = default)
        {
            query.Validate();
            return Task.FromResult(new ReverseGeocodeResponse(
                ReverseGeocodeStatus.Failure,
                ErrorCode: "10",
                ErrorMessage: $"raw-provider-secret: user account {username} is not enabled for free webservice",
                StopBatch: true,
                ProviderRequestCount: 1));
        }
    }

    private sealed class NoResultGeocoder : IReverseGeocoder
    {
        public string ProviderName => "geonames";

        public string ContractKey => "no-result-reporting-test-v1";

        public Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
            ReverseGeocodeQuery query,
            CancellationToken cancellationToken = default)
        {
            query.Validate();
            return Task.FromResult(new ReverseGeocodeResponse(
                ReverseGeocodeStatus.NoResult,
                ErrorCode: "15",
                ErrorMessage: "raw-provider-message: no result found",
                ProviderRequestCount: 1));
        }
    }

    private sealed class LongHierarchyGeocoder : IReverseGeocoder
    {
        public const string Place =
            "Sweden/Västernorrland County/Sundsvall Municipality/Njurunda District/Sundsvall/Njurundabommen";
        public const string CanonicalPlace = "Places/" + Place;

        public string ProviderName => "geonames";

        public string ContractKey => "long-hierarchy-reporting-test-v1";

        public Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
            ReverseGeocodeQuery query,
            CancellationToken cancellationToken = default)
        {
            query.Validate();
            return Task.FromResult(ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                PhotoPlacePath.Parse(Place),
                ProviderResultId: "2670781",
                CountryCode: "SE")));
        }
    }
}
