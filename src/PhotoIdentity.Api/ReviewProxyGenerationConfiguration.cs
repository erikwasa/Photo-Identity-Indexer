using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Api;

public sealed record ReviewProxyGenerationConfiguration(
    string? RootPath,
    string? ProfileId,
    int? MaximumLongEdge,
    int? JpegQuality)
{
    public bool TryResolve(out string? derivativeRoot, out ReviewProxyProfile? profile, out string? message)
    {
        derivativeRoot = null;
        profile = null;
        if (string.IsNullOrWhiteSpace(RootPath) ||
            string.IsNullOrWhiteSpace(ProfileId) ||
            MaximumLongEdge is null ||
            JpegQuality is null)
        {
            message = "Automatic review-proxy generation requires ReviewProxyRoot, ReviewProxyProfileId, ReviewProxyMaximumLongEdge and ReviewProxyJpegQuality.";
            return false;
        }

        try
        {
            derivativeRoot = Path.GetFullPath(RootPath);
            profile = new ReviewProxyProfile(ProfileId.Trim(), MaximumLongEdge.Value, JpegQuality.Value);
            message = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            derivativeRoot = null;
            profile = null;
            message = $"Review proxy generation configuration is invalid: {exception.Message}";
            return false;
        }
    }
}
