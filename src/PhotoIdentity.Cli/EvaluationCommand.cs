using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Cli;

internal static class EvaluationCommandRunner
{
    private const int DatasetSchemaVersion = 1;
    private const int ReportSchemaVersion = 1;
    private const string SelectionPolicy = "validation-balanced-known-recall-and-unknown-rejection-v1";

    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(
        EvaluationCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string datasetPath = Path.GetFullPath(options.DatasetPath);
        if (!File.Exists(datasetPath))
        {
            error.WriteLine("error: evaluation dataset does not exist");
            return 2;
        }

        string reportPath = Path.GetFullPath(options.OutputPath);
        if (string.Equals(datasetPath, reportPath, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine("error: evaluation output cannot overwrite the dataset");
            return 2;
        }

        byte[] datasetBytes = await File.ReadAllBytesAsync(datasetPath, cancellationToken);
        EvaluationDataset dataset;
        try
        {
            dataset = JsonSerializer.Deserialize<EvaluationDataset>(datasetBytes, InputJsonOptions)
                ?? throw new ArgumentException("The evaluation dataset is empty.");
            ValidateDataset(dataset);
        }
        catch (JsonException exception)
        {
            error.WriteLine($"error: invalid evaluation dataset JSON: {exception.Message}");
            return 2;
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"error: invalid evaluation dataset: {exception.Message}");
            return 2;
        }

        EvaluationReport report = Evaluate(dataset, datasetBytes, options);
        string json = JsonSerializer.Serialize(report, ReportJsonOptions) + Environment.NewLine;
        await WriteAtomicAsync(reportPath, json, cancellationToken);

        output.WriteLine($"dataset: {dataset.DatasetId}");
        output.WriteLine($"selected-threshold: {report.SelectedThreshold.ToString("0.######", CultureInfo.InvariantCulture)}");
        output.WriteLine($"validation-balanced-score: {report.Validation.Metrics.BalancedIdentityScore.ToString("0.######", CultureInfo.InvariantCulture)}");
        output.WriteLine($"test-balanced-score: {report.Test.Metrics.BalancedIdentityScore.ToString("0.######", CultureInfo.InvariantCulture)}");
        output.WriteLine($"report: {reportPath}");
        return 0;
    }

    private static EvaluationReport Evaluate(
        EvaluationDataset dataset,
        byte[] datasetBytes,
        EvaluationCommandOptions options)
    {
        IReadOnlyDictionary<string, IReadOnlyList<EmbeddingVector>> gallery = BuildGallery(dataset);
        double[] thresholds = dataset.Thresholds
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        EvaluationThresholdResult[] validationSweep = thresholds
            .Select(threshold => EvaluateThreshold(dataset.Validation, gallery, threshold))
            .ToArray();
        EvaluationThresholdResult[] testSweep = thresholds
            .Select(threshold => EvaluateThreshold(dataset.Test, gallery, threshold))
            .ToArray();

        EvaluationThresholdResult selected = validationSweep
            .OrderByDescending(result => result.Metrics.BalancedIdentityScore)
            .ThenByDescending(result => result.Metrics.IdentificationPrecision)
            .ThenByDescending(result => result.Metrics.UnknownRejectionRate)
            .ThenByDescending(result => result.Threshold)
            .First();

        EvaluationSplitReport validation = EvaluateSplit(
            "validation",
            dataset.Validation,
            gallery,
            selected.Threshold);
        EvaluationSplitReport test = EvaluateSplit(
            "test",
            dataset.Test,
            gallery,
            selected.Threshold);
        EvaluationArchiveProjection? projection = CreateProjection(test.Metrics, options);

        return new EvaluationReport(
            ReportSchemaVersion,
            dataset.DatasetId,
            Convert.ToHexString(SHA256.HashData(datasetBytes)).ToLowerInvariant(),
            dataset.PipelineVersion,
            new EvaluationModelReport(dataset.Detector.ModelId, dataset.Detector.ModelHash, null),
            new EvaluationModelReport(
                dataset.Embedder.ModelId,
                dataset.Embedder.ModelHash,
                dataset.Embedder.Dimensions),
            SelectionPolicy,
            "validation",
            selected.Threshold,
            validationSweep,
            testSweep,
            validation,
            test,
            projection);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<EmbeddingVector>> BuildGallery(
        EvaluationDataset dataset)
    {
        return dataset.Gallery
            .OrderBy(item => item.PersonId, StringComparer.Ordinal)
            .ThenBy(item => item.FaceId, StringComparer.Ordinal)
            .GroupBy(item => item.PersonId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EmbeddingVector>)group
                    .Select(item => new EmbeddingVector(item.Embedding))
                    .ToArray(),
                StringComparer.Ordinal);
    }

    private static EvaluationThresholdResult EvaluateThreshold(
        IReadOnlyList<EvaluationSample> samples,
        IReadOnlyDictionary<string, IReadOnlyList<EmbeddingVector>> gallery,
        double threshold)
    {
        EvaluationOutcome[] outcomes = samples
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .Select(sample => EvaluateSample(sample, gallery, threshold))
            .ToArray();
        return new EvaluationThresholdResult(threshold, CalculateMetrics(outcomes));
    }

    private static EvaluationSplitReport EvaluateSplit(
        string split,
        IReadOnlyList<EvaluationSample> samples,
        IReadOnlyDictionary<string, IReadOnlyList<EmbeddingVector>> gallery,
        double threshold)
    {
        EvaluationOutcome[] outcomes = samples
            .OrderBy(sample => sample.SampleId, StringComparer.Ordinal)
            .Select(sample => EvaluateSample(sample, gallery, threshold))
            .ToArray();
        EvaluationConfusionRow[] confusion = outcomes
            .GroupBy(
                outcome => new ConfusionKey(
                    outcome.ExpectedPersonId ?? "<unknown>",
                    outcome.PredictedLabel),
                outcome => outcome)
            .Select(group => new EvaluationConfusionRow(
                group.Key.Expected,
                group.Key.Predicted,
                group.Count()))
            .OrderBy(row => row.Expected, StringComparer.Ordinal)
            .ThenBy(row => row.Predicted, StringComparer.Ordinal)
            .ToArray();

        return new EvaluationSplitReport(split, threshold, CalculateMetrics(outcomes), confusion);
    }

    private static EvaluationOutcome EvaluateSample(
        EvaluationSample sample,
        IReadOnlyDictionary<string, IReadOnlyList<EmbeddingVector>> gallery,
        double threshold)
    {
        if (!sample.FaceDetected)
        {
            return new EvaluationOutcome(
                sample.SampleId,
                sample.ExpectedPersonId,
                "<missed>",
                Accepted: false,
                CorrectKnown: false,
                UnknownRejected: sample.ExpectedPersonId is null,
                sample.FaceExpected,
                sample.FaceDetected,
                sample.ElapsedMilliseconds);
        }

        EmbeddingVector target = new(sample.Embedding!);
        Candidate[] candidates = gallery
            .Select(pair => new Candidate(
                pair.Key,
                pair.Value.Max(exemplar => target.CosineSimilarity(exemplar))))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.PersonId, StringComparer.Ordinal)
            .ToArray();
        Candidate best = candidates[0];
        bool accepted = best.Score >= threshold;
        string predicted = accepted ? best.PersonId : "<rejected>";
        bool correctKnown = accepted && string.Equals(
            sample.ExpectedPersonId,
            best.PersonId,
            StringComparison.Ordinal);
        bool unknownRejected = sample.ExpectedPersonId is null && !accepted;

        return new EvaluationOutcome(
            sample.SampleId,
            sample.ExpectedPersonId,
            predicted,
            accepted,
            correctKnown,
            unknownRejected,
            sample.FaceExpected,
            sample.FaceDetected,
            sample.ElapsedMilliseconds);
    }

    private static EvaluationMetrics CalculateMetrics(IReadOnlyList<EvaluationOutcome> outcomes)
    {
        int expectedFaceCount = outcomes.Count(outcome => outcome.FaceExpected);
        int detectedExpectedFaceCount = outcomes.Count(
            outcome => outcome.FaceExpected && outcome.FaceDetected);
        int knownCount = outcomes.Count(outcome => outcome.ExpectedPersonId is not null);
        int unknownCount = outcomes.Count(outcome => outcome.ExpectedPersonId is null);
        int acceptedPredictionCount = outcomes.Count(outcome => outcome.Accepted);
        int correctKnownCount = outcomes.Count(outcome => outcome.CorrectKnown);
        int unknownRejectedCount = outcomes.Count(outcome => outcome.UnknownRejected);
        double elapsedMilliseconds = outcomes.Sum(outcome => outcome.ElapsedMilliseconds);

        double detectorRecall = Ratio(detectedExpectedFaceCount, expectedFaceCount);
        double identificationPrecision = Ratio(correctKnownCount, acceptedPredictionCount);
        double knownIdentificationRecall = Ratio(correctKnownCount, knownCount);
        double unknownRejectionRate = Ratio(unknownRejectedCount, unknownCount);
        double balancedIdentityScore = (knownIdentificationRecall + unknownRejectionRate) / 2d;
        double imagesPerSecond = elapsedMilliseconds > 0
            ? outcomes.Count * 1000d / elapsedMilliseconds
            : 0;

        return new EvaluationMetrics(
            outcomes.Count,
            expectedFaceCount,
            detectedExpectedFaceCount,
            detectorRecall,
            knownCount,
            unknownCount,
            acceptedPredictionCount,
            correctKnownCount,
            identificationPrecision,
            knownIdentificationRecall,
            unknownRejectedCount,
            unknownRejectionRate,
            balancedIdentityScore,
            elapsedMilliseconds,
            imagesPerSecond);
    }

    private static EvaluationArchiveProjection? CreateProjection(
        EvaluationMetrics testMetrics,
        EvaluationCommandOptions options)
    {
        if (options.ArchiveImages is null)
        {
            return null;
        }

        if (testMetrics.ImagesPerSecond <= 0)
        {
            throw new ArgumentException("Measured test throughput must be positive for archive projection.");
        }

        double estimatedHours = options.ArchiveImages.Value / testMetrics.ImagesPerSecond / 3600d;
        decimal? estimatedCost = options.HourlyCost is null
            ? null
            : options.HourlyCost.Value * (decimal)estimatedHours;
        return new EvaluationArchiveProjection(
            options.ArchiveImages.Value,
            testMetrics.ImagesPerSecond,
            estimatedHours,
            options.Currency,
            options.HourlyCost,
            estimatedCost);
    }

    private static void ValidateDataset(EvaluationDataset dataset)
    {
        if (dataset.SchemaVersion != DatasetSchemaVersion)
        {
            throw new ArgumentException(
                $"Unsupported schemaVersion {dataset.SchemaVersion}; expected {DatasetSchemaVersion}.");
        }

        Required(dataset.DatasetId, "datasetId");
        Required(dataset.PipelineVersion, "pipelineVersion");
        ValidateModel(dataset.Detector, "detector");
        ValidateModel(dataset.Embedder, "embedder");
        if (dataset.Embedder.Dimensions <= 0)
        {
            throw new ArgumentException("embedder.dimensions must be greater than zero.");
        }

        if (dataset.Thresholds.Count == 0)
        {
            throw new ArgumentException("At least one threshold is required.");
        }

        foreach (double threshold in dataset.Thresholds)
        {
            if (!double.IsFinite(threshold) || threshold is < -1 or > 1)
            {
                throw new ArgumentException("Thresholds must be finite cosine scores between -1 and 1.");
            }
        }

        if (dataset.Thresholds.Distinct().Count() != dataset.Thresholds.Count)
        {
            throw new ArgumentException("Thresholds must be unique.");
        }

        if (dataset.Gallery.Count == 0)
        {
            throw new ArgumentException("The gallery split must contain at least one exemplar.");
        }

        HashSet<string> galleryFaceIds = new(StringComparer.Ordinal);
        HashSet<string> galleryPeople = new(StringComparer.Ordinal);
        foreach (EvaluationGalleryItem item in dataset.Gallery)
        {
            Required(item.FaceId, "gallery.faceId");
            Required(item.PersonId, "gallery.personId");
            if (!galleryFaceIds.Add(item.FaceId))
            {
                throw new ArgumentException($"Duplicate gallery faceId '{item.FaceId}'.");
            }

            galleryPeople.Add(item.PersonId);
            ValidateEmbedding(item.Embedding, dataset.Embedder.Dimensions, $"gallery '{item.FaceId}'");
        }

        HashSet<string> sampleIds = new(StringComparer.Ordinal);
        ValidateSplit("validation", dataset.Validation, dataset.Embedder.Dimensions, galleryPeople, sampleIds);
        ValidateSplit("test", dataset.Test, dataset.Embedder.Dimensions, galleryPeople, sampleIds);
        if (sampleIds.Overlaps(galleryFaceIds))
        {
            throw new ArgumentException("Gallery face IDs must not be reused as validation or test sample IDs.");
        }
    }

    private static void ValidateSplit(
        string split,
        IReadOnlyList<EvaluationSample> samples,
        int dimensions,
        IReadOnlySet<string> galleryPeople,
        ISet<string> sampleIds)
    {
        if (samples.Count == 0)
        {
            throw new ArgumentException($"The {split} split cannot be empty.");
        }

        foreach (EvaluationSample sample in samples)
        {
            Required(sample.SampleId, $"{split}.sampleId");
            if (!sampleIds.Add(sample.SampleId))
            {
                throw new ArgumentException(
                    $"Sample ID '{sample.SampleId}' is reused across evaluation splits.");
            }

            if (!string.IsNullOrWhiteSpace(sample.ExpectedPersonId) &&
                !galleryPeople.Contains(sample.ExpectedPersonId))
            {
                throw new ArgumentException(
                    $"{split} sample '{sample.SampleId}' references a person absent from the gallery.");
            }

            if (!sample.FaceExpected && !string.IsNullOrWhiteSpace(sample.ExpectedPersonId))
            {
                throw new ArgumentException(
                    $"{split} sample '{sample.SampleId}' cannot expect a person when no face is expected.");
            }

            if (sample.FaceDetected)
            {
                ValidateEmbedding(sample.Embedding, dimensions, $"{split} sample '{sample.SampleId}'");
            }
            else if (sample.Embedding is { Length: > 0 })
            {
                throw new ArgumentException(
                    $"{split} sample '{sample.SampleId}' cannot contain an embedding when no face was detected.");
            }

            if (!double.IsFinite(sample.ElapsedMilliseconds) || sample.ElapsedMilliseconds <= 0)
            {
                throw new ArgumentException(
                    $"{split} sample '{sample.SampleId}' must have positive elapsedMilliseconds.");
            }
        }

        if (!samples.Any(sample => !string.IsNullOrWhiteSpace(sample.ExpectedPersonId)))
        {
            throw new ArgumentException($"The {split} split must contain at least one known person.");
        }

        if (!samples.Any(sample => string.IsNullOrWhiteSpace(sample.ExpectedPersonId)))
        {
            throw new ArgumentException($"The {split} split must contain at least one unknown example.");
        }
    }

    private static void ValidateModel(EvaluationModelDescriptor model, string name)
    {
        Required(model.ModelId, $"{name}.modelId");
        Required(model.ModelHash, $"{name}.modelHash");
        if (model.ModelHash.Length != 64 ||
            !model.ModelHash.All(character => Uri.IsHexDigit(character)))
        {
            throw new ArgumentException($"{name}.modelHash must be a 64-character SHA-256 value.");
        }
    }

    private static void ValidateEmbedding(float[]? embedding, int dimensions, string location)
    {
        if (embedding is null || embedding.Length != dimensions)
        {
            throw new ArgumentException(
                $"The embedding for {location} must contain exactly {dimensions} components.");
        }

        try
        {
            _ = new EmbeddingVector(embedding);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"The embedding for {location} is invalid: {exception.Message}", exception);
        }
    }

    private static void Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.");
        }
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : (double)numerator / denominator;

