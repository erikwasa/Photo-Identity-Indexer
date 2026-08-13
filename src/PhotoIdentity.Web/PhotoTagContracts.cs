namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoTagMutationRequest(string Name);

public sealed record PhotoTagResponse(
    string Name,
    string Source,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PhotoTagErrorResponse(string Error);
