namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Operator-private, offline CLIP evaluation inputs. IDs must be opaque UUIDs.
/// The model fingerprint and preprocessing labels are assertions supplied by
/// the operator; this offline evaluator does NOT authenticate model files.
/// </summary>
public sealed record VideoFrameRetrievalEvaluationManifest(
    string Protocol,
    string ClipModelSha256,
    string ImagePreprocessingVersion,
    string TextPreprocessingVersion,
    VideoFrameRetrievalEvaluationClip[] Clips,
    VideoFrameRetrievalEvaluationQuery[] Queries);

public sealed record VideoFrameRetrievalEvaluationClip(
    Guid ClipId, float[][] FrameEmbeddings);

public sealed record VideoFrameRetrievalEvaluationQuery(
    Guid QueryId, float[] TextEmbedding, Guid[] RelevantClipIds);

public sealed record VideoFrameRetrievalRankingMetrics(
    double HitRateAtK, double MeanReciprocalRankAtK,
    double MeanRecallAtK, double MeanAveragePrecisionAtK,
    double MeanNdcgAtK);

public sealed record VideoFrameRetrievalEvaluationResult(
    int Clips, int Queries, int RelevantJudgments,
    int FrameVectors, int EmbeddingDimensions,
    int FrameTopK, int RankingK,
    VideoFrameRetrievalRankingMetrics Maximum,
    VideoFrameRetrievalRankingMetrics TopKMean,
    VideoFrameRetrievalRankingMetrics DeduplicatedPooled);

/// <summary>
/// Pure, bounded evaluation of three *existing* diagnostic policies against
/// operator-judged relevant clips. No search index, ranker deployment, model,
/// media, file or network access. Reports only corpus-level numeric metrics.
/// </summary>
public static class VideoFrameRetrievalEvaluation
{
    public const string ManifestProtocol = "wi0190-retrieval-manifest-v1";
    public const string ReportProtocol = "wi0190-retrieval-evaluation-v1";
    public const int MaximumClips = 32;
    public const int MaximumQueries = 32;

