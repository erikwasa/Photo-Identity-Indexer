using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Private, operator-chosen reference stills. Aliases intentionally cannot contain real names.
/// Neither the reference manifest nor its photographs are uploaded, persisted or shared.
/// </summary>
public sealed record VideoFaceReference(string Alias, string File);
public sealed record VideoFaceReferenceSet(int SchemaVersion, List<VideoFaceReference>? References);

public sealed record VideoFaceReferenceFinding(
    int FrameNumber,
    long TimestampMs,
    int FaceNumber,
    string? CandidateAlias,
    double? Similarity,
    double? RunnerUpSimilarity,
    double? ScoreGap,
    string ReviewStatus);

public sealed record VideoFacePrivateReview(
    int SchemaVersion,
    int ReferenceCount,
    VideoFaceAggregate Aggregate,
    IReadOnlyList<VideoFaceReferenceFinding> Findings);

public sealed record VideoFaceReferenceEmbedding(string Alias, EmbeddingVector Embedding);

public static class VideoFaceReferenceSetLoader
{
    private const int MaximumManifestBytes = 16 * 1024;
    public const int MaximumReferences = 6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<VideoFaceReferenceSet> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileInfo file = new(Path.GetFullPath(path));
        if (!file.Exists || file.Length is < 1 or > MaximumManifestBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("Reference manifest must be a regular JSON file no larger than 16 KiB.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
        if (bytes.Length is < 1 or > MaximumManifestBytes)
        {
            throw new ArgumentException("Reference manifest size changed during reading.");
        }

        VideoFaceReferenceSet set = JsonSerializer.Deserialize<VideoFaceReferenceSet>(bytes, JsonOptions)
            ?? throw new ArgumentException("Reference manifest is empty.");
        Validate(set);
        return set;
    }

    public static void Validate(VideoFaceReferenceSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.SchemaVersion != 1 || set.References is not { Count: >= 1 and <= MaximumReferences })
        {
            throw new ArgumentException("Expected schemaVersion 1 and between one and six reference stills.");
        }

        HashSet<string> aliases = new(StringComparer.Ordinal);
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (VideoFaceReference? reference in set.References)
        {
            if (reference is null ||
                !ValidAlias(reference.Alias) ||
                !aliases.Add(reference.Alias))
            {
                throw new ArgumentException("Reference aliases must be unique pseudonyms R01–R99.");
            }

            // Reuse the exact still-image filename and extension restrictions from frame selection.
            VideoFaceFrameSetLoader.Validate(
                new VideoFaceFrameSet(1, [new VideoFaceFrame(0, reference.File)]));
            if (!files.Add(reference.File))
            {
                throw new ArgumentException("Each approved reference photo may be selected only once.");
            }
        }
    }

    private static bool ValidAlias(string? alias) =>
        alias is { Length: 3 } &&
        alias[0] == 'R' &&
        char.IsAsciiDigit(alias[1]) &&
        char.IsAsciiDigit(alias[2]) &&
        alias != "R00";
}

/// <summary>
/// This is nearest-neighbour cosine evidence only, not identity recognition.
/// All findings require human review; similarity and gap cutoffs are exploratory.
/// </summary>
public sealed class VideoFaceReferenceMatcher
{
    public const double ExploratoryCandidateSimilarity = 0.80;
    public const double ExploratoryGap = 0.05;

    public int ReferenceCount => _references.Count;

    private readonly IReadOnlyList<VideoFaceReferenceEmbedding> _references;

    public VideoFaceReferenceMatcher(IReadOnlyList<VideoFaceReferenceEmbedding> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count is < 1 or > VideoFaceReferenceSetLoader.MaximumReferences ||
            references.Any(r => r is null || r.Embedding is null))
        {
            throw new ArgumentException("One to six prepared references are required.", nameof(references));
        }

        if (references.Select(r => r.Alias).Distinct(StringComparer.Ordinal).Count() != references.Count)
        {
            throw new ArgumentException("Reference aliases must be unique.", nameof(references));
        }

        _references = references.ToArray();
    }

    public VideoFaceReferenceFinding FindCandidate(
        int frameNumber,
        long timestampMs,
        int faceNumber,
        EmbeddingVector query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (frameNumber < 1 || timestampMs < 0 || faceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumber));
        }

        var ranked = _references
            .Select(reference => new
            {
                reference.Alias,
                Score = query.CosineSimilarity(reference.Embedding),
            })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Alias, StringComparer.Ordinal)
            .ToArray();

        double top = ranked[0].Score;
        double? second = ranked.Length > 1 ? ranked[1].Score : null;
        double? gap = second is null ? null : top - second.Value;
        string status = top < ExploratoryCandidateSimilarity
            ? "below-exploratory-threshold"
            : gap is null
                ? "single-reference-uncertain"
                : gap.Value < ExploratoryGap
                    ? "ambiguous"
                    : "review-candidate";

        return new VideoFaceReferenceFinding(
            frameNumber,
            timestampMs,
            faceNumber,
            // A below-threshold result must not present a person candidate.
            top < ExploratoryCandidateSimilarity ? null : ranked[0].Alias,
            Math.Round(top, 4),
            second is null ? null : Math.Round(second.Value, 4),
            gap is null ? null : Math.Round(gap.Value, 4),
            status);
    }
}
