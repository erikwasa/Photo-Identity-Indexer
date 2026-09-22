namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoListCollectionRequest(
    string Name,
    string[]? RevisionIds = null);

public sealed record PhotoListCollectionResponse(
    string Id,
    string Name,
    string[] RevisionIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PhotoListCollectionErrorResponse(
    string Error,
    string[]? RevisionIds = null);
