namespace PhotoIdentity.Core.Geometry;

public readonly record struct NormalizedFaceLandmarks(
    NormalizedPoint LeftEye,
    NormalizedPoint RightEye,
    NormalizedPoint Nose,
    NormalizedPoint MouthLeft,
    NormalizedPoint MouthRight)
{
    public PixelFaceLandmarks ToPixels(ImageSize imageSize) => new(
        LeftEye.ToPixels(imageSize),
        RightEye.ToPixels(imageSize),
        Nose.ToPixels(imageSize),
        MouthLeft.ToPixels(imageSize),
        MouthRight.ToPixels(imageSize));
}

public readonly record struct PixelFaceLandmarks(
    PixelPoint LeftEye,
    PixelPoint RightEye,
    PixelPoint Nose,
    PixelPoint MouthLeft,
    PixelPoint MouthRight)
{
    public NormalizedFaceLandmarks ToNormalized(ImageSize imageSize)
    {
        ValidateWithinImage(LeftEye, imageSize);
        ValidateWithinImage(RightEye, imageSize);
        ValidateWithinImage(Nose, imageSize);
        ValidateWithinImage(MouthLeft, imageSize);
        ValidateWithinImage(MouthRight, imageSize);

        return new NormalizedFaceLandmarks(
            ToNormalizedPoint(LeftEye, imageSize),
            ToNormalizedPoint(RightEye, imageSize),
            ToNormalizedPoint(Nose, imageSize),
            ToNormalizedPoint(MouthLeft, imageSize),
            ToNormalizedPoint(MouthRight, imageSize));
    }

    private static NormalizedPoint ToNormalizedPoint(PixelPoint point, ImageSize imageSize) =>
        new(point.X / imageSize.Width, point.Y / imageSize.Height);

    private static void ValidateWithinImage(PixelPoint point, ImageSize imageSize)
    {
        if (point.X > imageSize.Width || point.Y > imageSize.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Landmark lies outside the image.");
        }
    }
}
