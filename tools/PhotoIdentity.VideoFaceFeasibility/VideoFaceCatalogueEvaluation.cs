using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Private, operator-approved identity scope and local video manifests. No archive discovery.
/// This file must remain outside Git and never be sent to telemetry or standard output.
/// </summary>
public sealed record VideoFaceCatalogueCase(string Alias, string Manifest);
public sealed record VideoFaceCataloguePerson(string Alias, Guid PersonId);
public sealed record VideoFaceCatalogueCases(
    int SchemaVersion,
    List<VideoFaceCataloguePerson>? People,
    List<VideoFaceCatalogueCase>? Clips);

public static class VideoFaceCatalogueCasesLoader
{
    private const int MaximumBytes = 16 * 1024;
    public const int MaximumPeople = 32;
    public const int MaximumClips = 4;

    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<VideoFaceCatalogueCases> LoadAsync(
        string filename, string repositoryRoot, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(filename))
        {
            throw new ArgumentException("A fully qualified private case-manifest path is required.");
        }

        string path = Path.GetFullPath(filename);
        FileInfo info = new(path);
        if (!info.Exists || info.Length is < 1 or > MaximumBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            IsWithin(repositoryRoot, path))
        {
            throw new ArgumentException("The private case manifest is unavailable, linked, oversized or inside the repository.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length is < 1 or > MaximumBytes)
        {
            throw new ArgumentException("The private case manifest size changed.");
        }

        var cases = JsonSerializer.Deserialize<VideoFaceCatalogueCases>(bytes, InputOptions)
            ?? throw new ArgumentException("The case manifest is empty.");
        Validate(cases, repositoryRoot, Path.GetDirectoryName(path)!);
        return cases;
    }

    public static void Validate(
        VideoFaceCatalogueCases cases, string repositoryRoot, string casesDirectory)
    {
        ArgumentNullException.ThrowIfNull(cases);
        if (cases.SchemaVersion != 1 ||
            cases.People is not { Count: >= 1 and <= MaximumPeople } ||
            cases.Clips is not { Count: >= 2 and <= MaximumClips })
        {
            throw new ArgumentException("Require schemaVersion 1, 1-32 people and 2-4 clips.");
        }

        if (cases.People.Any(p => p is null || p.PersonId == Guid.Empty ||
                !IsAlias(p.Alias, 'P')) ||
            cases.People.Select(p => p.Alias).Distinct(StringComparer.Ordinal).Count() != cases.People.Count ||
            cases.People.Select(p => p.PersonId).Distinct().Count() != cases.People.Count)
        {
            throw new ArgumentException("People require unique nonempty UUIDs and aliases P01-P99.");
        }

        if (cases.Clips.Any(c => c is null || !IsAlias(c.Alias, 'V') ||
                !Path.IsPathFullyQualified(c.Manifest) ||
                Path.GetExtension(c.Manifest) != ".json" ||
                IsWithin(repositoryRoot, c.Manifest) ||
                IsWithin(casesDirectory, c.Manifest) ||
                !IsRegularLocalFile(c.Manifest)) ||
            cases.Clips.Select(c => c.Alias).Distinct(StringComparer.Ordinal).Count() != cases.Clips.Count ||
            cases.Clips.Select(c => Path.GetFullPath(c.Manifest))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != cases.Clips.Count)
        {
            throw new ArgumentException("Clips require unique V01-V99 aliases and approved local manifests outside the case-manifest folder and repository.");
        }
    }

    private static bool IsAlias(string? value, char prefix) =>
        value is { Length: 3 } &&
        value[0] == prefix &&
        char.IsAsciiDigit(value[1]) &&
        char.IsAsciiDigit(value[2]) &&
        value[1..] != "00";

    private static bool IsRegularLocalFile(string filename)
    {
        var info = new FileInfo(Path.GetFullPath(filename));
        return info.Exists && info.Length is > 0 and <= MaximumBytes &&
            (info.Attributes & FileAttributes.ReparsePoint) == 0 &&
            (info.Directory!.Attributes & FileAttributes.ReparsePoint) == 0;
    }

    internal static bool IsWithin(string parent, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}

public sealed record VideoFaceCatalogueExemplar(
    string PersonAlias,
    string ContentSha256,
    EmbeddingVector Vector);

/// <summary>
/// Rank people, NOT reference photos. A person's three exemplars cannot occupy the
/// winner and runner-up simultaneously. Neither the max exemplar score nor repeated
/// high scores constitute a verified identity: all labels remain review-only.
/// </summary>
public sealed class VideoFaceCataloguePersonMatcher : IVideoFaceCandidateMatcher
{
    public const int MaximumExemplarsPerPerson = 3;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<EmbeddingVector>> _people;

    public int ReferenceCount => _people.Count;

    public VideoFaceCataloguePersonMatcher(IReadOnlyList<VideoFaceCatalogueExemplar> exemplars)
    {
        ArgumentNullException.ThrowIfNull(exemplars);
        if (exemplars.Count is < 1 or > VideoFaceCatalogueCasesLoader.MaximumPeople * MaximumExemplarsPerPerson ||
            exemplars.Any(e => e is null || e.Vector is null || string.IsNullOrEmpty(e.PersonAlias)))
        {
            throw new ArgumentException("A bounded eligible catalogue exemplar set is required.");
        }

        var groups = exemplars.GroupBy(e => e.PersonAlias, StringComparer.Ordinal).ToArray();
        if (groups.Length > VideoFaceCatalogueCasesLoader.MaximumPeople ||
            groups.Any(g => g.Count() > MaximumExemplarsPerPerson ||
                g.Select(e => e.ContentSha256).Distinct(StringComparer.Ordinal).Count() != g.Count()) ||
            exemplars.Select(e => e.Vector.Dimensions).Distinct().Count() != 1)
        {
            throw new ArgumentException("Inconsistent dimensions or duplicate/nonbounded person exemplars.");
        }

        _people = groups.ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<EmbeddingVector>)g.Select(e => e.Vector).ToArray(),
            StringComparer.Ordinal);
    }

