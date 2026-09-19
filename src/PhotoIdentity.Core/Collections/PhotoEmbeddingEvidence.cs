using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

/// <summary>
/// Regenerable whole-image embedding evidence for one immutable asset revision.
/// This is derived model evidence, not canonical photo metadata.
/// </summary>
public sealed record PhotoEmbeddingEvidence
{
    public const string Float32L2NormalizedEncoding = "float32-l2-normalized-v1";

    public PhotoEmbeddingEvidence(
        AssetRevisionId revisionId,
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        IReadOnlyList<float> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(preprocessingVersion);
        ArgumentNullException.ThrowIfNull(values);

        string normalizedHash = modelSha256.Trim().ToLowerInvariant();
        if (normalizedHash.Length != 64 || normalizedHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Whole-image embedding model SHA-256 must contain exactly 64 hexadecimal characters.",
                nameof(modelSha256));
        }

        if (values.Count == 0)
        {
            throw new ArgumentException(
                "Whole-image embedding vectors must contain at least one value.",
                nameof(values));
        }

        float[] vector = values.ToArray();
        if (vector.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException(
                "Whole-image embedding vectors must contain only finite values.",
                nameof(values));
        }

        double squaredNorm = vector.Sum(value => (double)value * value);
        if (!double.IsFinite(squaredNorm) || squaredNorm <= 0)
        {
            throw new ArgumentException(
                "Whole-image embedding vectors must have a positive finite norm.",
                nameof(values));
        }

        double norm = Math.Sqrt(squaredNorm);
        if (Math.Abs(norm - 1d) > 0.001d)
        {
            throw new ArgumentException(
                "Whole-image embedding vectors must be L2-normalized before evidence is created.",
                nameof(values));
        }

        RevisionId = revisionId;
        ModelId = modelId.Trim();
        ModelSha256 = normalizedHash;
        PreprocessingVersion = preprocessingVersion.Trim();
        Values = vector;
    }

    public AssetRevisionId RevisionId { get; }
    public string ModelId { get; }
    public string ModelSha256 { get; }
    public string PreprocessingVersion { get; }
    public string VectorEncoding => Float32L2NormalizedEncoding;
    public IReadOnlyList<float> Values { get; }
    public int Dimensions => Values.Count;
    public int StorageBytes => checked(Dimensions * sizeof(float));
}

public static class PhotoEmbeddingSimilarity
{
    public static double Cosine(
        IReadOnlyList<float> left,
        IReadOnlyList<float> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count == 0 || left.Count != right.Count)
        {
            throw new ArgumentException(
                "Embedding vectors must have the same positive dimension.");
        }

        double dot = 0;
        double leftSquared = 0;
        double rightSquared = 0;
        for (int index = 0; index < left.Count; index++)
        {
            float leftValue = left[index];
            float rightValue = right[index];
            if (!float.IsFinite(leftValue) || !float.IsFinite(rightValue))
            {
                throw new ArgumentException("Embedding vectors must contain only finite values.");
            }

            dot += (double)leftValue * rightValue;
            leftSquared += (double)leftValue * leftValue;
            rightSquared += (double)rightValue * rightValue;
        }

        if (leftSquared <= 0 || rightSquared <= 0)
        {
            throw new ArgumentException("Embedding vectors must have a positive norm.");
        }

        double similarity = dot / Math.Sqrt(leftSquared * rightSquared);
        return Math.Clamp(similarity, -1d, 1d);
    }
}
