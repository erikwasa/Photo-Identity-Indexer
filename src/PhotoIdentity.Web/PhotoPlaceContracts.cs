namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoPlaceMutationRequest(string Value);

public sealed record PhotoPlaceDefinitionResponse(
    string Id,
    string Name,
    string Value,
    string? ParentId,
    string? ParentValue);

public sealed record PhotoPlaceResponse(
    string Id,
    string Name,
    string Value,
    string Source,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PhotoPlaceStateResponse(
    string RevisionId,
    PhotoPlaceResponse? Place,
    PhotoPlaceMigrationConflictResponse? MigrationConflict);

public sealed record PhotoPlaceMigrationConflictResponse(
    string RevisionId,
    IReadOnlyList<string> CandidateValues,
    DateTimeOffset DetectedAtUtc);

public sealed record PhotoPlaceErrorResponse(string Error);
