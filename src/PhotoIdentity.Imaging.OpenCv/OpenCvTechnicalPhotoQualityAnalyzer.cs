using OpenCvSharp;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Imaging.OpenCv;

/// <summary>
/// Computes experimental technical evidence from an already-rendered durable review proxy.
/// Candidate reason thresholds are deliberately conservative and are calibrated only for the
/// maintained 1600px/q78 proxy settings. Other profiles retain raw signals without defect flags.
/// </summary>
public sealed class OpenCvTechnicalPhotoQualityAnalyzer
{
    private const double CandidateLowSharpnessVariance = 45d;
    private const double CandidateUnderexposedMean = 35d;
    private const double CandidateOverexposedMean = 220d;
    private const double CandidateShadowClippingFraction = 0.20d;
    private const double CandidateSevereShadowClippingFraction = 0.40d;
    private const double CandidateHighlightClippingFraction = 0.08d;
    private const double CandidateSevereHighlightClippingFraction = 0.25d;
    private const int CandidateLowContrastSpan = 40;
    private const double CandidateLowResolutionFraction = 0.50d;

    public TechnicalPhotoQualityEvidence Analyze(
        ReadOnlySpan<byte> encodedProxy,
        ReviewProxyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        if (encodedProxy.IsEmpty)
        {
            throw new InvalidDataException("The review proxy image is empty.");
        }

        try
        {
            using Mat gray = Cv2.ImDecode(encodedProxy.ToArray(), ImreadModes.Grayscale);
            if (gray.Empty())
            {
                throw new InvalidDataException("The review proxy image could not be decoded.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            using Mat laplacian = new();
            Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
            Cv2.MeanStdDev(laplacian, out Scalar _, out Scalar standardDeviation);
            double sharpnessVariance = standardDeviation.Val0 * standardDeviation.Val0;

            if (!gray.GetArray<byte>(out byte[] pixels) || pixels.Length == 0)
            {
                throw new InvalidDataException("The review proxy luminance pixels could not be read.");
            }

            int[] histogram = new int[256];
            long luminanceTotal = 0;
            int shadowClipped = 0;
            int highlightClipped = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                if ((index & 0xFFFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                byte pixel = pixels[index];
                histogram[pixel]++;
                luminanceTotal += pixel;
                if (pixel <= 4)
                {
                    shadowClipped++;
                }
                if (pixel >= 251)
                {
                    highlightClipped++;
                }
            }

            int p05 = FindPercentile(histogram, pixels.Length, 0.05d);
            int p95 = FindPercentile(histogram, pixels.Length, 0.95d);
            double mean = luminanceTotal / (double)pixels.Length;
            double shadowFraction = shadowClipped / (double)pixels.Length;
            double highlightFraction = highlightClipped / (double)pixels.Length;
            int longEdge = Math.Max(gray.Cols, gray.Rows);
            double longEdgeFraction = Math.Min(1d, longEdge / (double)profile.MaximumLongEdge);
            bool calibrated = profile.MaximumLongEdge == TechnicalPhotoQualityProtocol.CalibratedMaximumLongEdge &&
                profile.JpegQuality == TechnicalPhotoQualityProtocol.CalibratedJpegQuality;

            List<string> reasons = calibrated
                ? BuildCandidateReasons(
                    sharpnessVariance,
                    mean,
                    shadowFraction,
                    highlightFraction,
                    p95 - p05,
                    longEdgeFraction)
                : ["profile-not-calibrated"];

            return new TechnicalPhotoQualityEvidence(
                TechnicalPhotoQualityProtocol.AlgorithmVersion,
                calibrated
                    ? TechnicalPhotoQualityProtocol.CalibratedProfileVersion
                    : TechnicalPhotoQualityProtocol.RawSignalsOnlyVersion,
                profile.Id,
                gray.Cols,
                gray.Rows,
                pixels.LongLength,
                sharpnessVariance,
                mean,
                shadowFraction,
                highlightFraction,
                p05,
                p95,
                p95 - p05,
                longEdgeFraction,
                reasons);
        }
        catch (OpenCVException exception)
        {
            throw new InvalidDataException(
                "OpenCV could not derive technical-quality evidence from the review proxy.",
                exception);
        }
    }

    private static List<string> BuildCandidateReasons(
        double sharpnessVariance,
        double meanLuminance,
        double shadowClippingFraction,
        double highlightClippingFraction,
        int contrastSpan,
        double longEdgeFraction)
    {
        List<string> reasons = [];
        if (sharpnessVariance < CandidateLowSharpnessVariance)
        {
            reasons.Add("candidate-low-sharpness");
        }
        if (meanLuminance <= CandidateUnderexposedMean ||
            shadowClippingFraction >= CandidateSevereShadowClippingFraction)
        {
            reasons.Add("candidate-severe-underexposure");
        }
        if (meanLuminance >= CandidateOverexposedMean ||
            highlightClippingFraction >= CandidateSevereHighlightClippingFraction)
        {
            reasons.Add("candidate-severe-overexposure");
        }
        if (shadowClippingFraction >= CandidateShadowClippingFraction)
        {
            reasons.Add("candidate-shadow-clipping");
        }
        if (highlightClippingFraction >= CandidateHighlightClippingFraction)
        {
            reasons.Add("candidate-highlight-clipping");
        }
        if (contrastSpan <= CandidateLowContrastSpan)
        {
            reasons.Add("candidate-low-contrast");
        }
        if (longEdgeFraction < CandidateLowResolutionFraction)
        {
            reasons.Add("candidate-low-effective-proxy-resolution");
        }

        return reasons;
    }

    private static int FindPercentile(int[] histogram, int total, double percentile)
    {
        int target = Math.Max(1, (int)Math.Ceiling(total * percentile));
        int cumulative = 0;
        for (int value = 0; value < histogram.Length; value++)
        {
            cumulative += histogram[value];
            if (cumulative >= target)
            {
                return value;
            }
        }

        return 255;
    }
}
