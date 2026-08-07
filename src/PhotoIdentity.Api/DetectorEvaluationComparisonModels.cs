namespace PhotoIdentity.Api;

internal static class DetectorEvaluationComparisonKinds
{
    public const string Unmatched = "unmatched";
    public const string Duplicate = "duplicate";
    public const string Ambiguous = "ambiguous";
}

internal sealed class StoredDetectorEvaluationComparison
{
    public int SchemaVersion { get; init; } = DetectorEvaluationComparisonStore.CurrentSchemaVersion;
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid BaselineSessionId { get; init; }
    public string BaselineName { get; init; } = string.Empty;
    public DateTimeOffset GroundTruthFrozenAtUtc { get; init; }
    public string CandidateProcessingRunId { get; init; } = string.Empty;
    public double IouThreshold { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool? MaterialCategoryFailure { get; set; }
    public string? GateNotes { get; set; }
    public List<StoredDetectorEvaluationComparisonPhoto> Photos { get; init; } = [];
}

internal sealed class StoredDetectorEvaluationComparisonPhoto
{
    public string CandidateRevisionId { get; init; } = string.Empty;
    public string RevisionSha256 { get; init; } = string.Empty;
    public string PhotoName { get; init; } = string.Empty;
    public string SampleId { get; init; } = string.Empty;
    public string SampleGroup { get; init; } = string.Empty;
    public string SourceGroup { get; init; } = string.Empty;
    public string PrimaryCategory { get; init; } = string.Empty;
    public int CountableFaces { get; init; }
    public List<StoredDetectorGroundTruthFace> GroundTruthFaces { get; init; } = [];
    public List<StoredDetectorEvaluationCandidateDetection> CandidateDetections { get; init; } = [];
    public List<StoredDetectorEvaluationAutomaticMatch> AutomaticMatches { get; init; } = [];
    public List<StoredDetectorEvaluationExceptionComponent> ExceptionComponents { get; init; } = [];
    public StoredDetectorEvaluationManualCorrection Correction { get; set; } = new();
}

internal sealed class StoredDetectorEvaluationCandidateDetection
{
    public string Id { get; init; } = string.Empty;
    public int FaceNumber { get; init; }
    public double Confidence { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

internal sealed class StoredDetectorEvaluationAutomaticMatch
{
    public string GroundTruthFaceId { get; init; } = string.Empty;
    public string CandidateDetectionId { get; init; } = string.Empty;
    public double Iou { get; init; }
}

internal sealed class StoredDetectorEvaluationExceptionComponent
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public List<string> GroundTruthFaceIds { get; init; } = [];
    public List<string> CandidateDetectionIds { get; init; } = [];
}

internal sealed class StoredDetectorEvaluationManualCorrection
{
    public List<StoredDetectorEvaluationManualMatch> Matches { get; init; } = [];
    public List<string> FalseCandidateDetectionIds { get; init; } = [];
    public List<string> DuplicateCandidateDetectionIds { get; init; } = [];
    public List<string> NeutralCandidateDetectionIds { get; init; } = [];
    public List<string> MissedGroundTruthFaceIds { get; init; } = [];
    public string? Notes { get; init; }
}

internal sealed class StoredDetectorEvaluationManualMatch
{
    public string GroundTruthFaceId { get; init; } = string.Empty;
    public string CandidateDetectionId { get; init; } = string.Empty;
}

internal sealed record DetectorEvaluationComparisonSeed(
    string Name,
    Guid BaselineSessionId,
    string BaselineName,
    DateTimeOffset GroundTruthFrozenAtUtc,
    string CandidateProcessingRunId,
    double IouThreshold,
    IReadOnlyList<StoredDetectorEvaluationComparisonPhoto> Photos);

internal sealed record DetectorEvaluationComparisonCorrectionUpdate(
    IReadOnlyList<StoredDetectorEvaluationManualMatch> Matches,
    IReadOnlyList<string> FalseCandidateDetectionIds,
    IReadOnlyList<string> DuplicateCandidateDetectionIds,
    IReadOnlyList<string> MissedGroundTruthFaceIds,
    string? Notes)
{
    public IReadOnlyList<string> NeutralCandidateDetectionIds { get; init; } = [];
}
