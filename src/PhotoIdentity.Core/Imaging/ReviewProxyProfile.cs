using System.Globalization;

namespace PhotoIdentity.Core.Imaging;

/// <summary>
/// Exact, versioned settings for a permanent review proxy derivative.
/// The profile identifier is stable catalogue identity; changing any setting requires a new identifier.
/// </summary>
public sealed record ReviewProxyProfile
{
    public const string ProtocolVersion = "review-proxy-v1";
    public const string Encoder = "opencv-jpeg";
    public const string Format = "jpeg";
    public const string ContentType = "image/jpeg";
    public const string ResizePolicy = "fit-long-edge-no-upscale-area-v1";

    public ReviewProxyProfile(string id, int maximumLongEdge, int jpegQuality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string normalizedId = id.Trim();
        if (normalizedId.Length > 64 ||
            !char.IsAsciiLetterOrDigit(normalizedId[0]) ||
            normalizedId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Proxy profile id must be 1-64 ASCII letters, digits, '.', '_' or '-' and start with a letter or digit.",
                nameof(id));
        }

        if (maximumLongEdge is <= 0 or > 32768)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLongEdge),
                "Maximum long edge must be between 1 and 32768 pixels.");
        }

        if (jpegQuality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jpegQuality),
                "JPEG quality must be between 1 and 100.");
        }

        Id = normalizedId;
        MaximumLongEdge = maximumLongEdge;
        JpegQuality = jpegQuality;
    }

    public string Id { get; }
    public int MaximumLongEdge { get; }
    public int JpegQuality { get; }

    public string ToCanonicalText() => string.Join(
        '\n',
        $"profile-id={Id}",
        $"protocol={ProtocolVersion}",
        $"encoder={Encoder}",
        $"format={Format}",
        $"jpeg-quality={JpegQuality.ToString(CultureInfo.InvariantCulture)}",
        $"maximum-long-edge={MaximumLongEdge.ToString(CultureInfo.InvariantCulture)}",
        $"resize-policy={ResizePolicy}");

    public string ToDisplayText() => ToCanonicalText().Replace('\n', ';');
}
