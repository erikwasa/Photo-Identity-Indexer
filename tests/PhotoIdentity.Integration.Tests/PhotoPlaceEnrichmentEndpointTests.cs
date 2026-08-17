using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
                username);
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
            Assert.Equal(
                GeoNamesAutomaticEnrichmentConfiguration.SafeMinimumRequestIntervalMilliseconds,
                status.AutomaticMinimumRequestIntervalMilliseconds);
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

    private sealed class PlaceEnrichmentApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string? _username;

        public PlaceEnrichmentApiFactory(string databasePath, string? username)
        {
            _databasePath = databasePath;
            _username = username;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting("PhotoIdentity:GeoNames:Username", _username ?? string.Empty);
            builder.UseSetting("PhotoIdentity:GeoNames:MinimumRequestIntervalMilliseconds", "0");
            builder.UseSetting("PhotoIdentity:GeoNames:AutomaticEnrichmentEnabled", "false");
        }
    }
}