    public VideoFaceReferenceFinding FindCandidate(
        int frameNumber, long timestampMs, int faceNumber, EmbeddingVector query)
    {
        var (alias, top, second) = Rank(query);
        double? gap = second is null ? null : top - second.Value;
        string status = top < VideoFaceReferenceMatcher.ExploratoryCandidateSimilarity
            ? "below-exploratory-threshold"
            : gap is null
                ? "single-reference-uncertain"
                : gap.Value < VideoFaceReferenceMatcher.ExploratoryGap ? "ambiguous" : "review-candidate";
        return new(frameNumber, timestampMs, faceNumber,
            top < VideoFaceReferenceMatcher.ExploratoryCandidateSimilarity ? null : alias,
            Math.Round(top, 4),
            second is null ? null : Math.Round(second.Value, 4),
            gap is null ? null : Math.Round(gap.Value, 4),
            status);
    }

    public VideoFaceRankedDiagnostic Diagnose(
        int frameNumber, long timestampMs, int faceNumber, EmbeddingVector query)
    {
        var (alias, top, second) = Rank(query);
        return new(frameNumber, timestampMs, faceNumber, alias,
            Math.Round(top, 4),
            second is null ? null : Math.Round(second.Value, 4),
            second is null ? null : Math.Round(top - second.Value, 4),
            VideoFaceSensitivityDiagnostics.Compare(top, second));
    }

    private (string Alias, double Score, double? RunnerUp) Rank(EmbeddingVector query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var ranked = _people.Select(group => (
                Alias: group.Key,
                Score: group.Value.Max(vector => query.CosineSimilarity(vector))))
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.Alias, StringComparer.Ordinal)
            .ToArray();
        return (ranked[0].Alias, ranked[0].Score,
            ranked.Length == 1 ? null : ranked[1].Score);
    }
}

public sealed record VideoFaceCatalogueProvenance(
    DateTimeOffset SnapshotCapturedAtUtc,
    string PgSnapshot,
    string ModelId,
    string ModelSha256,
    string AlignmentProtocol,
    int SelectedPeople,
    int EligiblePeople,
    int ExemplarsLoaded,
    long LatestReviewActionId,
    long LatestEmbeddingId);

/// <summary>Top-rank observations, NOT unique people detected or identities assigned.</summary>
public sealed record VideoFaceCataloguePersonRankSummary(
    string Alias,
    int TopRankObservations,
    double BestTopRankSimilarity,
    int StrongReviewCandidates);

public sealed record VideoFaceCatalogueClipReview(
    string Alias,
    VideoFacePrivateReview Review)
{
    public IReadOnlyList<VideoFaceCataloguePersonRankSummary> PersonRankSummaries { get; init; }
        = [];
    public int BelowThresholdObservations { get; init; }
}

public sealed record VideoFaceCatalogueEvaluationReport(
    int SchemaVersion,
    VideoFaceCatalogueProvenance Catalogue,
    IReadOnlyList<VideoFaceCatalogueClipReview> Clips);

/// <summary>
/// Same private create-new safety as still-reference review; never writes a mapping,
/// vectors, names, canonical person IDs or paths. The operator's private input
/// contains the alias-to-person mapping, not the output.
/// </summary>
public static class VideoFaceCatalogueReportWriter
{
    public static async Task WriteAsync(
        string output, string repositoryRoot, string casesPath,
        IReadOnlyList<string> clipManifests, VideoFaceCatalogueEvaluationReport report,
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(output) ||
            !string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Private report requires an absolute JSON path.");
        }

        string full = Path.GetFullPath(output);
        var info = new FileInfo(full);
        if (!info.Directory!.Exists ||
            (info.Directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Exists ||
            VideoFaceCatalogueCasesLoader.IsWithin(repositoryRoot, full) ||
            VideoFaceCatalogueCasesLoader.IsWithin(Path.GetDirectoryName(Path.GetFullPath(casesPath))!, full) ||
            clipManifests.Any(m => VideoFaceCatalogueCasesLoader.IsWithin(
                Path.GetDirectoryName(Path.GetFullPath(m))!, full)))
        {
            throw new ArgumentException("Private report must be a new file outside repository and all input folders.");
        }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        bool created = false;
        try
        {
            await using var stream = new FileStream(
                full, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16 * 1024, useAsync: true);
            created = true;
            await stream.WriteAsync(json, cancellationToken);
        }
        catch
        {
            if (created) File.Delete(full);
            throw;
        }
    }
}
