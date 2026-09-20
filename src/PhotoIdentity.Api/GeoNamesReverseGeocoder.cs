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
        ? $"geonames-place-v3|{BaseUri.AbsoluteUri.ToLowerInvariant()}|primary=findNearbyPlaceName|fallback=countrySubdivision+countryCode|langPolicy=se-local-else-en|localCountry=true|style=FULL|maxRows=1"
        : $"geonames-place-v3|{BaseUri.AbsoluteUri.ToLowerInvariant()}|primary=findNearbyPlaceName|fallback=countrySubdivision+countryCode|lang={Language.ToLowerInvariant()}|localCountry=true|style=FULL|maxRows=1";
}

public sealed class GeoNamesReverseGeocoder : IReverseGeocoder, IDisposable
{
    private enum RequestKind
    {
        PopulatedPlace,
        AdministrativeSubdivision,
        Country,
    }

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
            return await ReverseGeocodeForLanguageAsync(
                query,
                _configuration.Language,
                cancellationToken);
        }

        ReverseGeocodeResponse local = await ReverseGeocodeForLanguageAsync(
            query,
            "local",
            cancellationToken);
        if (local.Status != ReverseGeocodeStatus.Success || local.Place is null)
        {
            return local;
        }

        if (string.Equals(local.Place.CountryCode, "SE", StringComparison.OrdinalIgnoreCase))
        {
            return local;
        }

        ReverseGeocodeResponse english = await ReverseGeocodeForLanguageAsync(
            query,
            "en",
            cancellationToken);
        return english with
        {
            ProviderRequestCount = local.ProviderRequestCount + english.ProviderRequestCount,
        };
    }

    private async Task<ReverseGeocodeResponse> ReverseGeocodeForLanguageAsync(
        ReverseGeocodeQuery query,
        string language,
        CancellationToken cancellationToken)
    {
        ReverseGeocodeResponse populatedPlace = await SendRequestAsync(
            query,
            language,
            RequestKind.PopulatedPlace,
            cancellationToken);
        if (populatedPlace.Status != ReverseGeocodeStatus.NoResult)
        {
            return populatedPlace;
        }

        ReverseGeocodeResponse administrative = await SendRequestAsync(
            query,
            language,
            RequestKind.AdministrativeSubdivision,
            cancellationToken);
        if (administrative.Status != ReverseGeocodeStatus.NoResult)
        {
            return administrative with
            {
                ProviderRequestCount =
                    populatedPlace.ProviderRequestCount +
                    administrative.ProviderRequestCount,
            };
        }

        ReverseGeocodeResponse country = await SendRequestAsync(
            query,
            language,
            RequestKind.Country,
            cancellationToken);
        return country with
        {
            ProviderRequestCount =
                populatedPlace.ProviderRequestCount +
                administrative.ProviderRequestCount +
                country.ProviderRequestCount,
        };
    }

    private async Task<ReverseGeocodeResponse> SendRequestAsync(
        ReverseGeocodeQuery query,
        string language,
        RequestKind requestKind,
        CancellationToken cancellationToken)
    {
        await WaitForRequestSlotAsync(cancellationToken);

        Uri requestUri = BuildRequestUri(query, language, requestKind);
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
            ReverseGeocodeResponse parsed = requestKind switch
            {
                RequestKind.PopulatedPlace => ParsePopulatedPlaceResponse(json),
                RequestKind.AdministrativeSubdivision => ParseAdministrativeResponse(json),
                RequestKind.Country => ParseCountryResponse(json),
                _ => throw new InvalidOperationException("Unsupported GeoNames request kind."),
            };
            return parsed with { ProviderRequestCount = 1 };
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

    private Uri BuildRequestUri(
        ReverseGeocodeQuery query,
        string language,
        RequestKind requestKind)
    {
        string endpointName = requestKind switch
        {
            RequestKind.PopulatedPlace => "findNearbyPlaceNameJSON",
            RequestKind.AdministrativeSubdivision => "countrySubdivisionJSON",
            RequestKind.Country => "countryCodeJSON",
            _ => throw new InvalidOperationException("Unsupported GeoNames request kind."),
        };
        Uri endpoint = new(_configuration.BaseUri, endpointName);
        List<string> parameters =
        [
            $"lat={Uri.EscapeDataString(query.Latitude.ToString("R", CultureInfo.InvariantCulture))}",
            $"lng={Uri.EscapeDataString(query.Longitude.ToString("R", CultureInfo.InvariantCulture))}",
        ];
        if (requestKind == RequestKind.PopulatedPlace)
        {
            parameters.Add("maxRows=1");
            parameters.Add("style=FULL");
            parameters.Add("localCountry=true");
        }

        parameters.Add($"lang={Uri.EscapeDataString(language)}");
        parameters.Add($"username={Uri.EscapeDataString(_configuration.Username!)}");
        return new UriBuilder(endpoint) { Query = string.Join("&", parameters) }.Uri;
    }

    private static ReverseGeocodeResponse ParsePopulatedPlaceResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ReverseGeocodeResponse? statusResponse = ParseProviderStatus(root);
        if (statusResponse is not null)
        {
            return statusResponse;
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

        return BuildPlaceResponse(
            segments,
            ReadIdentifier(item, "geonameId"),
            ReadString(item, "countryCode"),
            "populated-place");
    }

    private static ReverseGeocodeResponse ParseAdministrativeResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ReverseGeocodeResponse? statusResponse = ParseProviderStatus(root);
        if (statusResponse is not null)
        {
            return statusResponse.Status == ReverseGeocodeStatus.NoResult
                ? AdministrativeNoResult()
                : statusResponse;
        }

        string? country = ReadString(root, "countryName");
        if (string.IsNullOrWhiteSpace(country))
        {
            return AdministrativeNoResult();
        }

        List<string> segments = [];
        AddDistinctSegment(segments, country);
        AddDistinctSegment(segments, ReadString(root, "adminName1"));
        AddDistinctSegment(segments, ReadString(root, "adminName2"));
        AddDistinctSegment(segments, ReadString(root, "adminName3"));
        AddDistinctSegment(segments, ReadString(root, "adminName4"));

        return BuildPlaceResponse(
            segments,
            ReadIdentifier(root, "geonameId"),
            ReadString(root, "countryCode"),
            "administrative");
    }

    private static ReverseGeocodeResponse ParseCountryResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ReverseGeocodeResponse? statusResponse = ParseProviderStatus(root);
        if (statusResponse is not null)
        {
            return statusResponse.Status == ReverseGeocodeStatus.NoResult
                ? GeographyNoResult()
                : statusResponse;
        }

        string? country = ReadString(root, "countryName");
        if (string.IsNullOrWhiteSpace(country))
        {
            return GeographyNoResult();
        }

        return BuildPlaceResponse(
            [country],
            providerResultId: null,
            ReadString(root, "countryCode"),
            "country");
    }

    private static ReverseGeocodeResponse BuildPlaceResponse(
        IReadOnlyCollection<string> segments,
        string? providerResultId,
        string? countryCode,
        string resultKind)
    {
        try
        {
            PhotoPlacePath place = PhotoPlacePath.Parse(string.Join('/', segments));
            return ReverseGeocodeResponse.Succeeded(new ReverseGeocodePlace(
                place,
                providerResultId,
                countryCode));
        }
        catch (ArgumentException exception)
        {
            return new ReverseGeocodeResponse(
                ReverseGeocodeStatus.Failure,
                ErrorCode: $"invalid-{resultKind}-place-path",
                ErrorMessage: exception.Message);
        }
    }

    private static ReverseGeocodeResponse AdministrativeNoResult() =>
        new(
            ReverseGeocodeStatus.NoResult,
            ErrorCode: "administrative-no-result",
            ErrorMessage: "GeoNames returned no populated place and no usable administrative subdivision for these coordinates.");

    private static ReverseGeocodeResponse GeographyNoResult() =>
        new(
            ReverseGeocodeStatus.NoResult,
            ErrorCode: "geography-no-result",
            ErrorMessage: "GeoNames returned no populated place, administrative subdivision or country for these coordinates.");

    private static ReverseGeocodeResponse? ParseProviderStatus(JsonElement root)
    {
        if (!root.TryGetProperty("status", out JsonElement status))
        {
            return null;
        }

        int code = status.TryGetProperty("value", out JsonElement value) && value.TryGetInt32(out int parsed)
            ? parsed
            : -1;
        string? message = status.TryGetProperty("message", out JsonElement messageElement)
            ? messageElement.GetString()
            : null;
        return code switch
        {
            15 => new ReverseGeocodeResponse(
                ReverseGeocodeStatus.NoResult,
                ErrorCode: "15",
                ErrorMessage: message),
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

    private static string? ReadString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static string? ReadIdentifier(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }

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
