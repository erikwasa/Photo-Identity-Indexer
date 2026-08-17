using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
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
                StopBatch: true));
        }
    }
}
