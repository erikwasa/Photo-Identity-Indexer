using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class ManualCaptureDateApplicationTests
{
    [Fact]
    public async Task Photo_details_can_set_replace_persist_and_clear_manual_capture_date_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_capture_date_api_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sqlitePath = Path.Combine(directory, "unused.db");

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };

            AssetRevisionId revisionId = AssetRevisionId.New();
            DateTime extractedAtLocal = new(2025, 5, 6, 7, 8, 9, DateTimeKind.Unspecified);

            await using (PostgresCatalogueDatabase database = new(testBuilder.ConnectionString))
            {
                await database.InitializeAsync();
                await SeedRevisionAsync(testBuilder.ConnectionString, revisionId);
                await new PostgresPhotoCaptureMetadataRepository(database).SavePhotoMetadataAsync(
                    revisionId,
                    new PhotoCaptureMetadata(takenAtLocal: extractedAtLocal),
                    new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero));
            }

            Action<WebHostBuilder> configure = builder =>
            {
                builder.UseSetting("PhotoIdentity:CatalogueProvider", "postgresql");
                builder.UseSetting("PhotoIdentity:Postgres:ConnectionString", testBuilder.ConnectionString);
            };

            await using (PhotoIdentityApiTestFactory factory = new(sqlitePath, configure))
            {
                using HttpClient client = factory.CreateClient();
                string url = $"/api/collections/photos/{revisionId}/capture-date";
                string detailsUrl = $"/api/collections/photos/{revisionId}/details";

                PhotoDetailsResponse initial = Assert.IsType<PhotoDetailsResponse>(
                    await client.GetFromJsonAsync<PhotoDetailsResponse>(detailsUrl));
                Assert.Equal(extractedAtLocal, initial.Metadata!.TakenAtLocal);
                Assert.Equal("extracted", initial.CaptureDate!.Source);
                Assert.Equal("timestamp", initial.CaptureDate.Precision);
                Assert.Equal("2025-05-06", initial.CaptureDate.EffectiveFrom);
                Assert.True(initial.CaptureDate.CanEdit);

                using HttpResponseMessage invalid = await client.PutAsJsonAsync(
                    url,
                    new PhotoCaptureDateMutationRequest("2026-02-30"));
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

                using HttpResponseMessage invalidFormat = await client.PutAsJsonAsync(
                    url,
                    new PhotoCaptureDateMutationRequest("not-a-date"));
                Assert.Equal(HttpStatusCode.BadRequest, invalidFormat.StatusCode);

                PhotoDetailsResponse year = await PutAsync(client, url, "1987");
                Assert.Equal("manual", year.CaptureDate!.Source);
                Assert.Equal("year", year.CaptureDate.Precision);
                Assert.Equal("1987", year.CaptureDate.ManualValue);
                Assert.Equal("1987-01-01", year.CaptureDate.EffectiveFrom);
                Assert.Equal("1987-12-31", year.CaptureDate.EffectiveTo);
                Assert.Equal(extractedAtLocal, year.CaptureDate.ExtractedTakenAtLocal);
                Assert.Equal(extractedAtLocal, year.Metadata!.TakenAtLocal);

                PhotoDetailsResponse month = await PutAsync(client, url, "1987-04");
                Assert.Equal("month", month.CaptureDate!.Precision);
                Assert.Equal("1987-04", month.CaptureDate.ManualValue);
                Assert.Equal("1987-04-01", month.CaptureDate.EffectiveFrom);
                Assert.Equal("1987-04-30", month.CaptureDate.EffectiveTo);

                PhotoDetailsResponse day = await PutAsync(client, url, "1987-04-12");
                Assert.Equal("day", day.CaptureDate!.Precision);
                Assert.Equal("1987-04-12", day.CaptureDate.ManualValue);
                Assert.Equal("1987-04-12", day.CaptureDate.EffectiveFrom);
                Assert.Equal("1987-04-12", day.CaptureDate.EffectiveTo);
            }

            // A new API host proves the manual edit is durable across application restart.
            await using (PhotoIdentityApiTestFactory restarted = new(sqlitePath, configure))
            {
                using HttpClient client = restarted.CreateClient();
                string url = $"/api/collections/photos/{revisionId}/capture-date";
                string detailsUrl = $"/api/collections/photos/{revisionId}/details";

                PhotoDetailsResponse persisted = Assert.IsType<PhotoDetailsResponse>(
                    await client.GetFromJsonAsync<PhotoDetailsResponse>(detailsUrl));
                Assert.Equal("manual", persisted.CaptureDate!.Source);
                Assert.Equal("day", persisted.CaptureDate.Precision);
                Assert.Equal("1987-04-12", persisted.CaptureDate.ManualValue);

                using HttpResponseMessage clear = await client.DeleteAsync(url);
                clear.EnsureSuccessStatusCode();
                PhotoDetailsResponse cleared = Assert.IsType<PhotoDetailsResponse>(
                    await clear.Content.ReadFromJsonAsync<PhotoDetailsResponse>());

                Assert.Equal("extracted", cleared.CaptureDate!.Source);
                Assert.Equal("timestamp", cleared.CaptureDate.Precision);
                Assert.Null(cleared.CaptureDate.ManualValue);
                Assert.Equal("2025-05-06", cleared.CaptureDate.EffectiveFrom);
                Assert.Equal(extractedAtLocal, cleared.Metadata!.TakenAtLocal);
            }
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<PhotoDetailsResponse> PutAsync(
        HttpClient client,
        string url,
        string value)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            url,
            new PhotoCaptureDateMutationRequest(value));
        response.EnsureSuccessStatusCode();
        return Assert.IsType<PhotoDetailsResponse>(
            await response.Content.ReadFromJsonAsync<PhotoDetailsResponse>());
    }

    private static async Task SeedRevisionAsync(
        string connectionString,
        AssetRevisionId revisionId)
    {
        Guid sourceId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 20, 20, 0, 0, TimeSpan.Zero);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@source_id, 'test', @root_locator, @now);

            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
            VALUES (@asset_id, @source_id, 'photos/capture-date.jpg', @now, @now);

            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES (
                @revision_id,
                @asset_id,
                @content_sha256,
                123,
                @now,
                'image/jpeg',
                1200,
                800);
            """;
        command.Parameters.AddWithValue("source_id", NpgsqlDbType.Uuid, sourceId);
        command.Parameters.AddWithValue("root_locator", NpgsqlDbType.Text, $"capture-date-api-{sourceId:N}");
        command.Parameters.AddWithValue("asset_id", NpgsqlDbType.Uuid, assetId);
        command.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        command.Parameters.AddWithValue("content_sha256", NpgsqlDbType.Text, new string('b', 64));
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
