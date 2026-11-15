using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.CenterFace;
using PhotoIdentity.Recognition.Onnx.Models;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// One selected YuNet candidate from the existing private shadow report.
/// No embeddings, catalogue identity aliases, raw pixels or paths are stored.
/// </summary>
public sealed record VideoFaceIndependentCandidate(
    string Source, string? GroupAlias, VideoFaceGroupCrop Crop);

public sealed record VideoFaceIndependentAgreement(
    string Source, string? GroupAlias,
    int FrameNumber, int FaceNumber, double BestIntersectionOverUnion,
    bool CorroboratedAt030, bool CorroboratedAt050);

public sealed record VideoFaceIndependentAudit(
    int SchemaVersion, string ClipAlias, string SourceReportSha256,
    string IndependentDetectorModelId, string IndependentDetectorSha256,
    double MinimumIntersectionOverUnion,
    int FramesChecked, int GroupCropsChecked, int GroupCropsAt030,
    int GroupCropsAt050, int NoiseCropsChecked, int NoiseCropsAt030,
    int NoiseCropsAt050, IReadOnlyList<VideoFaceIndependentAgreement> Observations)
{
    // These are detector agreement counts, NOT classified faces.
    public bool FullyAccounted =>
        GroupCropsChecked + NoiseCropsChecked == Observations.Count;
}

/// <summary>
/// Read-only, separately invoked independent-detector agreement test. It
/// deliberately DOES NOT alter YuNet proposals, group membership, noise
/// retention, ranked catalogue suggestions or identity decisions.
/// A missing CenterFace match is NOT a non-face decision.
/// </summary>
public static class VideoFaceIndependentDetectorAudit
{
    public const double OverlapForExploration = 0.25;
    public const int MaximumGroupCrops = 64 * 5;
    public const int MaximumNoiseCrops = 96;

    public static IReadOnlyList<VideoFaceIndependentCandidate> Select(
        VideoFaceShadowDbscanReview shadow,
        VideoFaceFrameSet frames, int maximumGroups, int maximumNoise)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        VideoFaceFrameSetLoader.Validate(frames, coverage: true);
        if (maximumGroups is < 1 or > 64 || maximumNoise is < 0 or > 96 ||
            shadow.SchemaVersion != 1 || shadow.SameFrameCollisions != 0 ||
            shadow.ObservationsEvaluated != shadow.ClusterMembers + shadow.NoiseObservations ||
            shadow.ClusterCount != shadow.GroupsRepresentedForReview + shadow.GroupsNotRepresentedForReview ||
            shadow.Groups.Count != shadow.GroupsRepresentedForReview ||
            shadow.Groups.Count > 64 || shadow.NoiseObservations < 0 ||
            shadow.Groups.Any(g => g.Observations < 2 ||
                g.RepresentativeCrops.Count is < 1 or > 5 ||
                g.RepresentativeCrops.Count > g.Observations))
        {
            throw new ArgumentException("Inconsistent private shadow grouping evidence.");
        }

        var candidates = new List<VideoFaceIndependentCandidate>();
        HashSet<string> aliases = new(StringComparer.Ordinal);
        foreach (VideoFaceShadowDbscanGroup group in shadow.Groups.Take(maximumGroups))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(group.Alias,
                    @"^D(?!000)[0-9]{3}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
                !aliases.Add(group.Alias))
            {
                throw new ArgumentException("Invalid or repeated private shadow group.");
            }

