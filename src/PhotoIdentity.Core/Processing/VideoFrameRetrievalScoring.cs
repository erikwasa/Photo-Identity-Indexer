namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Read-only diagnostic scoring of sampled frame embeddings. Scores are
/// within a single CLIP embedding space and are NOT calibrated cross-video
/// relevance nor a deployed WI-0191 ranking policy.
/// </summary>
public sealed record VideoFrameRetrievalScores(
    double MaxCosine, double TopKMeanCosine, double PooledCosine,
    int Frames, int DistinctFrames);

public static class VideoFrameRetrievalScoring
{
    public const string PolicyVersion = "wi0190-frame-retrieval-diagnostic-v1";
    public const double DuplicateCosineThreshold = 0.98;

    public static VideoFrameRetrievalScores Compare(
        IReadOnlyList<IReadOnlyList<float>> frameEmbeddings,
        IReadOnlyList<float> queryEmbedding, int topK = 3)
    {
        ArgumentNullException.ThrowIfNull(frameEmbeddings);
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        if (frameEmbeddings.Count is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
            topK is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
            queryEmbedding.Count is < 1 or > VideoFrameEvidenceContract.MaximumEmbeddingDimensions)
            throw new ArgumentOutOfRangeException(nameof(frameEmbeddings),
                "Retrieval diagnostic needs a bounded nonempty CLIP batch and query.");

        double[] query = Normalize(queryEmbedding, queryEmbedding.Count);
        List<double[]> frames = frameEmbeddings.Select(
            v => Normalize(v, query.Length)).ToList();
        double[] scores = frames.Select(v => Dot(v, query)).OrderDescending().ToArray();

        List<double[]> distinct = [];
        foreach (double[] vector in frames)
        {
            if (!distinct.Any(prior => Dot(prior, vector) >= DuplicateCosineThreshold))
                distinct.Add(vector);
        }
        double[] centroid = new double[query.Length];
        foreach (double[] vector in distinct)
            for (int d = 0; d < centroid.Length; d++)
                centroid[d] += vector[d];
        double norm = Math.Sqrt(centroid.Sum(v => v * v));
        double pooled = norm > 1e-10
            ? centroid.Select((v, d) => v * query[d]).Sum() / norm
            : 0; // Opposing vectors cancel; no reliable pooled direction.
        int k = Math.Min(scores.Length, topK);
        return new(Math.Clamp(scores[0], -1, 1),
            Math.Clamp(scores.Take(k).Average(), -1, 1),
            Math.Clamp(pooled, -1, 1), frames.Count, distinct.Count);
    }

    private static double[] Normalize(IReadOnlyList<float> vector, int dimensions)
    {
        if (vector.Count != dimensions)
            throw new ArgumentException("All frame and query embeddings must share dimensions.");
        double[] data = vector.Select(x => (double)x).ToArray();
        if (data.Any(x => !double.IsFinite(x)))
            throw new ArgumentException("Embeddings cannot contain non-finite elements.");
        double norm = Math.Sqrt(data.Sum(x => x * x));
        if (norm <= 1e-10 || !double.IsFinite(norm))
            throw new ArgumentException("Embeddings require a positive finite norm.");
        return data.Select(x => x / norm).ToArray();
    }

    private static double Dot(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double value = 0;
        for (int i = 0; i < a.Count; i++) value += a[i] * b[i];
        return Math.Clamp(value, -1, 1);
    }
}
