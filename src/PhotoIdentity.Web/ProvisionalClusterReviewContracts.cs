namespace PhotoIdentity.Web.Contracts;

public sealed record ProvisionalClusterModelResponse(
    string ModelId,
    string ModelHash,
    int FaceCount);

public sealed record ProvisionalClusterRunResponse(
    string? RunId,
    string ModelId,
    string ModelHash,
    string PolicyVersion,
    string Algorithm,
    double? DistanceThreshold,
    int MinimumSamples,
    int? MinimumClusterSize,
    bool IncludeUnknown,
    string Status,
    bool IsActive,
    bool IsCurrent,
    int TargetCount,
    int ProcessedTargetCount,
    int ClusterCount,
    int NoiseCount,
    DateTimeOffset? RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? Error);

public sealed record ProvisionalClusterReviewGroupResponse(
    string RunId,
    string DerivedClusterKey,
    int MemberCount,
    int CoreCount,
    int BorderCount,
    double CoreShare,
    string Status,
    IReadOnlyList<string> RepresentativeFaceIds,
    IReadOnlyList<string> RepresentativeImageUrls);

public sealed record ProvisionalClusterReviewGroupPageResponse(
    IReadOnlyList<ProvisionalClusterReviewGroupResponse> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record ProvisionalClusterReviewMemberResponse(
    string RunId,
    string DerivedClusterKey,
    string FaceId,
    string Role,
    string ImageUrl,
    string DetailsUrl);

public sealed record ProvisionalClusterKnownPersonCandidateResponse(
    string PersonId,
    string DisplayName,
    int SupportCount,
    double SupportShare,
    int OrdinaryHighCount,
    int OrdinaryMediumCount,
    double MinimumScore,
    double MedianScore,
    double MaximumScore,
    double? MedianMargin);

public sealed record ProvisionalClusterKnownPersonAdvisoryResponse(
    string ClusterRunId,
    string ModelId,
    string ModelHash,
    string ClusterPolicyVersion,
    bool IncludeUnknown,
    string DerivedClusterKey,
    string AdvisoryPolicyVersion,
    int IdentitySuggestionPolicyVersion,
    int MemberCount,
    int CoreCount,
    double CoreShare,
    int InternalConflictCount,
    int RankedEvidenceCount,
    double RankedEvidenceCoverage,
    int QualifyingEvidenceCount,
    string Status,
    string Explanation,
    bool CanonicalAssignmentAllowed,
    ProvisionalClusterKnownPersonCandidateResponse? Candidate,
    ProvisionalClusterKnownPersonCandidateResponse? CompetingCandidate,
    DateTimeOffset EvaluatedAtUtc);

public sealed record ProvisionalClusterNotSameRequest(
    string AnchorFaceId,
    IReadOnlyList<string> OtherFaceIds,
    string Actor);

public sealed record ProvisionalClusterNotSameResponse(
    int RecordedCount,
    bool RefreshQueued,
    string Message);
