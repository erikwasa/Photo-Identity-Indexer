using System.Net;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Places;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class GeoNamesReverseGeocoderTests
{
    [Fact]
    public async Task Swedish_provider_result_keeps_local_language_and_builds_canonical_hierarchy()
    {
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "geonames": [
                    {
                      "geonameId": 2688250,
                      "name": "Norrtälje",
                      "countryName": "Sverige",
                      "countryCode": "SE",
                      "adminName1": "Stockholms län"
                    }
                  ]
                }
                """)
        });
        using HttpClient client = new(handler);
        GeoNamesReverseGeocodingConfiguration configuration = new(
            "private-user",
            null,
            "local",
            minimumRequestIntervalMilliseconds: 0);
        using GeoNamesReverseGeocoder geocoder = new(
            configuration,
            new SingleClientFactory(client),
            TimeProvider.System);

        ReverseGeocodeResponse response = await geocoder.ReverseGeocodeAsync(
            new ReverseGeocodeQuery(59.758, 18.705));

        Assert.Equal(ReverseGeocodeStatus.Success, response.Status);
        Assert.NotNull(response.Place);
        Assert.Equal("Sverige/Stockholms län/Norrtälje", response.Place.Place.DisplayValue);
        Assert.Equal("2688250", response.Place.ProviderResultId);
        Assert.Equal("SE", response.Place.CountryCode);
        Assert.Equal(1, response.ProviderRequestCount);
        Assert.Single(handler.RequestUris);
        Assert.Contains("lang=local", handler.RequestUris[0].Query);
        Assert.Contains("langPolicy=se-local-else-en", configuration.ContractKey);
        Assert.Equal("Sweden: local; elsewhere: English", configuration.LanguageDescription);
        Assert.Equal("https", handler.RequestUris[0].Scheme);
        Assert.Equal("secure.geonames.org", handler.RequestUris[0].Host);
        string query = handler.RequestUris[0].Query;
        Assert.Contains("lat=59.758", query);
        Assert.Contains("lng=18.705", query);
        Assert.Contains("username=private-user", query);
        Assert.Contains("maxRows=1", query);
        Assert.Contains("localCountry=true", query);
        Assert.DoesNotContain("filename", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("person", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tag", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_swedish_local_result_is_requeried_in_english_before_assignment()
    {
        CapturingHandler handler = new(request =>
        {
            bool english = request.RequestUri?.Query.Contains("lang=en", StringComparison.Ordinal) == true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(english
                    ? """
                        {
                          "geonames": [
                            {
                              "geonameId": 3128760,
                              "name": "Barcelona",
                              "countryName": "Spain",
                              "countryCode": "ES",
                              "adminName1": "Catalonia"
                            }
                          ]
                        }
                        """
                    : """
                        {
                          "geonames": [
                            {
                              "geonameId": 3128760,
                              "name": "Barcelona",
                              "countryName": "España",
                              "countryCode": "ES",
                              "adminName1": "Cataluña"
                            }
                          ]
                        }
                        """)
            };
        });
        using HttpClient client = new(handler);
        using GeoNamesReverseGeocoder geocoder = new(
            new GeoNamesReverseGeocodingConfiguration("private-user", null, "local", 0),
            new SingleClientFactory(client),
            TimeProvider.System);

        ReverseGeocodeResponse response = await geocoder.ReverseGeocodeAsync(
            new ReverseGeocodeQuery(41.3874, 2.1686));

        Assert.Equal(ReverseGeocodeStatus.Success, response.Status);
        Assert.NotNull(response.Place);
        Assert.Equal("Spain/Catalonia/Barcelona", response.Place.Place.DisplayValue);
        Assert.Equal("ES", response.Place.CountryCode);
        Assert.Equal(2, response.ProviderRequestCount);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("lang=local", handler.RequestUris[0].Query);
        Assert.Contains("lang=en", handler.RequestUris[1].Query);
    }

    [Fact]
    public async Task Explicit_language_override_uses_one_request_without_country_policy_fallback()
    {
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "geonames": [
                    {
                      "geonameId": 3128760,
                      "name": "Barcelona",
                      "countryName": "Spanien",
                      "countryCode": "ES",
                      "adminName1": "Katalonien"
                    }
                  ]
                }
                """)
        });
        using HttpClient client = new(handler);
        using GeoNamesReverseGeocoder geocoder = new(
            new GeoNamesReverseGeocodingConfiguration("private-user", null, "sv", 0),
            new SingleClientFactory(client),
            TimeProvider.System);

        ReverseGeocodeResponse response = await geocoder.ReverseGeocodeAsync(
            new ReverseGeocodeQuery(41.3874, 2.1686));

        Assert.Equal("Spanien/Katalonien/Barcelona", response.Place?.Place.DisplayValue);
        Assert.Equal(1, response.ProviderRequestCount);
        Assert.Single(handler.RequestUris);
        Assert.Contains("lang=sv", handler.RequestUris[0].Query);
    }

    [Fact]
    public async Task Quota_status_defers_and_stops_the_batch_cleanly()
    {
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                { "status": { "message": "the hourly limit of 1000 credits for demo has been exceeded", "value": 19 } }
                """)
        });
        using HttpClient client = new(handler);
        using GeoNamesReverseGeocoder geocoder = new(
            new GeoNamesReverseGeocodingConfiguration("private-user", null, null, 0),
            new SingleClientFactory(client),
            TimeProvider.System);

        ReverseGeocodeResponse response = await geocoder.ReverseGeocodeAsync(
            new ReverseGeocodeQuery(59, 18));

        Assert.Equal(ReverseGeocodeStatus.Deferred, response.Status);
        Assert.Equal("19", response.ErrorCode);
        Assert.True(response.StopBatch);
        Assert.Equal(1, response.ProviderRequestCount);
    }

    [Fact]
    public void Configuration_rejects_demo_account_non_https_service_urls_and_invalid_pacing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new GeoNamesReverseGeocodingConfiguration("demo", null, null, 0));
        Assert.Throws<InvalidOperationException>(() =>
            new GeoNamesReverseGeocodingConfiguration("private-user", "http://api.geonames.org/", null, 0));
        Assert.Throws<InvalidOperationException>(() =>
            new GeoNamesReverseGeocodingConfiguration("private-user", null, null, -1));
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is Uri uri)
            {
                RequestUris.Add(uri);
            }
            return Task.FromResult(_response(request));
        }
    }
}
