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
        VideoFaceDetectorSelection Detection,
        VideoFaceOverflowPolicy OverflowPolicy,
        bool ProvisionalGroups,
        bool UnassignedAudit,
        string? PrivatePeopleMap);

    public static async Task<int> RunAsync(string[] args)
    {
        string stage = "input-selection";
        try
        {
            Options options = Parse(args);
            VideoFaceCatalogueCases cases = await VideoFaceCatalogueCasesLoader.LoadAsync(
                options.Cases, options.Root);
            bool allPeople = cases.CatalogueScope == "all-eligible";
            if (allPeople != (options.PrivatePeopleMap is not null))
            {
                throw new ArgumentException(
                    "all-eligible scope requires --private-people-map; selected scope forbids it.");
            }
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

            if (options.PrivatePeopleMap is not null)
            {
                VideoFaceCataloguePrivatePeopleMapWriter.ValidateTarget(
                    options.PrivatePeopleMap, options.Root, options.Cases,
                    selected.Select(s => s.Clip.Manifest).ToArray(), options.PrivateReport);
            }

            stage = "model-verification";
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
                options.Detection.Describe(detector.Descriptor.InputSize),
                options.OverflowPolicy);

            // Evidence is materialized once and the transaction is closed before
            // any video-frame inference. All clips see the SAME identity snapshot.
            stage = "catalogue-snapshot";
            var (provenance, matcher, privatePeopleMap) =
                await VideoFaceCatalogueEvidenceReader.LoadAsync(cases, embedder.Descriptor);
            List<VideoFaceCatalogueClipReview> reviews = [];
            foreach (var (clip, frames) in selected)
            {
                stage = "clip-analysis";
                string folder = Path.GetDirectoryName(Path.GetFullPath(clip.Manifest))!;
                VideoFacePrivateReview review = await experiment.RunWithPreparedMatcherAsync(
                    frames, folder, matcher, options.DetectorConfidence,
                    provisionalGroups: options.ProvisionalGroups,
                    unassignedAudit: options.UnassignedAudit);
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

            stage = "report-persistence";
            await VideoFaceCatalogueReportWriter.WriteAsync(
                options.PrivateReport, options.Root, options.Cases,
                selected.Select(s => s.Clip.Manifest).ToArray(),
                new VideoFaceCatalogueEvaluationReport(1, provenance, reviews));
            if (options.PrivatePeopleMap is not null)
            {
                await VideoFaceCataloguePrivatePeopleMapWriter.WriteAsync(
                    options.PrivatePeopleMap, options.Root, options.Cases,
                    selected.Select(s => s.Clip.Manifest).ToArray(), options.PrivateReport,
                    new VideoFaceCataloguePrivatePersonMap(
                        1, provenance.PgSnapshot, privatePeopleMap));
            }

            // stdout is aggregate ONLY: no paths, catalogue IDs, names, aliases,
            // frames/timestamps, scores, ranks, vectors or private case mappings.
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                clipsEvaluated = reviews.Count,
                fullEligibleCatalogue = allPeople,
                canonicalPeople = provenance.SelectedPeople,
                eligiblePeople = provenance.EligiblePeople,
                framesExamined = reviews.Sum(r => r.Review.Aggregate.FramesExamined),
                framesWithFaces = reviews.Sum(r => r.Review.Aggregate.FramesWithFaces),
                facesDetected = reviews.Sum(r => r.Review.Aggregate.FacesDetected),
                embeddingsCalculated = reviews.Sum(r => r.Review.Aggregate.EmbeddingsCalculated),
                framesWithCandidateOverflow = reviews.Sum(r =>
                    r.Review.Diagnostics!.FrameDetections.Count(f =>
                        f.CandidatesDiscarded is > 0)),
                candidatesDiscarded = reviews.Sum(r =>
                    r.Review.Diagnostics!.FrameDetections.Sum(f =>
                        f.CandidatesDiscarded ?? 0)),
                groupReviewRequested = options.ProvisionalGroups,
                unassignedAuditRequested = options.UnassignedAudit,
                unassignedAuditSamples = options.UnassignedAudit
                    ? reviews.Sum(r => r.Review.Diagnostics!.ProvisionalGroups!.UnassignedAudit!.SampledObservations)
                    : 0,
                unassignedAuditStrataRepresented = options.UnassignedAudit
                    ? reviews.Sum(r => r.Review.Diagnostics!.ProvisionalGroups!.UnassignedAudit!.StrataRepresented)
                    : 0,
                provisionalGroups = options.ProvisionalGroups
                    ? reviews.Sum(r => r.Review.Diagnostics!.ProvisionalGroups!.Groups.Count)
                    : 0,
                groupsWithTopThree = options.ProvisionalGroups
                    ? reviews.Sum(r => r.Review.Diagnostics!.GroupSuggestions!.Count)
                    : 0,
                groupObservationsUnassigned = options.ProvisionalGroups
                    ? reviews.Sum(r => r.Review.Diagnostics!.ProvisionalGroups!.ObservationsUnassigned)
                    : 0,
                elapsedMilliseconds = Math.Round(reviews.Sum(
                    r => r.Review.Aggregate.ElapsedMilliseconds), 3),
                privateReportWritten = true,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            // A known, constant stage is safe to share. Never forward arbitrary
            // exception messages, source paths, timestamps, or identity evidence.
            // The report remains create-new and is never overwritten.
            Console.Error.WriteLine(VideoFaceFailureDiagnostics.Describe(exception, stage));
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        string? cases = null;
        string? report = null;
        string? peopleMap = null;
        string? root = null;
        string? models = null;
        string? detectorConfidence = null;
        string? detectorPipeline = null;
        string? tileSize = null;
        string? overflowPolicy = null;
        bool provisionalGroups = false;
        bool unassignedAudit = false;
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (name == "--provisional-groups")
            {
                if (provisionalGroups)
                {
                    throw new ArgumentException("Duplicate provisional-groups option.");
                }
                provisionalGroups = true;
                continue;
            }
            if (name == "--unassigned-audit")
            {
                if (unassignedAudit)
                {
                    throw new ArgumentException("Duplicate unassigned-audit option.");
                }
                unassignedAudit = true;
                continue;
            }
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
            {
                throw new ArgumentException("Each option requires a value.");
            }

            string value = args[i];
            switch (name)
            {
                case "--catalogue-cases" when cases is null: cases = value; break;
                case "--private-report" when report is null: report = value; break;
                case "--private-people-map" when peopleMap is null: peopleMap = value; break;
                case "--root" when root is null: root = value; break;
                case "--model-dir" when models is null: models = value; break;
                case "--detector-confidence" when detectorConfidence is null:
                    detectorConfidence = value; break;
                case "--detector-pipeline" when detectorPipeline is null:
                    detectorPipeline = value; break;
                case "--tile-size" when tileSize is null:
                    tileSize = value; break;
                case "--face-overflow-policy" when overflowPolicy is null:
                    overflowPolicy = value; break;
                default: throw new ArgumentException("Unknown or duplicate catalogue option.");
            }
        }

        if (unassignedAudit && !provisionalGroups)
        {
            throw new ArgumentException("The unassigned audit requires --provisional-groups.");
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
        VideoFaceDetectorSelection selected = VideoFaceDetectorSelection.Parse(
            detectorPipeline, tileSize);
        VideoFaceOverflowPolicy policy = overflowPolicy switch
        {
            null or "fail" => VideoFaceOverflowPolicy.Fail,
            "retain-top-32" => VideoFaceOverflowPolicy.RetainTop32,
            _ => throw new ArgumentException(
                "Face overflow policy must be fail or retain-top-32."),
        };
        if (policy != VideoFaceOverflowPolicy.Fail &&
            selected.PipelineMode != YuNetDetectorPipelineMode.MultiScale)
        {
            throw new ArgumentException(
                "Candidate overflow retention requires explicit multiscale detection.");
        }
        if (peopleMap is not null && !Path.IsPathFullyQualified(peopleMap))
        {
            throw new ArgumentException("Private people map requires a fully qualified path.");
        }
        return new Options(cases, report, repo, dir, threshold, selected, policy,
            provisionalGroups, unassignedAudit, peopleMap);
    }
}
