using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Tests;

public sealed class ModelAndImageTests
{
    [Fact]
    public void ImageFrameCopiesInputAndValidatesLength()
    {
        byte[] pixels = new byte[12];
        ImageFrame image = new(new ImageSize(2, 2), PixelFormat.Rgb24, 6, pixels);

        pixels[0] = 255;

        Assert.Equal((byte)0, image.Data[0]);
        Assert.Throws<ArgumentException>(() =>
            new ImageFrame(new ImageSize(2, 2), PixelFormat.Rgb24, 6, new byte[11]));
    }

    [Fact]
    public void EmbeddingModelRequiresEmbeddingMetadata()
    {
        Sha256Digest hash = new(new string('a', 64));

        Assert.Throws<ArgumentException>(() => new ModelDescriptor(
            new ModelId("sface"),
            ModelRole.FaceEmbedding,
            ModelFormat.Onnx,
            hash,
            new ImageSize(112, 112),
            "onnxruntime",
            "Apache-2.0",
            "1"));
    }

    [Fact]
    public void Sha256DigestIsNormalised()
    {
        Sha256Digest digest = new(new string('A', 64));

        Assert.Equal(new string('a', 64), digest.Value);
        Assert.Throws<ArgumentException>(() => new Sha256Digest("abc"));
    }

    [Fact]
    public void DetectionConfidenceIsValidated()
    {
        NormalizedFaceLandmarks landmarks = new(
            new NormalizedPoint(0.2, 0.2),
            new NormalizedPoint(0.4, 0.2),
            new NormalizedPoint(0.3, 0.3),
            new NormalizedPoint(0.25, 0.4),
            new NormalizedPoint(0.35, 0.4));

        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectedFaceCandidate(
            new NormalizedBoundingBox(0.1, 0.1, 0.4, 0.4),
            landmarks,
            1.1));
    }
}