            foreach (VideoFaceGroupCrop crop in group.RepresentativeCrops)
                candidates.Add(new("group", group.Alias, crop));
        }

        VideoFaceUnassignedAudit? audit = shadow.NoiseAudit;
        if (shadow.NoiseObservations > 0 && (audit is null ||
            audit.SchemaVersion != 1 ||
            audit.TotalUnassignedObservations != shadow.NoiseObservations ||
            audit.MaximumReviewSamples != 96 || audit.TemporalStrata != 12 ||
            audit.SampledCrops.Count > 96))
        {
            throw new ArgumentException("Missing or inconsistent private shadow noise sample.");
        }

        foreach (VideoFaceGroupCrop crop in audit?.SampledCrops.Take(maximumNoise) ??
            Enumerable.Empty<VideoFaceGroupCrop>())
        {
            candidates.Add(new("noise", null, crop));
        }

        if (candidates.Count is < 1 or > MaximumGroupCrops + MaximumNoiseCrops)
        {
            throw new ArgumentException("Empty or oversized private detector audit selection.");
        }

        foreach (VideoFaceIndependentCandidate candidate in candidates)
        {
            VideoFaceGroupCrop crop = candidate.Crop;
            if (crop.FrameNumber is < 1 or > VideoFaceFrameSetLoader.MaximumCoverageFrames ||
                crop.FrameNumber > frames.Frames!.Count ||
                crop.FaceNumber is < 1 or > 32 ||
                crop.TimestampMs != frames.Frames[crop.FrameNumber - 1].TimestampMs ||
                !double.IsFinite(crop.X) || !double.IsFinite(crop.Y) ||
                !double.IsFinite(crop.Width) || !double.IsFinite(crop.Height) ||
                crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
                crop.X + crop.Width > 1.000001 || crop.Y + crop.Height > 1.000001)
            {
                throw new ArgumentException("Invalid or mismatched private candidate geometry.");
            }
        }

        return candidates;
    }

    public static double BestOverlap(
        VideoFaceGroupCrop crop, IReadOnlyList<DetectedFaceCandidate> independentFaces,
        double minimumConfidence)
    {
        ArgumentNullException.ThrowIfNull(crop);
        ArgumentNullException.ThrowIfNull(independentFaces);
        double best = 0;
        foreach (DetectedFaceCandidate candidate in independentFaces)
        {
            if (candidate.Confidence < minimumConfidence) continue;
            NormalizedBoundingBox b = candidate.BoundingBox;
            double left = Math.Max(crop.X, b.X);
            double top = Math.Max(crop.Y, b.Y);
            double right = Math.Min(crop.X + crop.Width, b.X + b.Width);
            double bottom = Math.Min(crop.Y + crop.Height, b.Y + b.Height);
            double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
            double union = crop.Width * crop.Height + b.Width * b.Height - intersection;
            if (union > 0) best = Math.Max(best, intersection / union);
        }
        return Math.Round(best, 4);
    }

    public static async Task<VideoFaceIndependentAudit> EvaluateAsync(
        string clipAlias, string reportDigest,
        VideoFaceFrameSet frames, string frameDirectory,
        IReadOnlyList<VideoFaceIndependentCandidate> selected,
        IImageDecoder decoder, IFaceDetector independentDetector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(independentDetector);
        ArgumentNullException.ThrowIfNull(selected);
        VideoFaceFrameSetLoader.Validate(frames, coverage: true);
        if (!System.Text.RegularExpressions.Regex.IsMatch(clipAlias,
                @"^V(?!00)[0-9]{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            !System.Text.RegularExpressions.Regex.IsMatch(reportDigest,
                "^[0-9a-f]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            selected.Count is < 1 or > MaximumGroupCrops + MaximumNoiseCrops)
        {
            throw new ArgumentException("Private audit provenance or selected count is invalid.");
        }

        var groups = selected.GroupBy(s => s.Crop.FrameNumber)
            .OrderBy(g => g.Key).ToArray();
        // Preflight ALL requested source frames before calling the model.
        var localFrames = groups.ToDictionary(
            g => g.Key,
            g => VideoFaceFrameSetLoader.ResolveApprovedFrame(frameDirectory,
                frames.Frames![g.Key - 1].File));
        const long unavailableFlags = (long)FileAttributes.Offline | 0x00040000L | 0x00400000L;
        foreach (string frame in localFrames.Values)
        {
            FileInfo local = new(frame);
            if (((long)local.Attributes & unavailableFlags) != 0)
                throw new ArgumentException("Selected evidence frame must already be fully local.");
        }
        var results = new List<VideoFaceIndependentAgreement>(selected.Count);
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = localFrames[group.Key];
            byte[] original = await VideoFaceExperiment.ReadApprovedStillAsync(
                file, cancellationToken);
            byte[] digest = SHA256.HashData(original);
            using var stream = new MemoryStream(original, writable: false);
            ImageFrame image = await decoder.DecodeAsync(
                stream, new DecodeOptions(new ImageSize(3840, 3840)), cancellationToken);
            var detections = await independentDetector.DetectAsync(image, cancellationToken);
            foreach (VideoFaceIndependentCandidate item in group)
            {
                double best030 = BestOverlap(item.Crop, detections, .3);
                double best050 = BestOverlap(item.Crop, detections, .5);
                results.Add(new(
                    item.Source, item.GroupAlias, item.Crop.FrameNumber,
                    item.Crop.FaceNumber, best030,
                    best030 >= OverlapForExploration,
                    best050 >= OverlapForExploration));
            }

            byte[] after = await File.ReadAllBytesAsync(file, cancellationToken);
            if (!SHA256.HashData(after).AsSpan().SequenceEqual(digest))
                throw new InvalidOperationException("Approved source frame changed during independent audit.");
        }

        int groupTotal = results.Count(r => r.Source == "group");
        int noiseTotal = results.Count(r => r.Source == "noise");
        return new VideoFaceIndependentAudit(
            1, clipAlias, reportDigest,
            independentDetector.Descriptor.Id.Value,
            independentDetector.Descriptor.ModelHash.Value,
            OverlapForExploration, groups.Length,
            groupTotal, results.Count(r => r.Source == "group" && r.CorroboratedAt030),
            results.Count(r => r.Source == "group" && r.CorroboratedAt050),
            noiseTotal, results.Count(r => r.Source == "noise" && r.CorroboratedAt030),
            results.Count(r => r.Source == "noise" && r.CorroboratedAt050),
            results);
    }
}