    public static VideoFrameRetrievalEvaluationResult Evaluate(
        VideoFrameRetrievalEvaluationManifest manifest,
        int frameTopK = 3, int rankingK = 5)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Protocol != ManifestProtocol ||
            manifest.ClipModelSha256 is not { Length: 64 } ||
            manifest.ClipModelSha256.Any(c => c is not
                (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            !SafeVersion(manifest.ImagePreprocessingVersion) ||
            !SafeVersion(manifest.TextPreprocessingVersion))
            throw new ArgumentException("Retrieval manifest provenance is invalid.");
        if (manifest.Clips is null ||
            manifest.Clips.Length is < 2 or > MaximumClips ||
            manifest.Queries is null ||
            manifest.Queries.Length is < 1 or > MaximumQueries ||
            frameTopK is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
            rankingK is < 1 or > MaximumClips)
            throw new ArgumentOutOfRangeException(nameof(manifest),
                "Retrieval evaluation exceeds bounded dataset or ranking limits.");

        int dimensions = 0, frameCount = 0;
        HashSet<Guid> clipIds = [];
        foreach (VideoFrameRetrievalEvaluationClip clip in manifest.Clips)
        {
            if (clip is null || clip.ClipId == Guid.Empty ||
                !clipIds.Add(clip.ClipId) ||
                clip.FrameEmbeddings is null ||
                clip.FrameEmbeddings.Length is < 1 or
                    > VideoFrameSamplingPlanner.HardMaximumFrames)
                throw new ArgumentException("Retrieval clips must be unique and bounded.");
            foreach (float[] frame in clip.FrameEmbeddings)
            {
                if (frame is null || frame.Length is < 1 or
                    > VideoFrameEvidenceContract.MaximumEmbeddingDimensions ||
                    (dimensions != 0 && dimensions != frame.Length))
                    throw new ArgumentException("Retrieval frame dimensions do not match.");
                dimensions = frame.Length;
                RequireUsableVector(frame);
                frameCount++;
            }
        }

        HashSet<Guid> queryIds = [];
        int relevantJudgments = 0;
        foreach (VideoFrameRetrievalEvaluationQuery query in manifest.Queries)
        {
            if (query is null || query.QueryId == Guid.Empty ||
                !queryIds.Add(query.QueryId) ||
                query.TextEmbedding is null ||
                query.TextEmbedding.Length != dimensions ||
                query.RelevantClipIds is null ||
                query.RelevantClipIds.Length is < 1 or > MaximumClips)
                throw new ArgumentException("Retrieval query identity or dimensions are invalid.");
            RequireUsableVector(query.TextEmbedding);
            HashSet<Guid> relevance = [];
            foreach (Guid relevant in query.RelevantClipIds)
                if (relevant == Guid.Empty ||
                    !clipIds.Contains(relevant) || !relevance.Add(relevant))
                    throw new ArgumentException("Retrieval relevance IDs must be unique corpus clips.");
            relevantJudgments += relevance.Count;
        }

        int effectiveRankK = Math.Min(rankingK, manifest.Clips.Length);
        double[] max = new double[5], top = new double[5], pooled = new double[5];
        foreach (VideoFrameRetrievalEvaluationQuery query in manifest.Queries)
        {
            var ranked = manifest.Clips.Select(clip =>
            {
                // Preserve the existing per-video diagnostic policies without
                // introducing unreviewed search ranking semantics.
                VideoFrameRetrievalScores scores = VideoFrameRetrievalScoring.Compare(
                    clip.FrameEmbeddings, query.TextEmbedding, frameTopK);
                return (clip.ClipId, scores);
            }).ToArray();
            HashSet<Guid> relevant = query.RelevantClipIds.ToHashSet();
            Accumulate(max, ranked
                .OrderByDescending(x => x.scores.MaxCosine)
                .ThenBy(x => x.ClipId)
                .Take(effectiveRankK).Select(x => x.ClipId), relevant,
                effectiveRankK);
            Accumulate(top, ranked
                .OrderByDescending(x => x.scores.TopKMeanCosine)
                .ThenBy(x => x.ClipId)
                .Take(effectiveRankK).Select(x => x.ClipId), relevant,
                effectiveRankK);
            Accumulate(pooled, ranked
                .OrderByDescending(x => x.scores.PooledCosine)
                .ThenBy(x => x.ClipId)
                .Take(effectiveRankK).Select(x => x.ClipId), relevant,
                effectiveRankK);
        }

        VideoFrameRetrievalRankingMetrics Average(double[] sums)
        {
            double Round(double v) => Math.Round(v / manifest.Queries.Length,
                6, MidpointRounding.AwayFromZero);
            return new(Round(sums[0]), Round(sums[1]), Round(sums[2]),
                Round(sums[3]), Round(sums[4]));
        }
        return new(manifest.Clips.Length, manifest.Queries.Length,
            relevantJudgments, frameCount, dimensions, frameTopK,
            effectiveRankK, Average(max), Average(top), Average(pooled));
    }

    private static void Accumulate(double[] sum, IEnumerable<Guid> ranked,
        HashSet<Guid> relevant, int k)
    {
        int hits = 0, rank = 0;
        double averagePrecision = 0, discountedGain = 0;
        foreach (Guid clip in ranked)
        {
            rank++;
            if (!relevant.Contains(clip)) continue;
            hits++;
            if (hits == 1) sum[1] += 1d / rank; // reciprocal rank
            averagePrecision += (double)hits / rank;
            discountedGain += 1d / Math.Log2(rank + 1);
        }
        if (hits != 0) sum[0] += 1; // any relevant clip in top-k
        sum[2] += (double)hits / relevant.Count; // recall@k
        sum[3] += averagePrecision / Math.Min(k, relevant.Count);
        double idealGain = Enumerable.Range(1, Math.Min(k, relevant.Count))
            .Sum(r => 1d / Math.Log2(r + 1));
        sum[4] += discountedGain / idealGain;
    }

    private static void RequireUsableVector(float[] vector)
    {
        double squared = 0;
        foreach (float value in vector)
        {
            if (!float.IsFinite(value))
                throw new ArgumentException("CLIP vectors must contain finite numbers.");
            squared += (double)value * value;
        }
        if (squared <= 1e-20 || !double.IsFinite(squared))
            throw new ArgumentException("CLIP vectors must have positive finite norm.");
    }

    private static bool SafeVersion(string? value) =>
        value is { Length: >= 1 and <= 100 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'
            or '-' or '_' or '.');
}
