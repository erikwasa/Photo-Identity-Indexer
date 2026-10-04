namespace PhotoIdentity.Core.Imaging;

/// <summary>
/// Versioned, inspectable technical evidence derived from a durable review proxy.
/// It is evaluation evidence, not an objective or aesthetic photo-quality score.
/// </summary>
public sealed record TechnicalPhotoQualityEvidence(
    string AlgorithmVersion,
    string CalibrationVersion,
    string ProxyProfileId,
    int Width,
    int Height,
    long PixelCount,
    double SharpnessLaplacianVariance,
    double MeanLuminance,
    double ShadowClippingFraction,
    double HighlightClippingFraction,
    int LuminanceP05,
    int LuminanceP95,
    int LuminanceContrastSpan,
    double ProxyLongEdgeFraction,
    IReadOnlyList<string> Reasons);

public static class TechnicalPhotoQualityProtocol
{
    public const string AlgorithmVersion = "proxy-technical-quality-v1";
    public const string CalibratedProfileVersion = "jpeg-1600-q78-candidate-thresholds-v1";
    public const string RawSignalsOnlyVersion = "raw-signals-only-v1";
    public const int CalibratedMaximumLongEdge = 1600;
    public const int CalibratedJpegQuality = 78;
}
