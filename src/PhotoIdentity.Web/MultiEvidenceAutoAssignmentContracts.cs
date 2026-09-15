namespace PhotoIdentity.Web.Contracts;

public sealed record IdentityMultiEvidenceAutoAssignmentPolicyResponse(
    string ModelId,
    string ModelHash,
    int Version,
    bool Enabled,
    string AlgorithmPolicyVersion,
    double MinimumTargetScore,
    double MinimumTargetMargin,
    int MinimumIndependentReferenceCount,
    double MinimumReferenceScore,
    string ClusterPolicyVersion,
    bool ClusterIncludeUnknown,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpdateIdentityMultiEvidenceAutoAssignmentPolicyRequest(
    bool Enabled,
    string Actor);
