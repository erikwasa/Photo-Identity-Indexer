using System.Runtime.InteropServices;
using OpenCvSharp;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed class OpenCvFaceAligner : IFaceAligner
{
    private const int OutputWidth = 112;
    private const int OutputHeight = 112;

    private static readonly PixelPoint[] DestinationPoints =
    [
        new(38.2946, 51.6963),
        new(73.5318, 51.5014),
        new(56.0252, 71.7366),
        new(41.5493, 92.3655),
        new(70.7299, 92.2041),
    ];

    public static AlignmentProtocolId SFaceFivePointV1 { get; } =
        new("sface-five-point-v1");

    public static ImageSize AlignedSize { get; } =
        new(OutputWidth, OutputHeight);

    public Task<AlignedFace> AlignAsync(
        ImageFrame image,
        DetectedFaceCandidate detection,
        AlignmentProtocolId protocol,
        CancellationToken cancellationToken)
    {
        AlignedFace result = Align(image, detection, protocol, cancellationToken);
        return Task.FromResult(result);
    }

    public AlignedFace Align(
        ImageFrame image,
        DetectedFaceCandidate detection,
        AlignmentProtocolId protocol,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(detection);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(
                protocol.Value,
                SFaceFivePointV1.Value,
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Alignment protocol '{protocol}' is not supported. " +
                $"Expected '{SFaceFivePointV1}'.");
        }

        PixelFaceLandmarks landmarks = detection.Landmarks.ToPixels(image.Size);

        // OpenCV SFace's reference template is ordered as anatomical right eye,
        // anatomical left eye, nose, anatomical right mouth and anatomical left mouth.
        PixelPoint[] sourcePoints =
        [
            landmarks.RightEye,
            landmarks.LeftEye,
            landmarks.Nose,
            landmarks.MouthRight,
            landmarks.MouthLeft,
        ];

        double[] transformValues = CreateSimilarityTransform(
            sourcePoints,
            DestinationPoints);

        using Mat source = CreateMat(image, cancellationToken);
        using Mat transform = new(2, 3, MatType.CV_64FC1);
        Marshal.Copy(transformValues, 0, transform.Data, transformValues.Length);
        using Mat aligned = new();

        Cv2.WarpAffine(
            source,
            aligned,
            transform,
            new Size(OutputWidth, OutputHeight),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            new Scalar(0, 0, 0, 0));

        cancellationToken.ThrowIfCancellationRequested();
        return new AlignedFace(
            ToImageFrame(aligned, image.Format),
            SFaceFivePointV1);
    }

    private static double[] CreateSimilarityTransform(
        IReadOnlyList<PixelPoint> source,
        IReadOnlyList<PixelPoint> destination)
    {
        if (source.Count != destination.Count || source.Count == 0)
        {
            throw new ArgumentException(
                "Source and destination landmarks must contain the same non-zero number of points.");
        }

        double sourceMeanX = source.Average(point => point.X);
        double sourceMeanY = source.Average(point => point.Y);
        double destinationMeanX = destination.Average(point => point.X);
        double destinationMeanY = destination.Average(point => point.Y);

        double dot = 0;
        double cross = 0;
        double sourceVariance = 0;

        for (int index = 0; index < source.Count; index++)
        {
            double sourceX = source[index].X - sourceMeanX;
            double sourceY = source[index].Y - sourceMeanY;
            double destinationX = destination[index].X - destinationMeanX;
            double destinationY = destination[index].Y - destinationMeanY;

            dot += (sourceX * destinationX) + (sourceY * destinationY);
            cross += (sourceX * destinationY) - (sourceY * destinationX);
            sourceVariance += (sourceX * sourceX) + (sourceY * sourceY);
        }

        double correlationMagnitude = Math.Sqrt((dot * dot) + (cross * cross));
        if (sourceVariance <= 1e-12 || correlationMagnitude <= 1e-12)
        {
            throw new ArgumentException(
                "Face landmarks are degenerate and cannot define a similarity transform.",
                nameof(source));
        }

        double cosine = dot / correlationMagnitude;
        double sine = cross / correlationMagnitude;
        double scale = correlationMagnitude / sourceVariance;

        double m00 = scale * cosine;
        double m01 = -scale * sine;
        double m10 = scale * sine;
        double m11 = scale * cosine;
        double translationX = destinationMeanX -
            ((m00 * sourceMeanX) + (m01 * sourceMeanY));
        double translationY = destinationMeanY -
            ((m10 * sourceMeanX) + (m11 * sourceMeanY));

        return
        [
            m00,
            m01,
            translationX,
            m10,
            m11,
            translationY,
        ];
    }

    private static Mat CreateMat(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        int bytesPerPixel = ImageFrame.BytesPerPixel(image.Format);
        int packedStride = checked(image.Size.Width * bytesPerPixel);
        byte[] packed = new byte[checked(packedStride * image.Size.Height)];
        ReadOnlySpan<byte> source = image.Data;

        for (int row = 0; row < image.Size.Height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.Slice(row * image.Stride, packedStride)
                .CopyTo(packed.AsSpan(row * packedStride, packedStride));
        }

        Mat matrix = new(
            image.Size.Height,
            image.Size.Width,
            image.Format switch
            {
                PixelFormat.Gray8 => MatType.CV_8UC1,
                PixelFormat.Rgb24 or PixelFormat.Bgr24 => MatType.CV_8UC3,
                PixelFormat.Rgba32 or PixelFormat.Bgra32 => MatType.CV_8UC4,
                _ => throw new ArgumentOutOfRangeException(nameof(image)),
            });
        Marshal.Copy(packed, 0, matrix.Data, packed.Length);
        return matrix;
    }

    private static ImageFrame ToImageFrame(Mat image, PixelFormat pixelFormat)
    {
        int bytesPerPixel = ImageFrame.BytesPerPixel(pixelFormat);
        int stride = checked(image.Cols * bytesPerPixel);
        byte[] data = new byte[checked(stride * image.Rows)];
        Marshal.Copy(image.Data, data, 0, data.Length);

        return new ImageFrame(
            new ImageSize(image.Cols, image.Rows),
            pixelFormat,
            stride,
            data);
    }
}
