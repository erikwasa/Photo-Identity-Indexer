namespace PhotoIdentity.Web.Contracts;

/// <summary>
/// Video-only API contracts; intentionally separate from photo Search,
/// saved photo selections and Smart Collection scopes.
/// </summary>
public sealed record VideoSearchRequest(string Query, string? Mode = null, int Limit = 40);

public sealed record VideoSearchResultResponse(
    string RevisionId, string PosterUrl, long TimestampMilliseconds,
    string Caption, double CombinedScore, double? SemanticScore,
    double? CaptionScore, string[] Sources, string EvidenceKind,
    string SamplingPolicyVersion, string ClipModelId,
    string CaptionModelId, string CaptionPromptVersion);

public sealed record VideoSearchResponse(
    string Query, string Mode, bool SemanticModelAvailable,
    int CandidateVideos, bool CandidateLimitReached,
    double SearchMilliseconds, VideoSearchResultResponse[] Items);
