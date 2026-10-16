using PhotoIdentity.Core.Imaging;
using PhotoIdentity.PhotoQualityEvaluation;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class PhotoQualityModelEvaluationTests
{
    [Fact]
    public void Calibration_report_uses_only_representative_calibration_labels()
    {
        PhotoQualityBenchmarkExport benchmark = Manifest(
            Photo("cal-vlow", "calibration", "very-low"),
            Photo("cal-low", "calibration", "low"),
            Photo("cal-high", "calibration", "high"),
            Photo("cal-unsure", "calibration", "unsure"),
            Photo("held-high", "held-out", "high"),
            Photo("held-vlow", "held-out", "very-low"),
            Photo("challenge", "challenge", "very-low", "challenge"));

        PhotoQualityCandidateScoreExport scores = ScoreExport(
            benchmark,
            higherIsBetter: true,
            new Dictionary<string, double?>
            {
                ["cal-vlow"] = 10d,
                ["cal-low"] = 20d,
                ["cal-high"] = 80d,
                ["cal-unsure"] = 90d,
                ["held-high"] = 0d,
                ["held-vlow"] = 100d,
                ["challenge"] = 100d,
            });

        PhotoQualityCalibrationReport report =
            PhotoQualityModelEvaluation.EvaluateCalibration(
                benchmark,
                scores);

        Assert.Equal(4, report.RepresentativeCalibrationCount);
        Assert.Equal(3, report.ReviewedKnownTierCount);
        Assert.Equal(3, report.ScoredReviewedCount);
        Assert.Equal(0, report.ReviewedInferenceFailureCount);
        Assert.True(report.SpearmanRankCorrelation.HasValue);
        Assert.Equal(1d, report.SpearmanRankCorrelation.Value, 10);
        Assert.Equal(2, report.PairwiseTwoTierAgreement.TotalPairs);
        Assert.Equal(2, report.PairwiseTwoTierAgreement.CorrectPairs);
        Assert.Equal(0, report.PairwiseTwoTierAgreement.TiedPairs);
        Assert.True(report.PairwiseTwoTierAgreement.AgreementFraction.HasValue);
        Assert.Equal(
            1d,
            report.PairwiseTwoTierAgreement.AgreementFraction.Value,
            10);
        Assert.Equal(1, report.VeryLowReviewedCount);

        PhotoQualityVeryLowThresholdSummary threshold =
            Assert.IsType<PhotoQualityVeryLowThresholdSummary>(
                report.BestObservedVeryLowThresholdAtOrAboveTarget);
        Assert.Equal(10d, threshold.Threshold);
        Assert.Equal(1, threshold.PredictedVeryLowCount);
        Assert.Equal(1, threshold.TrueVeryLowCount);
        Assert.Equal(0, threshold.FalsePositiveCount);
        Assert.Equal(1d, threshold.ObservedPrecision);
        Assert.InRange(threshold.Wilson95LowerBound, 0.20d, 0.21d);
        Assert.True(threshold.Recall.HasValue);
        Assert.Equal(1d, threshold.Recall.Value, 10);
    }

    [Fact]
    public void Calibration_report_respects_lower_is_better_candidates()
    {
        PhotoQualityBenchmarkExport benchmark = Manifest(
            Photo("vlow", "calibration", "very-low"),
            Photo("normal", "calibration", "normal"),
            Photo("high", "calibration", "high"));

        PhotoQualityCandidateScoreExport scores = ScoreExport(
            benchmark,
            higherIsBetter: false,
            new Dictionary<string, double?>
            {
                ["vlow"] = 90d,
                ["normal"] = 50d,
                ["high"] = 10d,
            });

        PhotoQualityCalibrationReport report =
            PhotoQualityModelEvaluation.EvaluateCalibration(
                benchmark,
                scores);

        Assert.True(report.SpearmanRankCorrelation.HasValue);
        Assert.Equal(1d, report.SpearmanRankCorrelation.Value, 10);
        Assert.Equal(2, report.PairwiseTwoTierAgreement.TotalPairs);
        Assert.Equal(2, report.PairwiseTwoTierAgreement.CorrectPairs);

        PhotoQualityVeryLowThresholdSummary threshold =
            Assert.IsType<PhotoQualityVeryLowThresholdSummary>(
                report.BestObservedVeryLowThresholdAtOrAboveTarget);
        Assert.Equal(90d, threshold.Threshold);
        Assert.Equal(1, threshold.TrueVeryLowCount);
        Assert.Equal(0, threshold.FalsePositiveCount);
    }

    [Fact]
    public void Calibration_report_records_inference_failures_without_treating_them_as_scores()
    {
        PhotoQualityBenchmarkExport benchmark = Manifest(
            Photo("vlow", "calibration", "very-low"),
            Photo("high", "calibration", "high"));

        PhotoQualityCandidateScoreExport scores = ScoreExport(
            benchmark,
            higherIsBetter: true,
            new Dictionary<string, double?>
            {
                ["vlow"] = null,
                ["high"] = 80d,
            });

        scores = scores with
        {
            Scores = scores.Scores
                .Select(score => score.RevisionId == "vlow"
                    ? score with { ErrorCode = "inference-failed" }
                    : score)
                .ToArray(),
        };

        PhotoQualityCalibrationReport report =
            PhotoQualityModelEvaluation.EvaluateCalibration(
                benchmark,
                scores);

        Assert.Equal(2, report.ReviewedKnownTierCount);
        Assert.Equal(1, report.ScoredReviewedCount);
        Assert.Equal(1, report.ReviewedInferenceFailureCount);
        Assert.Null(report.SpearmanRankCorrelation);
        Assert.Equal(0, report.PairwiseTwoTierAgreement.TotalPairs);
        Assert.Null(report.BestObservedVeryLowThresholdAtOrAboveTarget);
    }

    [Fact]
    public void Score_export_validation_rejects_duplicate_revision_ids()
    {
        PhotoQualityBenchmarkExport benchmark = Manifest(
            Photo("a", "calibration", "normal"));

        PhotoQualityCandidateScoreExport scores = ScoreExport(
            benchmark,
            higherIsBetter: true,
            new Dictionary<string, double?>
            {
                ["a"] = 50d,
            });

        scores = scores with
        {
            Scores =
            [
                scores.Scores[0],
                scores.Scores[0],
            ],
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => PhotoQualityModelEvaluation.ValidateScoreExport(
                benchmark,
                scores));

        Assert.Contains(
            "duplicate",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wi0182_baseline_score_is_fixed_reason_count_not_archive_percentile()
    {
        TechnicalPhotoQualityEvidence evidence = new(
            TechnicalPhotoQualityProtocol.AlgorithmVersion,
            TechnicalPhotoQualityProtocol.CalibratedProfileVersion,
            "jpeg-1600-q78",
            1600,
            1200,
            1_920_000,
            20d,
            120d,
            0.01d,
            0.01d,
            10,
            200,
            190,
            1d,
            [
                "candidate-low-sharpness",
                "candidate-low-contrast",
            ]);

        Assert.Equal(80d, PhotoQualityTechnicalBaseline.Score(evidence));
    }

    private static PhotoQualityCandidateScoreExport ScoreExport(
        PhotoQualityBenchmarkExport benchmark,
        bool higherIsBetter,
        IReadOnlyDictionary<string, double?> values)
    {
        PhotoQualityCandidateScore[] scores = benchmark.Photos
            .Select(photo => new PhotoQualityCandidateScore(
                photo.RevisionId,
                values[photo.RevisionId],
                1d,
                values[photo.RevisionId].HasValue ? null : "test-failure",
                null))
            .ToArray();

        return new PhotoQualityCandidateScoreExport(
            1,
            benchmark.BenchmarkId,
            DateTimeOffset.UtcNow,
            new PhotoQualityCandidateDescriptor(
                "test-candidate",
                "Test candidate",
                "test-v1",
                "test",
                "test",
                "test",
                "test preprocessing",
                "test score",
                123,
                "CPU"),
            "review-proxy",
            higherIsBetter,
            scores.Length,
            new PhotoQualityCandidateRuntimeSummary(10d, 1d, 2d),
            scores);
    }

    private static PhotoQualityBenchmarkExport Manifest(
        params PhotoQualityBenchmarkPhoto[] photos)
    {
        PhotoQualityBenchmarkCoverage representativeCoverage =
            PhotoQualityBenchmarkFile.Coverage(
                photos.Where(photo =>
                    !string.Equals(
                        photo.SampleKind,
                        "challenge",
                        StringComparison.OrdinalIgnoreCase)));
        PhotoQualityBenchmarkCoverage populationCoverage =
            PhotoQualityBenchmarkFile.Coverage(photos);

        return new PhotoQualityBenchmarkExport(
            2,
            "bench-quality-eval",
            DateTimeOffset.UtcNow,
            new ReviewProxyProfile("jpeg-1600-q78", 1600, 78)
                .ToCanonicalText(),
            "test-selection",
            "test-partition",
            photos.Length,
            photos.Count(photo => photo.SampleKind != "challenge"),
            photos.Count(photo => photo.SampleKind != "challenge"),
            photos.Count(photo => photo.SampleKind == "challenge"),
            photos.Count(photo => photo.SampleKind == "challenge"),
            photos.Count(photo => photo.Partition == "calibration"),
            photos.Count(photo => photo.Partition == "held-out"),
            new PhotoQualityBenchmarkLeakageSummary(
                "test",
                4,
                0,
                0),
            populationCoverage,
            representativeCoverage,
            [],
            photos.Any(photo => photo.SampleKind == "challenge")
                ? PhotoQualityBenchmarkFile.Coverage(
                    photos.Where(photo => photo.SampleKind == "challenge"))
                : null,
            "test",
            photos);
    }

    private static PhotoQualityBenchmarkPhoto Photo(
        string revisionId,
        string partition,
        string? qualityLevel,
        string sampleKind = "representative") =>
        new(
            revisionId,
            $"private/{revisionId}.jpg",
            new string('a', 64),
            $"{revisionId}.jpg",
            new string('b', 64),
            "image/jpeg",
            4000,
            3000,
            new DateTime(2020, 1, 1),
            "Example",
            "Camera",
            1,
            new PhotoQualityBenchmarkStratum(
                "2020s",
                "camera:example/camera",
                "jpeg",
                "12-24mp",
                "landscape",
                "one"),
            partition,
            sampleKind,
            sampleKind == "challenge"
                ? ["test-challenge"]
                : [],
            new PhotoQualityBenchmarkHumanReview(
                qualityLevel,
                false,
                null));
}
