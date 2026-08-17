using System.Net;
using PhotoIdentity.Api;
using PhotoIdentity.Core.Places;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class GeoNamesReverseGeocoderTests
{
    [Fact]
    public async Task Provider_sends_only_coordinate_contract_and_builds_canonical_hierarchy()
    {
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "geonames": [
                    {
                      "geonameId": 2688250,
                      "name": "Norrtälje",
                      "countryName": "Sweden",
                      "countryCode": "SE",
                      "adminName1": "Stockholm County"
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
        Assert.Equal("Sweden/Stockholm County/Norrtälje", response.Place.Place.DisplayValue);
        Assert.Equal("2688250", response.Place.ProviderResultId);
        Assert.Equal("SE", response.Place.CountryCode);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("https", handler.LastRequestUri.Scheme);
        Assert.Equal("secure.geonames.org", handler.LastRequestUri.Host);
        string query = handler.LastRequestUri.Query;
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
    }

    [Fact]
    public void Configuration_rejects_demo_account_and_non_https_service_urls()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new GeoNamesReverseGeocodingConfiguration("demo", null, null, 0));
        Assert.Throws<InvalidOperationException>(() =>
            new GeoNamesReverseGeocodingConfiguration("private-user", "http://api.geonames.org/", null, 0));
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

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_response(request));
        }
    }
}
