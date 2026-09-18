using OpenCvSharp;
using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Imaging.OpenCv;

/// <summary>
/// Computes a compact difference hash from an already-rendered local review proxy.
/// The algorithm is deliberately simple, deterministic and presentation-only.
/// </summary>
public sealed class OpenCvPerceptualHashCalculator
{
    public async Task<PhotoPerceptualHash64> ComputeAsync(
        string proxyPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyPath);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] bytes = await File.ReadAllBytesAsync(proxyPath, cancellationToken);
        return Compute(bytes, cancellationToken);
    }

    public PhotoPerceptualHash64 Compute(
        ReadOnlySpan<byte> encodedImage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (encodedImage.IsEmpty)
        {
            throw new InvalidDataException("The review proxy image is empty.");
        }

        try
        {
            using Mat source = Cv2.ImDecode(encodedImage.ToArray(), ImreadModes.Grayscale);
            if (source.Empty())
            {
                throw new InvalidDataException("The review proxy image could not be decoded.");
            }

            using Mat resized = new();
            Cv2.Resize(
                source,
                resized,
                new Size(9, 8),
                interpolation: InterpolationFlags.Area);

            ulong value = 0;
            int bit = 0;
            for (int row = 0; row < 8; row++)
            {
                for (int column = 0; column < 8; column++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte left = resized.At<byte>(row, column);
                    byte right = resized.At<byte>(row, column + 1);
                    if (left > right)
                    {
                        value |= 1UL << bit;
                    }

                    bit++;
                }
            }

            return new PhotoPerceptualHash64(value);
        }
        catch (OpenCVException exception)
        {
            throw new InvalidDataException(
                "OpenCV could not compute a perceptual hash for the review proxy.",
                exception);
        }
    }
}
