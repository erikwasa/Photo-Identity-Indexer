using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public static class PhotoSearchModes
{
    public const string Combined = "combined";
    public const string Semantic = "semantic";
    public const string Caption = "caption";

    public static string Normalize(string? value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? Combined;
        return normalized switch
        {
            Combined => Combined,
            Semantic => Semantic,
            Caption => Caption,
            _ => throw new ArgumentException(
                "Photo search mode must be 'combined', 'semantic' or 'caption'.",
                nameof(value)),
        };
    }
}

public sealed record PhotoSearchEmbeddingRecord(
    PhotoEmbeddingEvidence Evidence,
    double GenerationMilliseconds,
    DateTimeOffset GeneratedAtUtc);

public sealed record PhotoSearchCaptionHit(
    AssetRevisionId RevisionId,
    string Language,
    string Content,
    double Score);

public sealed record PhotoSearchSemanticHit(
    AssetRevisionId RevisionId,
    double Score);

public sealed record PhotoSearchRankedHit(
    AssetRevisionId RevisionId,
    double CombinedScore,
    double? SemanticScore,
    double? CaptionScore,
    string? CaptionLanguage,
    string? Caption,
    IReadOnlyList<string> Sources);

public sealed record PhotoSearchCatalogueStatistics(
    int CurrentPhotoCount,
    int DisplayableCaptionCount);

public sealed record PhotoSearchStorageStatistics(
    int CurrentPhotoCount,
    int EmbeddingCount,
    int DisplayableCaptionCount,
    int? EmbeddingDimensions,
    long RawEmbeddingBytes,
    double? AverageEmbeddingGenerationMilliseconds);

/// <summary>
/// Persists regenerable semantic-search evidence and exposes bounded caption retrieval.
/// Whole-image vectors remain derived model evidence and never become canonical photo metadata.
/// </summary>
public interface IPhotoSearchRepository
{
    Task<IReadOnlyList<AssetRevisionId>> GetEmbeddingCandidatesAsync(
        string reviewProxyProfileId,
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding,
        int limit,
        CancellationToken cancellationToken = default);

    Task SaveEmbeddingAsync(
        PhotoEmbeddingEvidence evidence,
        double generationMilliseconds,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoSearchEmbeddingRecord>> GetCurrentEmbeddingsAsync(
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoSearchCaptionHit>> SearchCaptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);

    Task<PhotoSearchCatalogueStatistics> GetCatalogueStatisticsAsync(
        CancellationToken cancellationToken = default);

    Task<PhotoSearchStorageStatistics> GetStatisticsAsync(
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Combines independently ranked semantic and caption evidence using reciprocal-rank fusion.
/// Component scores are retained for explanation; they are deliberately not calibrated onto one
/// pretend-common scale.
/// </summary>
public static class PhotoSearchRanker
{
    private const double ReciprocalRankOffset = 60d;

    public static IReadOnlyList<PhotoSearchRankedHit> Fuse(
        IReadOnlyList<PhotoSearchSemanticHit> semantic,
        IReadOnlyList<PhotoSearchCaptionHit> captions,
        string mode,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(semantic);
        ArgumentNullException.ThrowIfNull(captions);
        string normalizedMode = PhotoSearchModes.Normalize(mode);
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        Dictionary<AssetRevisionId, MutableRankedHit> combined = [];

        if (normalizedMode is PhotoSearchModes.Combined or PhotoSearchModes.Semantic)
        {
            int rank = 0;
            foreach (PhotoSearchSemanticHit hit in semantic
                         .OrderByDescending(item => item.Score)
                         .ThenBy(item => item.RevisionId.ToString(), StringComparer.Ordinal))
            {
                rank++;
                MutableRankedHit entry = GetOrCreate(combined, hit.RevisionId);
                entry.SemanticScore = hit.Score;
                entry.CombinedScore += 1d / (ReciprocalRankOffset + rank);
            }
        }

        if (normalizedMode is PhotoSearchModes.Combined or PhotoSearchModes.Caption)
        {
            int rank = 0;
            foreach (PhotoSearchCaptionHit hit in captions
                         .OrderByDescending(item => item.Score)
                         .ThenBy(item => item.RevisionId.ToString(), StringComparer.Ordinal))
            {
                rank++;
                MutableRankedHit entry = GetOrCreate(combined, hit.RevisionId);
                if (!entry.CaptionScore.HasValue || hit.Score > entry.CaptionScore.Value)
                {
                    entry.CaptionScore = hit.Score;
                    entry.CaptionLanguage = hit.Language;
                    entry.Caption = hit.Content;
                }
                entry.CombinedScore += 1d / (ReciprocalRankOffset + rank);
            }
        }

        return combined.Values
            .OrderByDescending(item => item.CombinedScore)
            .ThenByDescending(item => item.SemanticScore ?? double.NegativeInfinity)
            .ThenByDescending(item => item.CaptionScore ?? double.NegativeInfinity)
            .ThenBy(item => item.RevisionId.ToString(), StringComparer.Ordinal)
            .Take(limit)
            .Select(item => new PhotoSearchRankedHit(
                item.RevisionId,
                item.CombinedScore,
                item.SemanticScore,
                item.CaptionScore,
                item.CaptionLanguage,
                item.Caption,
                BuildSources(item)))
            .ToArray();
    }

    private static MutableRankedHit GetOrCreate(
        IDictionary<AssetRevisionId, MutableRankedHit> items,
        AssetRevisionId revisionId)
    {
        if (!items.TryGetValue(revisionId, out MutableRankedHit? hit))
        {
            hit = new MutableRankedHit(revisionId);
            items.Add(revisionId, hit);
        }
        return hit;
    }

    private static string[] BuildSources(MutableRankedHit item)
    {
        List<string> sources = [];
        if (item.SemanticScore.HasValue)
        {
            sources.Add("semantic");
        }
        if (item.CaptionScore.HasValue)
        {
            sources.Add("caption");
        }
        return sources.ToArray();
    }

    private sealed class MutableRankedHit(AssetRevisionId revisionId)
    {
        public AssetRevisionId RevisionId { get; } = revisionId;
        public double CombinedScore { get; set; }
        public double? SemanticScore { get; set; }
        public double? CaptionScore { get; set; }
        public string? CaptionLanguage { get; set; }
        public string? Caption { get; set; }
    }
}
