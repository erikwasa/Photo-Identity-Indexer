using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityModelEvaluationCommand
{
    private const string ProxyRootEnvironmentVariable =
        "PhotoIdentity__ReviewProxyRoot";

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(arg => arg is "-h" or "--help"))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "baseline" => await RunBaselineAsync(args[1..]),
                "calibration-report" => await RunCalibrationReportAsync(args[1..]),
                _ => throw new ArgumentException(
                    $"Unknown quality-eval command: {args[0]}"),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }
    }

    private static async Task<int> RunBaselineAsync(string[] args)
    {
        BaselineOptions options = ParseBaseline(args);
        string inputPath = Path.GetFullPath(options.InputPath);
        string outputPath = ValidatePrivateOutputPath(options.OutputPath);
        string proxyRoot = Path.GetFullPath(RequiredSetting(
            options.ProxyRoot,
            ProxyRootEnvironmentVariable,
            "review-proxy derivative root"));

        if (!Directory.Exists(proxyRoot))
        {
            throw new DirectoryNotFoundException(
                $"Review-proxy root does not exist: {proxyRoot}");
        }

        if (File.Exists(outputPath) && !options.Force)
        {
            throw new IOException(
                $"Output already exists: {outputPath}. Pass --force to replace it.");
        }

        PhotoQualityBenchmarkExport benchmark =
            await PhotoQualityBenchmarkFile.LoadAsync(inputPath);
        ReviewProxyProfile profile = ParseReviewProxyProfile(
            benchmark.ProxyProfile);
        if (profile.MaximumLongEdge !=
                TechnicalPhotoQualityProtocol.CalibratedMaximumLongEdge ||
            profile.JpegQuality !=
                TechnicalPhotoQualityProtocol.CalibratedJpegQuality)
        {
            throw new InvalidDataException(
                "WI-0182 baseline requires the calibrated 1600px/q78 review-proxy profile.");
        }

        OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();
        List<PhotoQualityCandidateScore> scores = [];
        List<double> inferenceTimes = [];
        Stopwatch total = Stopwatch.StartNew();

        foreach (PhotoQualityBenchmarkPhoto photo in benchmark.Photos)
        {
            string proxyPath = PhotoQualityBenchmarkFile.ResolveProxyPath(
                proxyRoot,
                photo.ProxyRelativePath);
            byte[] content = await File.ReadAllBytesAsync(proxyPath);
            PhotoQualityBenchmarkFile.VerifyProxySha256(photo, content);

            Stopwatch inference = Stopwatch.StartNew();
            TechnicalPhotoQualityEvidence evidence =
                analyzer.Analyze(content, profile);
            double rawScore = PhotoQualityTechnicalBaseline.Score(evidence);
            inference.Stop();

            double milliseconds = inference.Elapsed.TotalMilliseconds;
            inferenceTimes.Add(milliseconds);
            scores.Add(new PhotoQualityCandidateScore(
                photo.RevisionId,
                rawScore,
                milliseconds,
                null,
                evidence.Reasons));
        }

        total.Stop();
        double[] sortedTimes = inferenceTimes.OrderBy(value => value).ToArray();
        PhotoQualityCandidateScoreExport export = new(
            SchemaVersion: 1,
            BenchmarkId: benchmark.BenchmarkId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Candidate: new PhotoQualityCandidateDescriptor(
                CandidateId: PhotoQualityTechnicalBaseline.CandidateId,
                ModelName: "WI-0182 technical proxy evidence baseline",
                ModelVersion: TechnicalPhotoQualityProtocol.AlgorithmVersion,
                Framework: "OpenCvSharp",
                License: "repository-owned evaluation code",
                Source: "WI-0182 / proxy-technical-quality-v1",
                Preprocessing:
                    $"{profile.Id}; grayscale technical analyzer; no archive-relative normalization",
                ScoreSemantics:
                    "100 - 10 * calibrated candidate-reason count; higher is better; intentionally simple and tie-heavy baseline",
                ModelSizeBytes: 0,
                ExecutionProvider: "CPU"),
            InputKind: "review-proxy",
            HigherIsBetter: true,
            PhotoCount: scores.Count,
            Runtime: new PhotoQualityCandidateRuntimeSummary(
                total.Elapsed.TotalMilliseconds,
                Percentile(sortedTimes, 0.50d),
                Percentile(sortedTimes, 0.95d)),
            Scores: scores);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(
                export,
                PhotoQualityBenchmarkFile.JsonOptions));

        Console.WriteLine(
            $"Scored {scores.Count} benchmark proxies with {PhotoQualityTechnicalBaseline.CandidateId}.");
        Console.WriteLine(
            $"Median inference {export.Runtime.MedianInferenceMilliseconds:F2} ms; p95 {export.Runtime.P95InferenceMilliseconds:F2} ms.");
        Console.WriteLine($"Private candidate score export: {outputPath}");
        Console.WriteLine(
            "This baseline is evaluation-only and does not persist or change production quality behavior.");
        return 0;
    }

    private static async Task<int> RunCalibrationReportAsync(string[] args)
    {
        CalibrationOptions options = ParseCalibration(args);
        PhotoQualityBenchmarkExport benchmark =
            await PhotoQualityBenchmarkFile.LoadAsync(options.InputPath);
        PhotoQualityCandidateScoreExport scoreExport =
            await LoadScoreExportAsync(options.ScoresPath);

        PhotoQualityCalibrationReport report =
            PhotoQualityModelEvaluation.EvaluateCalibration(
                benchmark,
                scoreExport);

        Console.WriteLine(
            $"Candidate: {report.Candidate.CandidateId} ({report.Candidate.ModelVersion})");
        Console.WriteLine(
            $"Calibration representative photos: {report.RepresentativeCalibrationCount}; reviewed known-tier={report.ReviewedKnownTierCount}; scored reviewed={report.ScoredReviewedCount}; inference failures={report.ReviewedInferenceFailureCount}.");
        Console.WriteLine(
            $"Spearman rho: {FormatNullable(report.SpearmanRankCorrelation)}");
        Console.WriteLine(
            $"Pairwise >=2 tiers: {report.PairwiseTwoTierAgreement.CorrectPairs}/{report.PairwiseTwoTierAgreement.TotalPairs} correct; ties={report.PairwiseTwoTierAgreement.TiedPairs}; agreement={FormatPercent(report.PairwiseTwoTierAgreement.AgreementFraction)}.");
        Console.WriteLine(
            $"Reviewed Very Low calibration labels: {report.VeryLowReviewedCount}.");

        if (report.BestObservedVeryLowThresholdAtOrAboveTarget is { } threshold)
        {
            Console.WriteLine(
                $"Best calibration threshold at >= {report.VeryLowObservedPrecisionTarget:P0} observed precision: threshold={threshold.Threshold.ToString("G17", CultureInfo.InvariantCulture)}, predicted={threshold.PredictedVeryLowCount}, false-positive={threshold.FalsePositiveCount}, observed precision={threshold.ObservedPrecision:P1}, Wilson95 lower={threshold.Wilson95LowerBound:P1}, recall={FormatPercent(threshold.Recall)}.");
        }
        else
        {
            Console.WriteLine(
                $"No calibration threshold reaches {report.VeryLowObservedPrecisionTarget:P0} observed precision.");
        }

        Console.WriteLine(
            "Held-out human labels are excluded from this report; held-out evaluation is intentionally not implemented in this checkpoint.");

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            string outputPath = ValidatePrivateOutputPath(options.OutputPath);
            if (File.Exists(outputPath) && !options.Force)
            {
                throw new IOException(
                    $"Output already exists: {outputPath}. Pass --force to replace it.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(
                    report,
                    PhotoQualityBenchmarkFile.JsonOptions));
            Console.WriteLine($"Private calibration report: {outputPath}");
        }

        return 0;
    }

    private static async Task<PhotoQualityCandidateScoreExport>
        LoadScoreExportAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Candidate score export was not found.",
                fullPath);
        }

        await using FileStream stream = File.OpenRead(fullPath);
        return await JsonSerializer.DeserializeAsync<PhotoQualityCandidateScoreExport>(
                   stream,
                   PhotoQualityBenchmarkFile.JsonOptions)
               ?? throw new InvalidDataException(
                   "Candidate score export JSON is empty or invalid.");
    }

    private static ReviewProxyProfile ParseReviewProxyProfile(string canonical)
    {
        Dictionary<string, string> values = canonical
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => parts[1],
                StringComparer.OrdinalIgnoreCase);

        if (!values.TryGetValue("profile-id", out string? id) ||
            !values.TryGetValue("maximum-long-edge", out string? maximumLongEdgeText) ||
            !values.TryGetValue("jpeg-quality", out string? jpegQualityText) ||
            !int.TryParse(
                maximumLongEdgeText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maximumLongEdge) ||
            !int.TryParse(
                jpegQualityText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int jpegQuality))
        {
            throw new InvalidDataException(
                "Benchmark proxyProfile is missing profile-id, maximum-long-edge or jpeg-quality.");
        }

        return new ReviewProxyProfile(id, maximumLongEdge, jpegQuality);
    }

    private static BaselineOptions ParseBaseline(string[] args)
    {
        string input = string.Empty;
        string output = string.Empty;
        string? proxyRoot = null;
        bool force = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input":
                    input = NextValue(args, ref index, "--input");
                    break;
                case "--output":
                    output = NextValue(args, ref index, "--output");
                    break;
                case "--proxy-root":
                    proxyRoot = NextValue(args, ref index, "--proxy-root");
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown baseline argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(input) ||
            string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException(
                "baseline requires --input <benchmark> and --output <score-export>.");
        }

        return new BaselineOptions(input, output, proxyRoot, force);
    }

    private static CalibrationOptions ParseCalibration(string[] args)
    {
        string input = string.Empty;
        string scores = string.Empty;
        string? output = null;
        bool force = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input":
                    input = NextValue(args, ref index, "--input");
                    break;
                case "--scores":
                    scores = NextValue(args, ref index, "--scores");
                    break;
                case "--output":
                    output = NextValue(args, ref index, "--output");
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown calibration-report argument: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(input) ||
            string.IsNullOrWhiteSpace(scores))
        {
            throw new ArgumentException(
                "calibration-report requires --input <benchmark> and --scores <score-export>.");
        }

        return new CalibrationOptions(input, scores, output, force);
    }

    private static string ValidatePrivateOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An output path is required.");
        }

        if (!Path.IsPathRooted(outputPath))
        {
            string normalized = outputPath.Replace('\\', '/').TrimStart('.', '/');
            if (!normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("data/", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("staging/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Relative quality-evaluation exports must be under private/, data/, or staging/.");
            }
        }

        return Path.GetFullPath(outputPath);
    }

    private static string RequiredSetting(
        string? optionValue,
        string environmentVariable,
        string description)
    {
        string? value = string.IsNullOrWhiteSpace(optionValue)
            ? Environment.GetEnvironmentVariable(environmentVariable)
            : optionValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Provide the {description} or set {environmentVariable}.");
        }

        return value.Trim();
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0d;
        }

        int index = Math.Clamp(
            (int)Math.Ceiling(sortedValues.Length * percentile) - 1,
            0,
            sortedValues.Length - 1);
        return sortedValues[index];
    }

    private static string FormatNullable(double? value) =>
        value.HasValue
            ? value.Value.ToString("F3", CultureInfo.InvariantCulture)
            : "n/a";

    private static string FormatPercent(double? value) =>
        value.HasValue ? value.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a";

    private static string NextValue(
        string[] args,
        ref int index,
        string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval baseline --input private/photo-quality-benchmark.json --output private/wi0182-quality-scores.json [--proxy-root <path>] [--force]");
        Console.WriteLine(
            "       dotnet run --project tools/PhotoIdentity.PhotoQualityEvaluation -- benchmark quality-eval calibration-report --input private/photo-quality-benchmark.json --scores private/<candidate>-scores.json [--output private/<candidate>-calibration.json] [--force]");
    }

    private sealed record BaselineOptions(
        string InputPath,
        string OutputPath,
        string? ProxyRoot,
        bool Force);

    private sealed record CalibrationOptions(
        string InputPath,
        string ScoresPath,
        string? OutputPath,
        bool Force);
}
