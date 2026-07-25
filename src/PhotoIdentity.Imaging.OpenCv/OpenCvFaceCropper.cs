using System.Buffers.Binary;
using System.Security.Cryptography;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed record FaceCropOptions
{
    public double PaddingRatio { get; init; } = 0.25;

    internal void Validate()
    {
        if (!double.IsFinite(PaddingRatio) || PaddingRatio < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PaddingRatio),
                "Padding ratio must be finite and non-negative.");
        }
    }
}

public sealed record PaddedFaceCrop(
    ImageFrame Image,
    PixelBoundingBox SourceBounds,
    Sha256Digest ContentHash);

public sealed class OpenCvFaceCropper
{
    private static ReadOnlySpan<byte> HashProtocol =>
        "photoidentity-face-crop-v1\0"u8;

    public PaddedFaceCrop CreatePaddedCrop(
        ImageFrame image,
        DetectedFaceCandidate detection,
        FaceCropOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(detection);
        cancellationToken.ThrowIfCancellationRequested();

        FaceCropOptions resolvedOptions = options ?? new FaceCropOptions();
        resolvedOptions.Validate();

        PixelBoundingBox face = detection.BoundingBox.ToPixels(image.Size);
        double horizontalPadding = face.Width * resolvedOptions.PaddingRatio;
        double verticalPadding = face.Height * resolvedOptions.PaddingRatio;

        int left = Math.Clamp(
            (int)Math.Floor(face.X - horizontalPadding),
            0,
            image.Size.Width - 1);
        int top = Math.Clamp(
            (int)Math.Floor(face.Y - verticalPadding),
            0,
            image.Size.Height - 1);
        int right = Math.Clamp(
            (int)Math.Ceiling(face.Right + horizontalPadding),
            left + 1,
            image.Size.Width);
        int bottom = Math.Clamp(
            (int)Math.Ceiling(face.Bottom + verticalPadding),
            top + 1,
            image.Size.Height);

        int width = right - left;
        int height = bottom - top;
        int bytesPerPixel = ImageFrame.BytesPerPixel(image.Format);
        int stride = checked(width * bytesPerPixel);
        byte[] cropData = new byte[checked(stride * height)];
        ReadOnlySpan<byte> source = image.Data;

        for (int row = 0; row < height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sourceOffset = checked(((top + row) * image.Stride) + (left * bytesPerPixel));
            source.Slice(sourceOffset, stride)
                .CopyTo(cropData.AsSpan(row * stride, stride));
        }

        ImageFrame crop = new(
            new ImageSize(width, height),
            image.Format,
            stride,
            cropData);

        return new PaddedFaceCrop(
            crop,
            new PixelBoundingBox(left, top, width, height),
            ComputeContentHash(crop));
    }

    private static Sha256Digest ComputeContentHash(ImageFrame image)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(HashProtocol);

        Span<byte> metadata = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(metadata[..4], image.Size.Width);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.Slice(4, 4), image.Size.Height);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.Slice(8, 4), (int)image.Format);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.Slice(12, 4), image.Stride);
        hash.AppendData(metadata);
        hash.AppendData(image.Data);

        return new Sha256Digest(Convert.ToHexString(hash.GetHashAndReset()));
    }
}
