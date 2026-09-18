namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoPresentationPreferenceMutationRequest(string Preference);

public sealed record PhotoPresentationPreferenceActionResponse(
    long Id,
    string ActionKind,
    string? Preference,
    string Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record PhotoPresentationPreferenceResponse(
    string RevisionId,
    string? Preference,
    IReadOnlyList<PhotoPresentationPreferenceActionResponse> History);