    private static async Task WriteAtomicAsync(
        string outputPath,
        string content,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = outputPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record Candidate(string PersonId, double Score);

    private sealed record ConfusionKey(string Expected, string Predicted);

    private sealed record EvaluationOutcome(
        string SampleId,
        string? ExpectedPersonId,
        string PredictedLabel,
        bool Accepted,
        bool CorrectKnown,
        bool UnknownRejected,
        bool FaceExpected,
        bool FaceDetected,
        double ElapsedMilliseconds);
}

internal sealed record EvaluationCommandOptions(
    string DatasetPath,
    string OutputPath,
    long? ArchiveImages,
    decimal? HourlyCost,
    string Currency)
{
    public static EvaluationCommandOptions Parse(string[] args)
    {
        string? dataset = null;
        string? output = null;
        long? archiveImages = null;
        decimal? hourlyCost = null;
        string currency = "GBP";

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--dataset":
                    dataset = Single(dataset, value, option);
                    break;
                case "--output":
                    output = Single(output, value, option);
                    break;
                case "--archive-images":
                    if (archiveImages is not null ||
                        !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedImages) ||
                        parsedImages <= 0)
                    {
                        throw new ArgumentException("--archive-images must be supplied once as a positive integer.");
                    }

                    archiveImages = parsedImages;
                    break;
                case "--hourly-cost":
                    if (hourlyCost is not null ||
                        !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedCost) ||
                        parsedCost < 0)
                    {
                        throw new ArgumentException("--hourly-cost must be supplied once as a non-negative amount.");
                    }

