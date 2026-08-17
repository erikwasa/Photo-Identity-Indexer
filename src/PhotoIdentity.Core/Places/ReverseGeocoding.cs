namespace PhotoIdentity.Core.Places;

public sealed record ReverseGeocodeQuery(double Latitude, double Longitude)
{
    public ReverseGeocodeQuery Validate()
    {
        if (!double.IsFinite(Latitude) || Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(Latitude), "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(Longitude) || Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(Longitude), "Longitude must be between -180 and 180.");
        }

        return this;
    }
}

public sealed record ReverseGeocodePlace(
    PhotoPlacePath Place,
    string? ProviderResultId,
    string? CountryCode);

public enum ReverseGeocodeStatus
{
    Success,
    NoResult,
    Deferred,
    Failure,
}

public sealed record ReverseGeocodeResponse(
    ReverseGeocodeStatus Status,
    ReverseGeocodePlace? Place = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool StopBatch = false)
{
    public static ReverseGeocodeResponse Succeeded(ReverseGeocodePlace place) =>
        new(ReverseGeocodeStatus.Success, place);
}

public interface IReverseGeocoder
{
    string ProviderName { get; }

    string ContractKey { get; }

    Task<ReverseGeocodeResponse> ReverseGeocodeAsync(
        ReverseGeocodeQuery query,
        CancellationToken cancellationToken = default);
}
