using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoPlaceEnrichmentEndpointTests
{
    [Fact]
    public async Task Status_reports_configuration_without_exposing_username()
    {
        string directory = CreateTemporaryDirectory();
        const string username = "private-geonames-maintainer";
        try
        {
            await using PlaceEnrichmentApiFactory factory = new(
                Path.Combine(directory, "catalogue.db"),
                username,
                automaticMinimumRequestIntervalMilliseconds: 45_000,
                automaticIdlePollIntervalMilliseconds: 7_000);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync("/api/place-enrichment/status");
            response.EnsureSuccessStatusCode();
            string payload = await response.Content.ReadAsStringAsync();
            PhotoPlaceEnrichmentStatusResponse status =
                await response.Content.ReadFromJsonAsync<PhotoPlaceEnrichmentStatusResponse>()
                ?? throw new InvalidOperationException("The status response was empty.");

            Assert.True(status.Configured);
            Assert.Equal("geonames", status.Provider);
            Assert.Equal("secure.geonames.org", status.ServiceHost);
            Assert.False(status.AutomaticEnrichmentEnabled);
            Assert.Equal(45_000, status.AutomaticMinimumRequestIntervalMilliseconds);
            Assert.Equal(7_000, status.AutomaticIdlePollIntervalMilliseconds);
            Assert.Contains("7000 ms", status.AutomaticMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(username, payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Unconfigured_provider_refuses_execution_before_any_external_request()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            await using PlaceEnrichmentApiFactory factory = new(
                Path.Combine(directory, "catalogue.db"),
                username: null);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(
                "/api/place-enrichment/geonames?limit=10&refresh=false",
                content: null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            PhotoPlaceEnrichmentErrorResponse error =
                await response.Content.ReadFromJsonAsync<PhotoPlaceEnrichmentErrorResponse>()
                ?? throw new InvalidOperationException("The error response was empty.");
            Assert.Contains("disabled", error.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Endpoint_rejects_an_unbounded_batch_before_provider_use()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            await using PlaceEnrichmentApiFactory factory = new(
                Path.Combine(directory, "catalogue.db"),
                "private-geonames-maintainer");
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(
                "/api/place-enrichment/geonames?limit=251&refresh=false",
                content: null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
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

    private sealed class PlaceEnrichmentApiFactory : PhotoIdentityApiTestFactory
    {
        public PlaceEnrichmentApiFactory(
            string databasePath,
            string? username,
            int automaticMinimumRequestIntervalMilliseconds = 30_000,
            int automaticIdlePollIntervalMilliseconds = 5_000)
            : base(
                databasePath,
                builder =>
                {
                    builder.UseSetting("PhotoIdentity:GeoNames:Username", username ?? string.Empty);
                    builder.UseSetting("PhotoIdentity:GeoNames:MinimumRequestIntervalMilliseconds", "0");
                    builder.UseSetting("PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled", "false");
                    builder.UseSetting(
                        "PhotoIdentity:GeoNames:AutomaticMinimumRequestIntervalMilliseconds",
                        automaticMinimumRequestIntervalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    builder.UseSetting(
                        "PhotoIdentity:GeoNames:AutomaticIdlePollIntervalMilliseconds",
                        automaticIdlePollIntervalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                })
        {
        }
    }
}
