using System.Text.Json;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;
using PhotoIdentity.Recognition.Onnx.YuNet;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Isolated opt-in catalogue experiment. One short read-only snapshot, two to four
/// explicit local video manifests, one create-new private report. No production
/// API/worker, automatic identification, background scanning or database writes.
/// </summary>
public static class VideoFaceCatalogueRunner
{
    private sealed record Options(
        string Cases, string PrivateReport, string Root, string ModelDirectory,
        double DetectorConfidence,
        VideoFaceDetectorSelection Detection);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Options options = Parse(args);
            VideoFaceCatalogueCases cases = await VideoFaceCatalogueCasesLoader.LoadAsync(
                options.Cases, options.Root);
            var selected = new List<(VideoFaceCatalogueCase Clip, VideoFaceFrameSet Frames)>();
            foreach (VideoFaceCatalogueCase clip in cases.Clips!)
            {
                VideoFaceFrameSet frames = await VideoFaceFrameSetLoader.LoadAsync(
                    clip.Manifest, coverage: true);
                string folder = Path.GetDirectoryName(Path.GetFullPath(clip.Manifest))!;
                // Fail before opening PostgreSQL if any selected file is not present.
                foreach (VideoFaceFrame frame in frames.Frames!)
                {
                    _ = VideoFaceFrameSetLoader.ResolveApprovedFrame(folder, frame.File);
                }
                selected.Add((clip, frames));
            }

            ModelManifestLoader loader = new();
            string manifests = Path.Combine(options.Root, "models", "manifests");
            ModelManifest detectorManifest = await loader.LoadAsync(
                Path.Combine(manifests, "yunet-2023mar-fp32.json"), CancellationToken.None);
            ModelManifest embedderManifest = await loader.LoadAsync(
                Path.Combine(manifests, "sface-2021dec-fp32.json"), CancellationToken.None);
            string detectorPath = Path.Combine(
                options.ModelDirectory, detectorManifest.FileName);
            string embedderPath = Path.Combine(
                options.ModelDirectory, embedderManifest.FileName);
            ModelFileVerifier verifier = new();
            await verifier.VerifyOrThrowAsync(detectorPath, detectorManifest);
            await verifier.VerifyOrThrowAsync(embedderPath, embedderManifest);

            using YuNetFaceDetector detector = new(
                detectorManifest, detectorPath,
                options.Detection.CreateOptions(options.DetectorConfidence));
            using SFaceFaceEmbedder embedder = new(embedderManifest, embedderPath);
            var experiment = new VideoFaceExperiment(
                new OpenCvImageDecoder(), detector, new OpenCvFaceAligner(), embedder,
                options.Detection.Describe(detector.Descriptor.InputSize));

            // Evidence is materialized once and the transaction is closed before
            // any video-frame inference. All clips see the SAME identity snapshot.
            var (provenance, matcher) = await VideoFaceCatalogueEvidenceReader.LoadAsync(
                cases, embedder.Descriptor);
            List<VideoFaceCatalogueClipReview> reviews = [];
            foreach (var (clip, frames) in selected)
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(clip.Manifest))!;
                VideoFacePrivateReview review = await experiment.RunWithPreparedMatcherAsync(
                    frames, folder, matcher, options.DetectorConfidence);
                var personRanks = review.Diagnostics!.RankedFaces
                    .GroupBy(face => face.TopReferenceAlias, StringComparer.Ordinal)
                    .Select(group => new VideoFaceCataloguePersonRankSummary(
                        group.Key,
                        group.Count(),
                        group.Max(face => face.TopSimilarity),
                        review.Findings.Count(f => f.CandidateAlias == group.Key &&
                            f.ReviewStatus == "review-candidate")))
                    .OrderBy(s => s.Alias, StringComparer.Ordinal)
                    .ToArray();
                reviews.Add(new VideoFaceCatalogueClipReview(clip.Alias, review)
                {
                    PersonRankSummaries = personRanks,
                    BelowThresholdObservations = review.Findings.Count(
                        f => f.ReviewStatus == "below-exploratory-threshold"),
                });
            }

            await VideoFaceCatalogueReportWriter.WriteAsync(
                options.PrivateReport, options.Root, options.Cases,
                selected.Select(s => s.Clip.Manifest).ToArray(),
                new VideoFaceCatalogueEvaluationReport(1, provenance, reviews));

            // stdout is aggregate ONLY: no paths, catalogue IDs, names, aliases,
            // frames/timestamps, scores, ranks, vectors or private case mappings.
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                clipsEvaluated = reviews.Count,
                framesExamined = reviews.Sum(r => r.Review.Aggregate.FramesExamined),
                framesWithFaces = reviews.Sum(r => r.Review.Aggregate.FramesWithFaces),
                facesDetected = reviews.Sum(r => r.Review.Aggregate.FacesDetected),
                embeddingsCalculated = reviews.Sum(r => r.Review.Aggregate.EmbeddingsCalculated),
                elapsedMilliseconds = Math.Round(reviews.Sum(
                    r => r.Review.Aggregate.ElapsedMilliseconds), 3),
                privateReportWritten = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception)
        {
            // Deliberately never print private paths, credentials, UUIDs or model errors
            // that could contain user-controlled data. The report is never overwritten.
            Console.Error.WriteLine(
                "error: private read-only catalogue evaluation failed; no identity data was logged.");
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        string? cases = null;
        string? report = null;
        string? root = null;
        string? models = null;
        string? detectorConfidence = null;
        string? detectorPipeline = null;
        string? tileSize = null;
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
            {
                throw new ArgumentException("Each option requires a value.");
            }

            string value = args[i];
            switch (name)
            {
                case "--catalogue-cases" when cases is null: cases = value; break;
                case "--private-report" when report is null: report = value; break;
                case "--root" when root is null: root = value; break;
                case "--model-dir" when models is null: models = value; break;
                case "--detector-confidence" when detectorConfidence is null:
                    detectorConfidence = value; break;
                case "--detector-pipeline" when detectorPipeline is null:
                    detectorPipeline = value; break;
                case "--tile-size" when tileSize is null:
                    tileSize = value; break;
                default: throw new ArgumentException("Unknown or duplicate catalogue option.");
            }
        }

        string repo = Path.GetFullPath(root ?? Directory.GetCurrentDirectory());
        string dir = Path.GetFullPath(models ?? Path.Combine(repo, "models", "files"));
        if (cases is null || report is null || !Path.IsPathFullyQualified(cases) ||
            !Path.IsPathFullyQualified(report))
        {
            throw new ArgumentException("Catalogue evaluation requires private cases and report paths.");
        }

        double threshold = detectorConfidence switch
        {
            null or "0.7" => 0.7,
            "0.5" => 0.5,
            "0.9" => 0.9,
            _ => throw new ArgumentException("Only detector confidence 0.5, 0.7, 0.9 is permitted."),
        };
        return new Options(cases, report, repo, dir, threshold,
            VideoFaceDetectorSelection.Parse(detectorPipeline, tileSize));
    }
}
