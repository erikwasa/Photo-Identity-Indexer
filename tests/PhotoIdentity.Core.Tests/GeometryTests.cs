using PhotoIdentity.Core.Geometry;

namespace PhotoIdentity.Core.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void PixelBoundingBoxRejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBoundingBox(0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBoundingBox(-1, 0, 10, 10));
    }

    [Fact]
    public void NormalizedBoundingBoxMustFitUnitSquare()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedBoundingBox(0.8, 0.2, 0.3, 0.2));
    }

    [Fact]
    public void IntersectionOverUnionIsCalculated()
    {
        PixelBoundingBox first = new(0, 0, 10, 10);
        PixelBoundingBox second = new(5, 5, 10, 10);

        Assert.Equal(25d / 175d, first.IntersectionOverUnion(second), 12);
    }

    [Fact]
    public void BoundingBoxesRoundTripBetweenCoordinateSpaces()
    {
        ImageSize imageSize = new(200, 100);
        PixelBoundingBox pixels = new(20, 10, 80, 40);

        NormalizedBoundingBox normalized = pixels.ToNormalized(imageSize);
        PixelBoundingBox result = normalized.ToPixels(imageSize);

        Assert.Equal(pixels, result);
    }

    [Fact]
    public void LandmarksRoundTripBetweenCoordinateSpaces()
    {
        ImageSize imageSize = new(100, 100);
        PixelFaceLandmarks pixels = new(
            new PixelPoint(20, 30),
            new PixelPoint(70, 30),
            new PixelPoint(45, 50),
            new PixelPoint(30, 70),
            new PixelPoint(60, 70));

        PixelFaceLandmarks result = pixels.ToNormalized(imageSize).ToPixels(imageSize);

        Assert.Equal(pixels, result);
    }
}
