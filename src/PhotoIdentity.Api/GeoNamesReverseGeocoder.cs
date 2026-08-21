using System.Globalization;
using System.Net;
using System.Text.Json;
using PhotoIdentity.Core.Places;

namespace PhotoIdentity.Api;

public sealed record GeoNamesReverseGeocodingConfiguration
{
    public const string DefaultBaseUrl = "https://secure.geonames.org/";
    public const int DefaultMinimumRequestIntervalMilliseconds = 11_000;

    public GeoNamesReverseGeocodingConfiguration(
        string? username,
        string? baseUrl,
        string? language,
        int? minimumRequestIntervalMilliseconds)
    {
        Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        if (string.Equals(Username, "demo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("GeoNames username 'demo' is reserved for documentation examples and cannot be used by Photo Identity.");
        }

        string requestedBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
        if (!Uri.TryCreate(requestedBaseUrl, UriKind.Absolute, out Uri? parsedBaseUri) ||
            !string.Equals(parsedBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsedBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(parsedBaseUri.Query) ||
            !string.IsNullOrEmpty(parsedBaseUri.Fragment))
        {
            throw new InvalidOperationException("GeoNames base URL must be an absolute HTTPS URL without credentials, query parameters or fragments.");
        }

        BaseUri = requestedBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? parsedBaseUri
            : new Uri(requestedBaseUrl + "/", UriKind.Absolute);
        Language = string.IsNullOrWhiteSpace(language) ? "local" : language.Trim();
        if (Language.Length > 16 || Language.Any(char.IsControl))
        {
            throw new InvalidOperationException("GeoNames language must be a short language identifier.");
        }

        MinimumRequestIntervalMilliseconds = minimumRequestIntervalMilliseconds ?? DefaultMinimumRequestIntervalMilliseconds;
        if (MinimumRequestIntervalMilliseconds is < 0 or > 600_000)
        {
            throw new InvalidOperationException("GeoNames minimum request interval must be between 0 and 600000 milliseconds.");
        }
    }

    public string? Username { get; }

    public Uri BaseUri { get; }

    public string Language { get; }

    public int MinimumRequestIntervalMilliseconds { get; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username);

    public bool UsesSwedenLocalElseEnglishPolicy =>
        string.Equals(Language, "local", StringComparison.OrdinalIgnoreCase);

    public string LanguageDescription => UsesSwedenLocalElseEnglishPolicy
        ? "Sweden: local; elsewhere: English"
        : Language;

    public string ContractKey => UsesSwedenLocalElseEnglishPolicy
        ? $"findNearbyPlaceName-v2|{BaseUri.AbsoluteUri.ToLowerInvariant()}|langPolicy=se-local-else-en|localCountry=true|style=FULL|maxRows=1"
        : $"findNearbyPlaceName-v1|{BaseUri.AbsoluteUri.ToLowerInvariant()}|lang={Language.ToLowerInvariant()}|localCountry=true|style=FULL|maxRows=1";
}

public sealed class GeoNamesReverseGeocoder : IReverseGeocoder, IDisposable
{
    private readonly GeoNamesReverseGeocodingConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset? _lastRequestAtUtc;

    public GeoNamesReverseGeocoder(
        GeoNamesReverseGeocodingConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public string ProviderName => "geonames";

    public string ContractKey => _configuration.ContractKey;

    public async Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
        ReverseGeocodeQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Validate();
        if (!_configuration.IsConfigured)
        {
            return new ReverseGeocodeResponse(
                ReverseGeocodeStatus.Failure,
                ErrorCode: "not-configured",
                ErrorMessage: "GeoNames enrichment is disabled until a private GeoNames username is configured.",
                StopBatch: true);
        }

        if (!_configuration.UsesSwedenLocalElseEnglishPolicy)
        {
            return await SendRequestAsync(query, _configuration.Language, cancellationToken);
        }

        ReverseGeocodeResponse local = await SendRequestAsync(query, "local", cancellationToken);
        if (local.Status != ReverseGeocodeStatus.Success || local.Place is null)
        {
            return local;
        }

        if (string.Equals(local.Place.CountryCode, "SE", StringComparison.OrdinalIgnoreCase))
        {
            return local;
        }

        ReverseGeocodeResponse english = await SendRequestAsync(query, "en", cancellationToken);
        return english with
        {
            ProviderRequestCount = local.ProviderRequestCount + english.ProviderRequestCount,
        };
    }

    private async Task<ReverseGeocodeResponse> SendRequestAsync(
        ReverseGeocodeQuery query,
        string language,
        CancellationToken cancellationToken)
    {
        await WaitForRequestSlotAsync(cancellationToken);

        Uri requestUri = BuildRequestUri(query, language);
        try
        {
            HttpClient client = _httpClientFactory.CreateClient("GeoNames");
            using HttpResponseMessage response = await client.GetAsync(requestUri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                return new ReverseGeocodeResponse(
                    ReverseGeocodeStatus.Deferred,
                    ErrorCode: $"http-{(int)response.StatusCode}",
                    ErrorMessage: "GeoNames is temporarily unavailable or rate limited the request.",
                    StopBatch: true,
                    ProviderRequestCount: 1);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ReverseGeocodeResponse(
                    ReverseGeocodeStatus.Failure,
                    ErrorCode: $"http-{(int)response.StatusCode}",
                    ErrorMessage: "GeoNames rejected the reverse-geocoding request.",
                    StopBatch: true,
                    ProviderRequestCount: 1);
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(json) with { ProviderRequestCount = 1 };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ReverseGeocodeResponse(
                ReverseGeocodeStatus.Deferred,
                ErrorCode: "transport",
                ErrorMessage: exception.Message,
                StopBatch: true,
                ProviderRequestCount: 1);
        }
    }

    private async Task WaitForRequestSlotAsync(CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            if (_lastRequestAtUtc is DateTimeOffset last && _configuration.MinimumRequestIntervalMilliseconds > 0)
            {
                DateTimeOffset next = last.AddMilliseconds(_configuration.MinimumRequestIntervalMilliseconds);
                TimeSpan delay = next - _timeProvider.GetUtcNow();
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken);
                }
            }

            _lastRequestAtUtc = _timeProvider.GetUtcNow();
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private Uri BuildRequestUri(ReverseGeocodeQuery query, string language)
    {
        Uri endpoint = new(_configuration.BaseUri, "findNearbyPlaceNameJSON");
        string queryString = string.Join(
            "&",
            $"lat={Uri.EscapeDataString(query.Latitude.ToString("R", CultureInfo.InvariantCulture))}",
            $"lng={Uri.EscapeDataString(query.Longitude.ToString("R", CultureInfo.InvariantCulture))}",
            "maxRows=1",
            "style=FULL",
            "localCountry=true",
            $"lang={Uri.EscapeDataString(language)}",
            $"username={Uri.EscapeDataString(_configuration.Username!)}");
        return new UriBuilder(endpoint) { Query = queryString }.Uri;
    }

    private static ReverseGeocodeResponse ParseResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("status", out JsonElement status))
        {
            int code = status.TryGetProperty("value", out JsonElement value) && value.TryGetInt32(out int parsed)
                ? parsed
                : -1;
            string? message = status.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.GetString()
                : null;
            return code switch
            {
                15 => new ReverseGeocodeResponse(ReverseGeocodeStatus.NoResult, ErrorCode: "15", ErrorMessage: message),
                13 or 18 or 19 or 20 or 22 => new ReverseGeocodeResponse(
                    ReverseGeocodeStatus.Deferred,
                    ErrorCode: code.ToString(CultureInfo.InvariantCulture),
                    ErrorMessage: message,
                    StopBatch: true),
                10 or 14 or 21 or 23 or 24 or 27 => new ReverseGeocodeResponse(
                    ReverseGeocodeStatus.Failure,
                    ErrorCode: code.ToString(CultureInfo.InvariantCulture),
                    ErrorMessage: message,
                    StopBatch: true),
                _ => new ReverseGeocodeResponse(
                    ReverseGeocodeStatus.Failure,
                    ErrorCode: code.ToString(CultureInfo.InvariantCulture),
                    ErrorMessage: message),
            };
        }

        if (!root.TryGetProperty("geonames", out JsonElement geonames) ||
            geonames.ValueKind != JsonValueKind.Array ||
            geonames.GetArrayLength() == 0)
        {
            return new ReverseGeocodeResponse(
                ReverseGeocodeStatus.NoResult,
                ErrorCode: "no-result",
                ErrorMessage: "GeoNames returned no populated place for these coordinates.");
        }

        JsonElement item = geonames[0];
        string? country = ReadString(item, "countryName");
        string? locality = ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(locality))
        {
            return new ReverseGeocodeResponse(
                ReverseGeocodeStatus.Failure,
                ErrorCode: "incomplete-result",
                ErrorMessage: "GeoNames returned a populated-place result without both country and locality names.");
        }

        List<string> segments = [];
        AddDistinctSegment(segments, country);
        AddDistinctSegment(segments, ReadString(item, "adminName1"));
        AddDistinctSegment(segments, ReadString(item, "adminName2"));
        AddDistinctSegment(segments, ReadString(item, "adminName3"));
        AddDistinctSegment(segments, ReadString(item, "adminName4"));
        AddDistinctSegment(segments, locality);

        try
        {
            PhotoPlacePath place = PhotoPlacePath.Parse(string.Join('/', segments));
            string? providerResultId = item.TryGetProperty("geonameId", out JsonElement geonameId)
                ? geonameId.ToString()
                : null;
            return ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                place,
                providerResultId,
                ReadString(item, "countryCode")));
        }
        catch (ArgumentException exception)
        {
            return new ReverseGeocodeResponse(
                ReverseGeocodeStatus.Failure,
                ErrorCode: "invalid-place-path",
                ErrorMessage: exception.Message);
        }
    }

    private static string? ReadString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static void AddDistinctSegment(ICollection<string> segments, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string trimmed = value.Trim();
        if (segments.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        segments.Add(trimmed);
    }

    public void Dispose()
    {
        _requestGate.Dispose();
    }
}
