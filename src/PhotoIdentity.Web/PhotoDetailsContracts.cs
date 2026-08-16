namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoDetailsPersonResponse(
    string Id,
    string DisplayName,
    int ConfirmedFaceCount,
    bool ManualPresence);

public sealed record PhotoDetailsResponse(
    string RevisionId,
    string FileName,
    IReadOnlyList<PhotoDetailsPersonResponse> People);

public sealed record PhotoPersonMutationRequest(string PersonId);

public sealed record PhotoPersonErrorResponse(string Error);