                    hourlyCost = parsedCost;
                    break;
                case "--currency":
                    currency = Single(currency == "GBP" ? null : currency, value, option).Trim().ToUpperInvariant();
                    if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
                    {
                        throw new ArgumentException("--currency must be a three-letter code.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (dataset is null)
        {
            throw new ArgumentException("--dataset is required.");
        }

        if (hourlyCost is not null && archiveImages is null)
        {
            throw new ArgumentException("--hourly-cost requires --archive-images.");
        }

        output ??= Path.Combine(".artifacts", "model-lab", "evaluation-report.json");
        return new EvaluationCommandOptions(dataset, output, archiveImages, hourlyCost, currency);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return value;
    }
}

internal sealed record EvaluationDataset
{
    public int SchemaVersion { get; init; }
    public string DatasetId { get; init; } = string.Empty;
    public string PipelineVersion { get; init; } = string.Empty;
    public EvaluationModelDescriptor Detector { get; init; } = new();
    public EvaluationEmbeddingModelDescriptor Embedder { get; init; } = new();
    public List<double> Thresholds { get; init; } = [];
    public List<EvaluationGalleryItem> Gallery { get; init; } = [];
    public List<EvaluationSample> Validation { get; init; } = [];
    public List<EvaluationSample> Test { get; init; } = [];
}

internal record EvaluationModelDescriptor
{
    public string ModelId { get; init; } = string.Empty;
    public string ModelHash { get; init; } = string.Empty;
}

internal sealed record EvaluationEmbeddingModelDescriptor : EvaluationModelDescriptor
{
    public int Dimensions { get; init; }
}

internal sealed record EvaluationGalleryItem
{
    public string FaceId { get; init; } = string.Empty;
    public string PersonId { get; init; } = string.Empty;
    public float[] Embedding { get; init; } = [];
}

internal sealed record EvaluationSample
{
    public string SampleId { get; init; } = string.Empty;
    public string? ExpectedPersonId { get; init; }
    public bool FaceExpected { get; init; } = true;
    public bool FaceDetected { get; init; }
    public float[]? Embedding { get; init; }
    public double ElapsedMilliseconds { get; init; }
}

internal sealed record EvaluationReport(
    int SchemaVersion,
    string DatasetId,
    string InputSha256,
    string PipelineVersion,
    EvaluationModelReport Detector,
    EvaluationModelReport Embedder,
    string ThresholdSelectionPolicy,
    string ThresholdSelectionSplit,
    double SelectedThreshold,
    IReadOnlyList<EvaluationThresholdResult> ValidationThresholdSweep,
    IReadOnlyList<EvaluationThresholdResult> TestThresholdSweep,
    EvaluationSplitReport Validation,
    EvaluationSplitReport Test,
    EvaluationArchiveProjection? ArchiveProjection);

internal sealed record EvaluationModelReport(
    string ModelId,
    string ModelHash,
    int? Dimensions);

internal sealed record EvaluationThresholdResult(
    double Threshold,
    EvaluationMetrics Metrics);

internal sealed record EvaluationSplitReport(
    string Split,
    double Threshold,
    EvaluationMetrics Metrics,
    IReadOnlyList<EvaluationConfusionRow> Confusion);

internal sealed record EvaluationMetrics(
    int SampleCount,
    int ExpectedFaceCount,
    int DetectedExpectedFaceCount,
    double DetectorRecall,
    int KnownCount,
    int UnknownCount,
    int AcceptedPredictionCount,
    int CorrectKnownCount,
    double IdentificationPrecision,
    double KnownIdentificationRecall,
    int UnknownRejectedCount,
    double UnknownRejectionRate,
    double BalancedIdentityScore,
    double TotalElapsedMilliseconds,
    double ImagesPerSecond);

internal sealed record EvaluationConfusionRow(
    string Expected,
    string Predicted,
    int Count);

internal sealed record EvaluationArchiveProjection(
    long ArchiveImageCount,
    double MeasuredImagesPerSecond,
    double EstimatedHours,
    string Currency,
    decimal? HourlyCost,
    decimal? EstimatedCost);