public static class VideoFaceIndependentDetectorCommand
{
    private const long UnsafeAttributes =
        (long)(FileAttributes.ReparsePoint | FileAttributes.Offline) |
        0x00040000L | 0x00400000L;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string? casesPath = null, sourceReport = null, output = null;
            string? modelDir = null, root = null, alias = null;
            int maxGroups = 20, maxNoise = 24;
            HashSet<string> seen = new(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];
                if (flag == "--cross-check-centerface")
                {
                    if (!seen.Add(flag)) throw new ArgumentException();
                    continue;
                }
                if (!seen.Add(flag) || ++i >= args.Length) throw new ArgumentException();
                string value = args[i];
                switch (flag)
                {
                    case "--catalogue-cases": casesPath = value; break;
                    case "--source-shadow-report": sourceReport = value; break;
                    case "--private-report": output = value; break;
                    case "--model-dir": modelDir = value; break;
                    case "--root": root = value; break;
                    case "--clip": alias = value; break;
                    case "--max-groups": maxGroups = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--max-noise": maxNoise = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException();
                }
            }

            if (!seen.Contains("--cross-check-centerface") ||
                casesPath is null || sourceReport is null || output is null ||
                alias is null || maxGroups is < 1 or > 64 || maxNoise is < 0 or > 96)
                throw new ArgumentException();

            string repo = Path.GetFullPath(root ?? Directory.GetCurrentDirectory());
            string reportPath = PrivateFile(sourceReport, 64 * 1024 * 1024);
            string approvedCases = PrivateFile(casesPath, 16 * 1024);
            string requestedOutput = Path.GetFullPath(output);
            VideoFaceCatalogueCases cases =
                await VideoFaceCatalogueCasesLoader.LoadAsync(approvedCases, repo);
            if (cases.CatalogueScope != "all-eligible") throw new ArgumentException();

            var sourceBytes = await File.ReadAllBytesAsync(reportPath);
            string sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
            var report = System.Text.Json.JsonSerializer.Deserialize<VideoFaceCatalogueEvaluationReport>(
                sourceBytes, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                ?? throw new ArgumentException();

            VideoFaceCatalogueCase[] matchingCases = cases.Clips!
                .Where(c => c.Alias == alias).ToArray();
            VideoFaceCatalogueClipReview[] matchingReports = report.Clips
                .Where(c => c.Alias == alias).ToArray();
            if (report.SchemaVersion != 1 || matchingCases.Length != 1 ||
                matchingReports.Length != 1 || report.Clips.Count != cases.Clips!.Count)
                throw new ArgumentException();
            var clip = matchingReports[0];
            VideoFaceShadowDbscanReview shadow = clip.Review.Diagnostics?.ShadowDbscan
                ?? throw new ArgumentException();
            VideoFaceFrameSet frames = await VideoFaceFrameSetLoader.LoadAsync(
                matchingCases[0].Manifest, coverage: true);
            if (clip.Review.Aggregate.FramesExamined != frames.Frames!.Count ||
                clip.Review.Aggregate.FacesDetected != shadow.ObservationsEvaluated)
                throw new ArgumentException();

            string frameDirectory = Path.GetDirectoryName(
                Path.GetFullPath(matchingCases[0].Manifest))!;
            if (!Path.IsPathFullyQualified(output) ||
                Path.GetExtension(output) != ".json" ||
                File.Exists(requestedOutput) ||
                !Directory.Exists(Path.GetDirectoryName(requestedOutput)) ||
                ((long)File.GetAttributes(Path.GetDirectoryName(requestedOutput)!) & UnsafeAttributes) != 0 ||
                IsWithin(repo, requestedOutput) ||
                IsWithin(frameDirectory, requestedOutput) ||
                IsWithin(Path.GetDirectoryName(reportPath)!, requestedOutput) ||
                IsWithin(Path.GetDirectoryName(approvedCases)!, requestedOutput))
                throw new ArgumentException();

            IReadOnlyList<VideoFaceIndependentCandidate> selected =
                VideoFaceIndependentDetectorAudit.Select(shadow, frames, maxGroups, maxNoise);

            string installed = Path.GetFullPath(modelDir ?? Path.Combine(repo, "models", "files"));
            ModelManifest manifest = await new ModelManifestLoader().LoadAsync(
                Path.Combine(repo, "models", "manifests", "centerface-2019-fp32.json"),
                CancellationToken.None);
            string modelPath = Path.Combine(installed, manifest.FileName);
            await new ModelFileVerifier().VerifyOrThrowAsync(modelPath, manifest);

            using var centerFace = new CenterFaceFaceDetector(manifest, modelPath,
                new CenterFaceDetectorOptions { ConfidenceThreshold = .3 });
            VideoFaceIndependentAudit result = await VideoFaceIndependentDetectorAudit.EvaluateAsync(
                alias, sourceHash, frames, frameDirectory, selected,
                new OpenCvImageDecoder(), centerFace);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
            await using (var stream = new FileStream(requestedOutput,
                FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(bytes);

            // Only aggregate agreement counts, never faces, identities or paths.
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                clip = result.ClipAlias,
                detectorAgreementOnly = true,
                framesChecked = result.FramesChecked,
                groupCropsChecked = result.GroupCropsChecked,
                groupCropsAt030 = result.GroupCropsAt030,
                groupCropsAt050 = result.GroupCropsAt050,
                noiseCropsChecked = result.NoiseCropsChecked,
                noiseCropsAt030 = result.NoiseCropsAt030,
                noiseCropsAt050 = result.NoiseCropsAt050,
                privateReportWritten = true,
                automaticSuppressionEnabled = false,
            }, Json));
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(
                "error: private CenterFace cross-check failed; input details were not logged.");
            return 1;
        }
    }

    private static string PrivateFile(string input, long limit)
    {
        if (!Path.IsPathFullyQualified(input)) throw new ArgumentException();
        string path = Path.GetFullPath(input);
        FileInfo file = new(path);
        if (!file.Exists || file.Length is < 1 || file.Length > limit ||
            ((long)file.Attributes & UnsafeAttributes) != 0 ||
            ((long)file.Directory!.Attributes & UnsafeAttributes) != 0)
            throw new ArgumentException();
        return path;
    }

    private static bool IsWithin(string parent, string file)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(parent), file);
        return relative == "." || (!Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
