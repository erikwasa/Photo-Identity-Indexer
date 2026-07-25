using System.Runtime.InteropServices;
using OpenCvSharp;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class FaceCropAndAlignmentTests
{
    private readonly OpenCvFaceCropper _cropper = new();
    private readonly OpenCvFaceAligner _aligner = new();

    [Fact]
    public void Padded_crop_clamps_edge_face_and_packs_rows()
    {
        ImageFrame image = CreateGradientFrame(8, 6, stridePadding: 5);
        DetectedFaceCandidate detection = CreateDetection(
            new NormalizedBoundingBox(0, 0, 0.5, 0.5),
            CreateLandmarks(
                leftEye: new NormalizedPoint(0.35, 0.2),
                rightEye: new NormalizedPoint(0.15, 0.2),
                nose: new NormalizedPoint(0.25, 0.3),
                mouthLeft: new NormalizedPoint(0.33, 0.42),
                mouthRight: new NormalizedPoint(0.17, 0.42)));

        PaddedFaceCrop crop = _cropper.CreatePaddedCrop(
            image,
            detection,
            new FaceCropOptions { PaddingRatio = 0.5 });

        Assert.Equal(new PixelBoundingBox(0, 0, 6, 5), crop.SourceBounds);
        Assert.Equal(new ImageSize(6, 5), crop.Image.Size);
        Assert.Equal(PixelFormat.Bgr24, crop.Image.Format);
        Assert.Equal(18, crop.Image.Stride);
        Assert.Equal(GetPixel(image, 0, 0), GetPixel(crop.Image, 0, 0));
        Assert.Equal(GetPixel(image, 5, 4), GetPixel(crop.Image, 5, 4));
    }

    [Fact]
    public void Crop_hash_is_stable_across_runs_and_source_strides()
    {
        ImageFrame packed = CreateGradientFrame(12, 10);
        ImageFrame padded = CreateGradientFrame(12, 10, stridePadding: 11);
        DetectedFaceCandidate detection = CreateDetection(
            new NormalizedBoundingBox(0.25, 0.2, 0.5, 0.6),
            CanonicalLandmarks());

        PaddedFaceCrop first = _cropper.CreatePaddedCrop(packed, detection);
        PaddedFaceCrop second = _cropper.CreatePaddedCrop(packed, detection);
        PaddedFaceCrop equivalent = _cropper.CreatePaddedCrop(padded, detection);

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.ContentHash, equivalent.ContentHash);
        Assert.Equal(first.Image.Data.ToArray(), equivalent.Image.Data.ToArray());
    }

    [Fact]
    public async Task Alignment_returns_fixed_sface_output_and_protocol()
    {
        ImageFrame canonical = CreateGradientFrame(112, 112);
        DetectedFaceCandidate detection = CreateDetection(
            new NormalizedBoundingBox(0.15, 0.2, 0.7, 0.75),
            CanonicalLandmarks());

        AlignedFace result = await _aligner.AlignAsync(
            canonical,
            detection,
            OpenCvFaceAligner.SFaceFivePointV1,
            CancellationToken.None);

        Assert.Equal(new ImageSize(112, 112), result.Image.Size);
        Assert.Equal(PixelFormat.Bgr24, result.Image.Format);
        Assert.Equal(OpenCvFaceAligner.SFaceFivePointV1, result.Protocol);
        Assert.InRange(AverageAbsoluteDifference(canonical, result.Image), 0, 0.5);
    }

    [Fact]
    public async Task Alignment_restores_rotated_visual_fixture()
    {
        ImageFrame canonical = CreateVisualFixture();
        const int canvasSize = 180;
        double angle = 18 * Math.PI / 180;
        double scale = 0.95;
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        double m00 = scale * cosine;
        double m01 = -scale * sine;
        double m10 = scale * sine;
        double m11 = scale * cosine;
        double sourceCentre = 56;
        double canvasCentre = canvasSize / 2d;
        double m02 = canvasCentre - (m00 * sourceCentre) - (m01 * sourceCentre);
        double m12 = canvasCentre - (m10 * sourceCentre) - (m11 * sourceCentre);
        double[] forward = [m00, m01, m02, m10, m11, m12];

        using Mat canonicalMat = ToMat(canonical);
        using Mat transform = CreateTransformMat(forward);
        using Mat rotatedMat = new();
        Cv2.WarpAffine(
            canonicalMat,
            rotatedMat,
            transform,
            new Size(canvasSize, canvasSize),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            new Scalar(0, 0, 0));

        ImageFrame rotated = ToFrame(rotatedMat, PixelFormat.Bgr24);
        PixelFaceLandmarks canonicalPixels = CanonicalLandmarks()
            .ToPixels(canonical.Size);
        PixelFaceLandmarks rotatedPixels = new(
            Transform(canonicalPixels.LeftEye, forward),
            Transform(canonicalPixels.RightEye, forward),
            Transform(canonicalPixels.Nose, forward),
            Transform(canonicalPixels.MouthLeft, forward),
            Transform(canonicalPixels.MouthRight, forward));
        DetectedFaceCandidate detection = CreateDetection(
            new NormalizedBoundingBox(0.15, 0.12, 0.7, 0.76),
            rotatedPixels.ToNormalized(rotated.Size));

        AlignedFace result = await _aligner.AlignAsync(
            rotated,
            detection,
            OpenCvFaceAligner.SFaceFivePointV1,
            CancellationToken.None);

        Assert.Equal(new ImageSize(112, 112), result.Image.Size);
        Assert.InRange(AverageAbsoluteDifference(canonical, result.Image), 0, 12);
    }

    [Fact]
    public async Task Alignment_rejects_unknown_protocol()
    {
        ImageFrame image = CreateGradientFrame(112, 112);
        DetectedFaceCandidate detection = CreateDetection(
            new NormalizedBoundingBox(0.15, 0.2, 0.7, 0.75),
            CanonicalLandmarks());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _aligner.AlignAsync(
                image,
                detection,
                new AlignmentProtocolId("unknown-v1"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Alignment_rejects_degenerate_landmarks()
    {
        ImageFrame image = CreateGradientFrame(112, 112);
        NormalizedPoint same = new(0.5, 0.5);
        DetectedFaceCandidate detection = CreateDetection(
            new NormalizedBoundingBox(0.25, 0.25, 0.5, 0.5),
            CreateLandmarks(same, same, same, same, same));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _aligner.AlignAsync(
                image,
                detection,
                OpenCvFaceAligner.SFaceFivePointV1,
                CancellationToken.None));
    }

    private static DetectedFaceCandidate CreateDetection(
        NormalizedBoundingBox box,
        NormalizedFaceLandmarks landmarks) =>
        new(box, landmarks, confidence: 0.99);

    private static NormalizedFaceLandmarks CreateLandmarks(
        NormalizedPoint leftEye,
        NormalizedPoint rightEye,
        NormalizedPoint nose,
        NormalizedPoint mouthLeft,
        NormalizedPoint mouthRight) =>
        new(leftEye, rightEye, nose, mouthLeft, mouthRight);

    private static NormalizedFaceLandmarks CanonicalLandmarks() =>
        new(
            LeftEye: new NormalizedPoint(73.5318 / 112, 51.5014 / 112),
            RightEye: new NormalizedPoint(38.2946 / 112, 51.6963 / 112),
            Nose: new NormalizedPoint(56.0252 / 112, 71.7366 / 112),
            MouthLeft: new NormalizedPoint(70.7299 / 112, 92.2041 / 112),
            MouthRight: new NormalizedPoint(41.5493 / 112, 92.3655 / 112));

    private static ImageFrame CreateGradientFrame(
        int width,
        int height,
        int stridePadding = 0)
    {
        int stride = checked((width * 3) + stridePadding);
        byte[] data = new byte[checked(stride * height)];
        Array.Fill(data, (byte)0xee);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 3);
                data[offset] = (byte)((x * 7 + y * 3) % 256);
                data[offset + 1] = (byte)((x * 5 + y * 11) % 256);
                data[offset + 2] = (byte)((x * 13 + y * 2) % 256);
            }
        }

        return new ImageFrame(
            new ImageSize(width, height),
            PixelFormat.Bgr24,
            stride,
            data);
    }

    private static ImageFrame CreateVisualFixture()
    {
        ImageFrame gradient = CreateGradientFrame(112, 112);
        byte[] data = gradient.ToArray();
        PixelFaceLandmarks landmarks = CanonicalLandmarks()
            .ToPixels(gradient.Size);

        DrawMarker(data, gradient.Stride, landmarks.RightEye, [255, 0, 0]);
        DrawMarker(data, gradient.Stride, landmarks.LeftEye, [0, 255, 0]);
        DrawMarker(data, gradient.Stride, landmarks.Nose, [0, 0, 255]);
        DrawMarker(data, gradient.Stride, landmarks.MouthRight, [255, 255, 0]);
        DrawMarker(data, gradient.Stride, landmarks.MouthLeft, [255, 0, 255]);

        return new ImageFrame(
            gradient.Size,
            gradient.Format,
            gradient.Stride,
            data);
    }

    private static void DrawMarker(
        byte[] data,
        int stride,
        PixelPoint point,
        byte[] colour)
    {
        int centreX = (int)Math.Round(point.X);
        int centreY = (int)Math.Round(point.Y);

        for (int y = centreY - 3; y <= centreY + 3; y++)
        {
            for (int x = centreX - 3; x <= centreX + 3; x++)
            {
                int offset = (y * stride) + (x * 3);
                colour.CopyTo(data, offset);
            }
        }
    }

    private static byte[] GetPixel(ImageFrame image, int x, int y)
    {
        int offset = (y * image.Stride) + (x * 3);
        return image.Data.Slice(offset, 3).ToArray();
    }

    private static PixelPoint Transform(PixelPoint point, IReadOnlyList<double> matrix) =>
        new(
            (matrix[0] * point.X) + (matrix[1] * point.Y) + matrix[2],
            (matrix[3] * point.X) + (matrix[4] * point.Y) + matrix[5]);

    private static Mat CreateTransformMat(double[] values)
    {
        Mat transform = new(2, 3, MatType.CV_64FC1);
        Marshal.Copy(values, 0, transform.Data, values.Length);
        return transform;
    }

    private static Mat ToMat(ImageFrame image)
    {
        int packedStride = checked(image.Size.Width * 3);
        byte[] packed = new byte[checked(packedStride * image.Size.Height)];

        for (int row = 0; row < image.Size.Height; row++)
        {
            image.Data.Slice(row * image.Stride, packedStride)
                .CopyTo(packed.AsSpan(row * packedStride, packedStride));
        }

        Mat matrix = new(image.Size.Height, image.Size.Width, MatType.CV_8UC3);
        Marshal.Copy(packed, 0, matrix.Data, packed.Length);
        return matrix;
    }

    private static ImageFrame ToFrame(Mat image, PixelFormat format)
    {
        int stride = checked(image.Cols * 3);
        byte[] data = new byte[checked(stride * image.Rows)];
        Marshal.Copy(image.Data, data, 0, data.Length);
        return new ImageFrame(new ImageSize(image.Cols, image.Rows), format, stride, data);
    }

    private static double AverageAbsoluteDifference(ImageFrame expected, ImageFrame actual)
    {
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.Format, actual.Format);

        ReadOnlySpan<byte> expectedData = expected.Data;
        ReadOnlySpan<byte> actualData = actual.Data;
        double total = 0;
        int samples = 0;
        int packedStride = checked(expected.Size.Width * 3);

        for (int row = 0; row < expected.Size.Height; row++)
        {
            ReadOnlySpan<byte> expectedRow = expectedData.Slice(row * expected.Stride, packedStride);
            ReadOnlySpan<byte> actualRow = actualData.Slice(row * actual.Stride, packedStride);
            for (int index = 0; index < packedStride; index++)
            {
                total += Math.Abs(expectedRow[index] - actualRow[index]);
                samples++;
            }
        }

        return total / samples;
    }
}
