namespace PhotoIdentity.Web.Contracts;

public sealed record SmartCollectionLocationRequest(
    string? Place = null,
    double? South = null,
    double? West = null,
    double? North = null,
    double? East = null)
{
    public SmartCollectionLocationRequest(double south, double west, double north, double east)
        : this(null, south, west, north, east)
    {
    }
}

public sealed record SmartCollectionDefinitionRequest(
    string Name,
    string[]? People = null,
    string? PeopleMatch = null,
    string[]? Tags = null,
    string? TagMatch = null,
    SmartCollectionLocationRequest? Location = null,
    string? Taken = null);

public sealed record SmartCollectionQueryRequest(
    string[]? People = null,
    string? PeopleMatch = null,
    string[]? Tags = null,
    string? TagMatch = null,
    SmartCollectionLocationRequest? Location = null,
    string? Taken = null,
    int Offset = 0,
    int Limit = 40);

public sealed record SmartCollectionDateRangeResponse(string From, string To);
public sealed record SmartCollectionFilterResponse(
    string[] People,
    string PeopleMatch,
    string[] Tags,
    string TagMatch,
    SmartCollectionLocationRequest? Location,
    SmartCollectionDateRangeResponse? Taken);
public sealed record SmartCollectionDefinitionResponse(string Id, string Name, SmartCollectionFilterResponse Filter, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record SmartCollectionPhotoResponse(
    string RevisionId, string AssetId, string ThumbnailUrl, string PreviewUrl, string OriginalUrl,
    DateTimeOffset ObservedAtUtc, string? MediaType, int? Width, int? Height, DateTime? TakenAtLocal,
    double? Latitude, double? Longitude);
public sealed record SmartCollectionPageResponse(
    SmartCollectionPhotoResponse[] Items, int Offset, int Limit, int Total,
    SmartCollectionFilterResponse Filter, string? CollectionId = null, string? CollectionName = null);
public sealed record SmartCollectionErrorResponse(string Error);
