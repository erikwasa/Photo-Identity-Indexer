using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal static class PhotoQualityModelEvaluation
{
    public const string ProtocolVersion = "m37-quality-calibration-v1";
    public const double VeryLowObservedPrecisionTarget = 0.98d;

    public static PhotoQualityCalibrationReport EvaluateCalibration(
        PhotoQualityBenchmarkExport benchmark,
        PhotoQualityCandidateScoreExport scoreExport)
    {
        ValidateScoreExport(benchmark, scoreExport);

        Dictionary<string, PhotoQualityCandidateScore> scoreByRevision =
            scoreExport.Scores.ToDictionary(
                score => score.RevisionId,
                StringComparer.OrdinalIgnoreCase);

        PhotoQualityBenchmarkPhoto[] calibration = benchmark.Photos
            .Where(IsRepresentativeCalibration)
            .ToArray();

        CalibrationRow[] reviewed = calibration
            .Select(photo => TryCreateRow(photo, scoreByRevision[photo.RevisionId]))
            .Where(row => row is not null)
            .Select(row => row!)
            .ToArray();

        int reviewedWithKnownTier = calibration.Count(
            photo => TryHumanOrdinal(photo.Review.QualityLevel, out _));
        int inferenceFailures = reviewed.Count(row => !row.Score.HasValue);
        CalibrationRow[] scored = reviewed
            .Where(row => row.Score.HasValue)
            .ToArray();

        IReadOnlyDictionary<string, int> labelCounts = calibration
            .Where(photo => TryHumanOrdinal(photo.Review.QualityLevel, out _))
            .GroupBy(
                photo => photo.Review.QualityLevel!,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => HumanOrdinal(group.Key))
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        double? spearman = scored.Length >= 2
            ? Spearman(
                scored.Select(row => row.HumanOrdinal).ToArray(),
                scored.Select(row => DirectionalScore(
                    row.Score!.Value,
                    scoreExport.HigherIsBetter)).ToArray())
            : null;

        PhotoQualityPairwiseAgreement pairwise =
            PairwiseAgreement(scored, scoreExport.HigherIsBetter);

        int actualVeryLow = scored.Count(row => row.HumanOrdinal == 1);
        PhotoQualityVeryLowThresholdSummary? bestVeryLow =
            BestVeryLowThreshold(
                scored,
                scoreExport.HigherIsBetter,
                actualVeryLow,
                VeryLowObservedPrecisionTarget);

        return new PhotoQualityCalibrationReport(
            SchemaVersion: 1,
            ProtocolVersion,
            BenchmarkId: benchmark.BenchmarkId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Candidate: scoreExport.Candidate,
            InputKind: scoreExport.InputKind,
            HigherIsBetter: scoreExport.HigherIsBetter,
            RepresentativeCalibrationCount: calibration.Length,
            ReviewedKnownTierCount: reviewedWithKnownTier,
            ScoredReviewedCount: scored.Length,
            ReviewedInferenceFailureCount: inferenceFailures,
            LabelCounts: labelCounts,
            SpearmanRankCorrelation: spearman,
            PairwiseTwoTierAgreement: pairwise,
            VeryLowReviewedCount: actualVeryLow,
            VeryLowObservedPrecisionTarget,
            BestObservedVeryLowThresholdAtOrAboveTarget: bestVeryLow,
            Runtime: scoreExport.Runtime,
            Notes:
                "Calibration-only report. Challenge photos, Unsure labels and every held-out human label are excluded. A threshold that reaches the observed precision target is diagnostic evidence only; Wilson lower bound and sample count must be considered before any safety claim.");
    }

    public static void ValidateScoreExport(
        PhotoQualityBenchmarkExport benchmark,
        PhotoQualityCandidateScoreExport scoreExport)
    {
        if (scoreExport.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported quality score schemaVersion {scoreExport.SchemaVersion}; expected 1.");
        }

        if (!string.Equals(
                benchmark.BenchmarkId,
                scoreExport.BenchmarkId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Candidate scores belong to a different benchmarkId.");
        }

        if (string.IsNullOrWhiteSpace(scoreExport.Candidate.CandidateId) ||
            string.IsNullOrWhiteSpace(scoreExport.Candidate.ModelVersion) ||
            string.IsNullOrWhiteSpace(scoreExport.Candidate.Preprocessing))
        {
            throw new InvalidDataException(
                "Candidate score metadata must include candidateId, modelVersion and preprocessing.");
        }

        if (scoreExport.Scores is null)
        {
            throw new InvalidDataException("Candidate score export has no scores collection.");
        }

        string[] duplicates = scoreExport.Scores
            .GroupBy(score => score.RevisionId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Take(5)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidDataException(
                $"Candidate score export contains duplicate revision IDs: {string.Join(", ", duplicates)}.");
        }

        HashSet<string> expected = benchmark.Photos
            .Select(photo => photo.RevisionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> actual = scoreExport.Scores
            .Select(score => score.RevisionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] missing = expected.Except(actual).Take(5).ToArray();
        string[] unknown = actual.Except(expected).Take(5).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new InvalidDataException(
                $"Candidate score coverage must exactly match the benchmark. Missing={string.Join(", ", missing)}; unknown={string.Join(", ", unknown)}.");
        }

        foreach (PhotoQualityCandidateScore score in scoreExport.Scores)
        {
            if (score.RawScore.HasValue &&
                (double.IsNaN(score.RawScore.Value) ||
                 double.IsInfinity(score.RawScore.Value)))
            {
                throw new InvalidDataException(
                    $"Candidate score for revision {score.RevisionId} is not finite.");
            }

            if (!score.RawScore.HasValue && string.IsNullOrWhiteSpace(score.ErrorCode))
            {
                throw new InvalidDataException(
                    $"Candidate score failure for revision {score.RevisionId} must include errorCode.");
            }

            if (score.InferenceMilliseconds < 0d ||
                double.IsNaN(score.InferenceMilliseconds) ||
                double.IsInfinity(score.InferenceMilliseconds))
            {
                throw new InvalidDataException(
                    $"Candidate runtime for revision {score.RevisionId} is invalid.");
            }
        }
    }

    private static bool IsRepresentativeCalibration(PhotoQualityBenchmarkPhoto photo) =>
        !string.Equals(
            photo.SampleKind,
            "challenge",
            StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(
            photo.Partition,
            "challenge",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            photo.Partition,
            "calibration",
            StringComparison.OrdinalIgnoreCase);

    private static CalibrationRow? TryCreateRow(
        PhotoQualityBenchmarkPhoto photo,
        PhotoQualityCandidateScore score)
    {
        if (!TryHumanOrdinal(photo.Review.QualityLevel, out int ordinal))
        {
            return null;
        }

        return new CalibrationRow(
            photo.RevisionId,
            ordinal,
            score.RawScore);
    }

    private static bool TryHumanOrdinal(string? qualityLevel, out int ordinal)
    {
        ordinal = qualityLevel?.Trim().ToLowerInvariant() switch
        {
            "very-low" => 1,
            "low" => 2,
            "normal" => 3,
            "high" => 4,
            "very-high" => 5,
            _ => 0,
        };
        return ordinal > 0;
    }

    private static int HumanOrdinal(string qualityLevel)
    {
        if (!TryHumanOrdinal(qualityLevel, out int ordinal))
        {
            throw new InvalidDataException(
                $"Unknown human quality level '{qualityLevel}'.");
        }

        return ordinal;
    }

    private static double DirectionalScore(
        double rawScore,
        bool higherIsBetter) =>
        higherIsBetter ? rawScore : -rawScore;

    private static double? Spearman(
        IReadOnlyList<int> human,
        IReadOnlyList<double> candidate)
    {
        double[] humanRanks = AverageRanks(
            human.Select(value => (double)value).ToArray());
        double[] candidateRanks = AverageRanks(candidate.ToArray());
        return Pearson(humanRanks, candidateRanks);
    }

    private static double[] AverageRanks(double[] values)
    {
        int[] order = Enumerable.Range(0, values.Length)
            .OrderBy(index => values[index])
            .ThenBy(index => index)
            .ToArray();
        double[] ranks = new double[values.Length];

        int start = 0;
        while (start < order.Length)
        {
            int end = start + 1;
            while (end < order.Length &&
                   values[order[end]].Equals(values[order[start]]))
            {
                end++;
            }

            double averageRank = ((start + 1) + end) / 2d;
            for (int index = start; index < end; index++)
            {
                ranks[order[index]] = averageRank;
            }

            start = end;
        }

        return ranks;
    }

    private static double? Pearson(double[] left, double[] right)
    {
        double leftMean = left.Average();
        double rightMean = right.Average();
        double numerator = 0d;
        double leftVariance = 0d;
        double rightVariance = 0d;
        for (int index = 0; index < left.Length; index++)
        {
            double leftDelta = left[index] - leftMean;
            double rightDelta = right[index] - rightMean;
            numerator += leftDelta * rightDelta;
            leftVariance += leftDelta * leftDelta;
            rightVariance += rightDelta * rightDelta;
        }

        double denominator = Math.Sqrt(leftVariance * rightVariance);
        return denominator <= 0d ? null : numerator / denominator;
    }

    private static PhotoQualityPairwiseAgreement PairwiseAgreement(
        CalibrationRow[] rows,
        bool higherIsBetter)
    {
        int total = 0;
        int correct = 0;
        int ties = 0;
        for (int leftIndex = 0; leftIndex < rows.Length; leftIndex++)
        {
            for (int rightIndex = leftIndex + 1;
                 rightIndex < rows.Length;
                 rightIndex++)
            {
                CalibrationRow left = rows[leftIndex];
                CalibrationRow right = rows[rightIndex];
                int humanDelta = left.HumanOrdinal - right.HumanOrdinal;
                if (Math.Abs(humanDelta) < 2)
                {
                    continue;
                }

                total++;
                double candidateDelta = DirectionalScore(
                    left.Score!.Value,
                    higherIsBetter) - DirectionalScore(
                    right.Score!.Value,
                    higherIsBetter);
                if (candidateDelta == 0d)
                {
                    ties++;
                    continue;
                }

                if (Math.Sign(candidateDelta) == Math.Sign(humanDelta))
                {
                    correct++;
                }
            }
        }

        return new PhotoQualityPairwiseAgreement(
            TotalPairs: total,
            CorrectPairs: correct,
            TiedPairs: ties,
            AgreementFraction: total == 0 ? null : correct / (double)total);
    }

    private static PhotoQualityVeryLowThresholdSummary? BestVeryLowThreshold(
        CalibrationRow[] rows,
        bool higherIsBetter,
        int actualVeryLow,
        double targetPrecision)
    {
        double[] thresholds = rows
            .Select(row => row.Score!.Value)
            .Distinct()
            .OrderBy(value => higherIsBetter ? value : -value)
            .ToArray();

        List<PhotoQualityVeryLowThresholdSummary> eligible = [];
        foreach (double threshold in thresholds)
        {
            CalibrationRow[] predicted = rows
                .Where(row => higherIsBetter
                    ? row.Score!.Value <= threshold
                    : row.Score!.Value >= threshold)
                .ToArray();
            if (predicted.Length == 0)
            {
                continue;
            }

            int trueVeryLow = predicted.Count(row => row.HumanOrdinal == 1);
            int falsePositive = predicted.Length - trueVeryLow;
            double precision = trueVeryLow / (double)predicted.Length;
            if (precision + 1e-12 < targetPrecision)
            {
                continue;
            }

            eligible.Add(new PhotoQualityVeryLowThresholdSummary(
                Threshold: threshold,
                PredictedVeryLowCount: predicted.Length,
                TrueVeryLowCount: trueVeryLow,
                FalsePositiveCount: falsePositive,
                ObservedPrecision: precision,
                Wilson95LowerBound: WilsonLowerBound(
                    trueVeryLow,
                    predicted.Length),
                Recall: actualVeryLow == 0
                    ? null
                    : trueVeryLow / (double)actualVeryLow));
        }

        return eligible
            .OrderByDescending(summary => summary.TrueVeryLowCount)
            .ThenByDescending(summary => summary.Wilson95LowerBound)
            .ThenByDescending(summary => summary.PredictedVeryLowCount)
            .FirstOrDefault();
    }

    private static double WilsonLowerBound(int successes, int total)
    {
        if (total == 0)
        {
            return 0d;
        }

        const double z = 1.959963984540054d;
        double proportion = successes / (double)total;
        double zSquared = z * z;
        double denominator = 1d + zSquared / total;
        double centre = proportion + zSquared / (2d * total);
        double margin = z * Math.Sqrt(
            (proportion * (1d - proportion) / total) +
            (zSquared / (4d * total * total)));
        return Math.Max(0d, (centre - margin) / denominator);
    }

    private sealed record CalibrationRow(
        string RevisionId,
        int HumanOrdinal,
        double? Score);
}

internal static class PhotoQualityTechnicalBaseline
{
    public const string CandidateId = "wi0182-technical-reason-count-v1";

    public static double Score(TechnicalPhotoQualityEvidence evidence)
    {
        if (!string.Equals(
                evidence.AlgorithmVersion,
                TechnicalPhotoQualityProtocol.AlgorithmVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                evidence.CalibrationVersion,
                TechnicalPhotoQualityProtocol.CalibratedProfileVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "WI-0182 baseline scoring requires calibrated proxy-technical-quality-v1 evidence.");
        }

        return Math.Max(0d, 100d - (10d * evidence.Reasons.Count));
    }
}

internal sealed record PhotoQualityCandidateScoreExport(
    int SchemaVersion,
    string BenchmarkId,
    DateTimeOffset GeneratedAtUtc,
    PhotoQualityCandidateDescriptor Candidate,
    string InputKind,
    bool HigherIsBetter,
    int PhotoCount,
    PhotoQualityCandidateRuntimeSummary Runtime,
    IReadOnlyList<PhotoQualityCandidateScore> Scores);

internal sealed record PhotoQualityCandidateDescriptor(
    string CandidateId,
    string ModelName,
    string ModelVersion,
    string Framework,
    string License,
    string Source,
    string Preprocessing,
    string ScoreSemantics,
    long? ModelSizeBytes,
    string ExecutionProvider);

internal sealed record PhotoQualityCandidateRuntimeSummary(
    double TotalElapsedMilliseconds,
    double MedianInferenceMilliseconds,
    double P95InferenceMilliseconds);

internal sealed record PhotoQualityCandidateScore(
    string RevisionId,
    double? RawScore,
    double InferenceMilliseconds,
    string? ErrorCode,
    IReadOnlyList<string>? Diagnostics);

internal sealed record PhotoQualityCalibrationReport(
    int SchemaVersion,
    string ProtocolVersion,
    string BenchmarkId,
    DateTimeOffset GeneratedAtUtc,
    PhotoQualityCandidateDescriptor Candidate,
    string InputKind,
    bool HigherIsBetter,
    int RepresentativeCalibrationCount,
    int ReviewedKnownTierCount,
    int ScoredReviewedCount,
    int ReviewedInferenceFailureCount,
    IReadOnlyDictionary<string, int> LabelCounts,
    double? SpearmanRankCorrelation,
    PhotoQualityPairwiseAgreement PairwiseTwoTierAgreement,
    int VeryLowReviewedCount,
    double VeryLowObservedPrecisionTarget,
    PhotoQualityVeryLowThresholdSummary? BestObservedVeryLowThresholdAtOrAboveTarget,
    PhotoQualityCandidateRuntimeSummary Runtime,
    string Notes);

internal sealed record PhotoQualityPairwiseAgreement(
    int TotalPairs,
    int CorrectPairs,
    int TiedPairs,
    double? AgreementFraction);

internal sealed record PhotoQualityVeryLowThresholdSummary(
    double Threshold,
    int PredictedVeryLowCount,
    int TrueVeryLowCount,
    int FalsePositiveCount,
    double ObservedPrecision,
    double Wilson95LowerBound,
    double? Recall);
