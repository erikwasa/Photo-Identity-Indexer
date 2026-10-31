using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// In-memory validation of a proposed frame-evidence batch. NOT a persistence
/// layer, a lease, a source authorization, or a current-record repository.
/// Future PostgreSQL publication must recheck current revision, exclusions,
/// control generation and job lease atomically with its commit.
/// </summary>
public sealed record VideoFrameEvidenceProvenance(
    AssetRevisionId RevisionId,
    string SamplingPolicyVersion,
    string ExtractionProfileVersion,
    string ModelId,
    string ModelSha256,
    string PreprocessingVersion)
{
    public string? CaptionModelId { get; init; }
    public string? CaptionModelSha256 { get; init; }
    public string? CaptionPromptVersion { get; init; }
}

public sealed record VideoFrameEvidenceItem(
    long TimestampMilliseconds,
    int? SceneIndex,
    string? Caption,
    IReadOnlyList<float>? Embedding);

public sealed record VideoFrameEvidenceBatch(
    VideoFrameEvidenceProvenance Provenance,
    IReadOnlyList<VideoFrameEvidenceItem> Items);

public sealed record VideoFrameEvidenceValidation(
    bool Accepted, string ReasonCode, int FrameCount);

public static class VideoFrameEvidenceContract
{
    // This limit is a policy decision for the *future* persisted vector shape,
    // not a claim that every existing image model returns this dimensionality.
    public const int MaximumEmbeddingDimensions = 2048;

    public static VideoFrameEvidenceValidation Validate(
        VideoFrameSamplingPlan plan, VideoFrameEvidenceBatch batch,
        VideoFrameEvidenceProvenance expectedProvenance,
        bool CurrentRevision, bool Excluded, bool FullyCompleted)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(batch.Provenance);
        ArgumentNullException.ThrowIfNull(expectedProvenance);
        ArgumentNullException.ThrowIfNull(batch.Items);
        VideoFrameEvidenceProvenance p = batch.Provenance;

        if (!CurrentRevision || Excluded)
            return Reject("revision-unavailable");
        if (!FullyCompleted)
            return Reject("incomplete-batch");
        if (p.RevisionId.IsEmpty || p != expectedProvenance ||
            p.SamplingPolicyVersion != plan.PolicyVersion ||
            !SafeVersion(p.ExtractionProfileVersion) ||
            !SafeVersion(p.PreprocessingVersion) ||
            !SafeVersion(p.ModelId) ||
            !IsDigest(p.ModelSha256) ||
            (p.CaptionModelId is not null || p.CaptionModelSha256 is not null ||
             p.CaptionPromptVersion is not null) &&
            (!SafeModelId(p.CaptionModelId) ||
             !IsDigest(p.CaptionModelSha256) ||
             !SafeVersion(p.CaptionPromptVersion)))
            return Reject("provenance-invalid");
        if (plan.Samples.Count is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
            batch.Items.Count != plan.Samples.Count)
            return Reject("frame-count-mismatch");

        bool hasCaption = false, hasVector = false;
        for (int i = 0; i < plan.Samples.Count; i++)
        {
            VideoFrameSample expected = plan.Samples[i];
            VideoFrameEvidenceItem? actual = batch.Items[i];
            if (actual is null || actual.TimestampMilliseconds != expected.TimestampMilliseconds ||
                actual.SceneIndex != expected.SceneIndex)
                return Reject("timestamp-mismatch");

            if (actual.Caption is { } caption)
            {
                if (caption.Length is < 1 or > 500 ||
                    caption != caption.Trim() ||
                    caption.Any(char.IsControl))
                    return Reject("caption-invalid");
                hasCaption = true;
            }
            if (actual.Embedding is { } embedding)
            {
                if (embedding.Count is < 1 or > MaximumEmbeddingDimensions ||
                    embedding.Any(v => !float.IsFinite(v)))
                    return Reject("embedding-invalid");
                // Never compare vectors of different dimensions within a
                // single evidence batch. CLIP similarity expects one space.
                if (batch.Items.Any(item => item?.Embedding is { } v &&
                     v.Count != embedding.Count))
                    return Reject("embedding-dimensions-mismatch");
                hasVector = true;
            }
            if (actual.Caption is null && actual.Embedding is null)
                return Reject("frame-evidence-empty");
        }
        if (!hasCaption || !hasVector)
            return Reject("missing-evidence-kind");
        return new(true, "validated-not-published", batch.Items.Count);
    }

    private static VideoFrameEvidenceValidation Reject(string code) => new(false, code, 0);

    private static bool SafeVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        !value.Contains("..", StringComparison.Ordinal) &&
        value[0] is not ('.' or '/') &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'
            or '-' or '_' or '.' or '/');

    private static bool SafeModelId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        !value.Contains("..", StringComparison.Ordinal) &&
        value[0] is not ('.' or '/') &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'
            or '-' or '_' or '.' or '/' or ':');

    private static bool IsDigest(string? digest) =>
        digest is { Length: 64 } &&
        digest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
