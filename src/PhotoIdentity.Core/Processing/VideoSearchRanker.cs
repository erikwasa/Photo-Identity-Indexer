using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Core.Processing;

/// <summary>Published, already-authorized frame evidence; never a source-file or processing request.</summary>
public sealed record VideoSearchFrame(long TimestampMilliseconds, string Caption, float[] Embedding);

public sealed record VideoSearchCandidate(
    Guid RevisionId, string SamplingPolicyVersion, string ClipModelId,
    string ClipModelSha256, string ClipPreprocessingVersion,
    string CaptionModelId, string CaptionPromptVersion,
    IReadOnlyList<VideoSearchFrame> Frames);

public sealed record VideoSearchHit(
    Guid RevisionId, long TimestampMilliseconds, string Caption,
    double CombinedScore, double? SemanticScore, double? CaptionScore,
    string[] Sources, string SamplingPolicyVersion, string ClipModelId,
    string CaptionModelId, string CaptionPromptVersion);

/// <summary>
/// Exact bounded in-memory frame ranking. Reciprocal-rank fusion happens AFTER
/// reducing each evidence stream to one hit per video, so multiple sampled
/// frames never give a long video extra fusion votes. This is not a calibrated
/// cross-media score or a new index.
/// </summary>
public static class VideoSearchRanker
{
    private const double RankOffset = 60d;

    public static IReadOnlyList<VideoSearchHit> Rank(
        IReadOnlyList<VideoSearchCandidate> videos, string query, string? mode,
        int limit, IReadOnlyList<float>? textEmbedding = null,
        string? clipModelId = null, string? clipModelSha256 = null,
        string? clipPreprocessingVersion = null)
    {
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Trim().Length > 300 || limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(query), "Query or result bound is invalid.");
        string selectedMode = PhotoSearchModes.Normalize(mode);
        string[] terms = Tokens(query).Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length == 0)
            throw new ArgumentException("Search query needs at least one word.", nameof(query));

        bool visual = selectedMode is PhotoSearchModes.Combined or PhotoSearchModes.Semantic
            && textEmbedding is { Count: > 0 }
            && !string.IsNullOrEmpty(clipModelId)
            && !string.IsNullOrEmpty(clipModelSha256)
            && !string.IsNullOrEmpty(clipPreprocessingVersion);
        List<Match> matches = [];
        HashSet<Guid> seen = [];
        foreach (VideoSearchCandidate video in videos)
        {
            if (video.RevisionId == Guid.Empty || !seen.Add(video.RevisionId) ||
                video.Frames is null || video.Frames.Count is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames)
                throw new ArgumentException("Search requires unique complete bounded video candidates.");

            VideoSearchFrame? semanticFrame = null, captionFrame = null;
            double? semantic = null, caption = null;
            long previous = -1;
            bool compatible = visual &&
                video.ClipModelId == clipModelId &&
                video.ClipModelSha256 == clipModelSha256 &&
                video.ClipPreprocessingVersion == clipPreprocessingVersion;
            foreach (VideoSearchFrame frame in video.Frames)
            {
                if (frame.TimestampMilliseconds <= previous || frame.Caption is null ||
                    frame.Caption.Length is < 1 or > 500)
                    throw new ArgumentException("Video frame order or caption is invalid.");
                previous = frame.TimestampMilliseconds;

                if (selectedMode != PhotoSearchModes.Semantic)
                {
                    string[] words = Tokens(frame.Caption).ToArray();
                    int matched = terms.Count(t => words.Contains(t, StringComparer.Ordinal));
                    if (matched > 0)
                    {
                        double score = (double)matched / terms.Length;
                        if (frame.Caption.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                            score += 1d;
                        if (!caption.HasValue || score > caption.Value)
                        {
                            caption = score;
                            captionFrame = frame;
                        }
                    }
                }

                if (compatible)
                {
                    if (frame.Embedding is null ||
                        frame.Embedding.Length != textEmbedding!.Count)
                        continue;
                    double score;
                    try
                    {
                        score = VideoFrameRetrievalScoring.Compare(
                            [frame.Embedding], textEmbedding).MaxCosine;
                    }
                    catch (ArgumentException)
                    {
                        continue; // Corrupt or mismatched vector never becomes a result.
                    }
                    if (!semantic.HasValue || score > semantic.Value)
                    {
                        semantic = score;
                        semanticFrame = frame;
                    }
                }
            }
            if (semantic.HasValue || caption.HasValue)
                matches.Add(new Match(video, semanticFrame, semantic,
                    captionFrame, caption));
        }

        var fusion = matches.ToDictionary(x => x.Video.RevisionId,
            x => new Score(x));
        if (selectedMode != PhotoSearchModes.Caption)
        {
            int rank = 0;
            foreach (Match item in matches.Where(x => x.Semantic.HasValue)
                         .OrderByDescending(x => x.Semantic!.Value)
                         .ThenBy(x => x.Video.RevisionId))
                fusion[item.Video.RevisionId].AddSemantic(++rank);
        }
        if (selectedMode != PhotoSearchModes.Semantic)
        {
            int rank = 0;
            foreach (Match item in matches.Where(x => x.Caption.HasValue)
                         .OrderByDescending(x => x.Caption!.Value)
                         .ThenBy(x => x.Video.RevisionId))
                fusion[item.Video.RevisionId].AddCaption(++rank);
        }
        return fusion.Values.OrderByDescending(x => x.Fused)
            .ThenBy(x => x.Match.Video.RevisionId).Take(limit)
            .Select(x =>
            {
                Match match = x.Match;
                // Caption evidence wins the representative frame in a combined
                // result when it actually contributed, otherwise use the visual match.
                VideoSearchFrame frame = x.CaptionRank > 0
                    ? match.CaptionFrame! : match.SemanticFrame!;
                return new VideoSearchHit(
                    match.Video.RevisionId, frame.TimestampMilliseconds,
                    frame.Caption, x.Fused, x.SemanticRank > 0 ? match.Semantic : null,
                    x.CaptionRank > 0 ? match.Caption : null,
                    (x.SemanticRank > 0, x.CaptionRank > 0) switch
                    {
                        (true, true) => ["semantic", "caption"],
                        (true, false) => ["semantic"],
                        _ => ["caption"]
                    },
                    match.Video.SamplingPolicyVersion, match.Video.ClipModelId,
                    match.Video.CaptionModelId, match.Video.CaptionPromptVersion);
            }).ToArray();
    }

    private static IEnumerable<string> Tokens(string value) =>
        value.ToLowerInvariant().Split(
            value.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(),
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record Match(
        VideoSearchCandidate Video, VideoSearchFrame? SemanticFrame, double? Semantic,
        VideoSearchFrame? CaptionFrame, double? Caption);
    private sealed class Score(Match match)
    {
        public Match Match { get; } = match;
        public double Fused { get; private set; }
        public int SemanticRank { get; private set; }
        public int CaptionRank { get; private set; }
        public void AddSemantic(int rank)
        {
            SemanticRank = rank;
            Fused += 1d / (RankOffset + rank);
        }
        public void AddCaption(int rank)
        {
            CaptionRank = rank;
            Fused += 1d / (RankOffset + rank);
        }
    }
}
