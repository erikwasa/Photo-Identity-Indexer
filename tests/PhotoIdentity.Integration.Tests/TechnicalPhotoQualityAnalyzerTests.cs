using OpenCvSharp;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class TechnicalPhotoQualityAnalyzerTests
{
    private static readonly ReviewProxyProfile CalibratedProfile = new("jpeg-1600-q78", 1600, 78);

    [Fact]
    public void Analyze_constant_midgray_reports_low_sharpness_and_low_contrast_without_an_overall_score()
    {
        OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();

        TechnicalPhotoQualityEvidence evidence = analyzer.Analyze(EncodeConstantJpeg(128), CalibratedProfile);

        Assert.Equal(TechnicalPhotoQualityProtocol.AlgorithmVersion, evidence.AlgorithmVersion);
        Assert.Equal(TechnicalPhotoQualityProtocol.CalibratedProfileVersion, evidence.CalibrationVersion);
        Assert.InRange(evidence.MeanLuminance, 126d, 130d);
        Assert.True(evidence.SharpnessLaplacianVariance < 1d);
        Assert.Contains("candidate-low-sharpness", evidence.Reasons);
        Assert.Contains("candidate-low-contrast", evidence.Reasons);
    }

    [Fact]
    public void Analyze_black_proxy_surfaces_underexposure_and_shadow_clipping_evidence()
    {
        OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();

        TechnicalPhotoQualityEvidence evidence = analyzer.Analyze(EncodeConstantJpeg(0), CalibratedProfile);

        Assert.Contains("candidate-severe-underexposure", evidence.Reasons);
        Assert.Contains("candidate-shadow-clipping", evidence.Reasons);
        Assert.InRange(evidence.ShadowClippingFraction, 0.99d, 1d);
        Assert.InRange(evidence.HighlightClippingFraction, 0d, 0.01d);
    }

    [Fact]
    public void Analyze_white_proxy_surfaces_overexposure_and_highlight_clipping_evidence()
    {
        OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();

        TechnicalPhotoQualityEvidence evidence = analyzer.Analyze(EncodeConstantJpeg(255), CalibratedProfile);

        Assert.Contains("candidate-severe-overexposure", evidence.Reasons);
        Assert.Contains("candidate-highlight-clipping", evidence.Reasons);
        Assert.InRange(evidence.HighlightClippingFraction, 0.99d, 1d);
        Assert.InRange(evidence.ShadowClippingFraction, 0d, 0.01d);
    }

    [Fact]
    public void Analyze_unvalidated_proxy_profile_retains_raw_signals_without_candidate_defect_thresholds()
    {
        OpenCvTechnicalPhotoQualityAnalyzer analyzer = new();
        ReviewProxyProfile unvalidated = new("jpeg-1600-q90", 1600, 90);

        TechnicalPhotoQualityEvidence evidence = analyzer.Analyze(EncodeConstantJpeg(0, quality: 90), unvalidated);

        Assert.Equal(TechnicalPhotoQualityProtocol.RawSignalsOnlyVersion, evidence.CalibrationVersion);
        Assert.Equal("profile-not-calibrated", Assert.Single(evidence.Reasons));
        Assert.InRange(evidence.ShadowClippingFraction, 0.99d, 1d);
    }

    private static byte[] EncodeConstantJpeg(byte value, int quality = 78)
    {
        using Mat image = new(512, 768, MatType.CV_8UC1);
        image.SetTo(new Scalar(value));
        Cv2.ImEncode(
            ".jpg",
            image,
            out byte[] encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, quality));
        return encoded;
    }
}
