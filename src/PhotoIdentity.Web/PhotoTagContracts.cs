namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoTagMutationRequest(string Value);

public sealed record PhotoTagDefinitionResponse(
    string Id,
    string Name,
    string Value,
    string? ParentId,
    string? ParentValue,
    string? Color);

public sealed record PhotoTagResponse(
    string Id,
    string Name,
    string Value,
    string? ParentId,
    string? ParentValue,
    string? Color,
    string Source,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PhotoTagErrorResponse(string Error);
