using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Api;

internal static class OpenCvImageDecoderSearchExtensions
{
    public static Task<ImageFrame> DecodeAsync(
        this OpenCvImageDecoder decoder,
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return decoder.DecodeAsync(source, new DecodeOptions(), cancellationToken);
    }
}
