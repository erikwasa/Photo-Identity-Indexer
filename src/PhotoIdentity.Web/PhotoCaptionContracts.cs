namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoCaptionEnrichmentSettingsRequest(
    bool Enabled,
    string Language);

public sealed record PhotoCaptionEnrichmentStatusResponse(
    bool Enabled,
    string Language,
    string State,
    string Message,
    DateTimeOffset? LastActivityAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string Model,
    string GenerationVersion);

public sealed record PhotoCaptionResponse(
    string RevisionId,
    string Language,
    string Status,
    string? Caption,
    DateTimeOffset? GeneratedAtUtc);
