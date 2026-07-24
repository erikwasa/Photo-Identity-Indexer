namespace PhotoIdentity.Core.Geometry;

public readonly record struct ImageSize
{
    public ImageSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public long PixelCount => checked((long)Width * Height);
}

public readonly record struct PixelPoint
{
    public PixelPoint(double x, double y)
    {
        GeometryGuard.FiniteNonNegative(x, nameof(x));
        GeometryGuard.FiniteNonNegative(y, nameof(y));
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}

public readonly record struct PixelBoundingBox
{
    public PixelBoundingBox(double x, double y, double width, double height)
    {
        GeometryGuard.FiniteNonNegative(x, nameof(x));
        GeometryGuard.FiniteNonNegative(y, nameof(y));
        GeometryGuard.FinitePositive(width, nameof(width));
        GeometryGuard.FinitePositive(height, nameof(height));
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;

    public double IntersectionOverUnion(PixelBoundingBox other)
    {
        double intersectionWidth = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X));
        double intersectionHeight = Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
        double intersection = intersectionWidth * intersectionHeight;
        double union = Area + other.Area - intersection;
        return union == 0 ? 0 : intersection / union;
    }

    public NormalizedBoundingBox ToNormalized(ImageSize imageSize)
    {
        if (Right > imageSize.Width || Bottom > imageSize.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(imageSize), "Bounding box extends beyond the image.");
        }

        return new NormalizedBoundingBox(
            X / imageSize.Width,
            Y / imageSize.Height,
            Width / imageSize.Width,
            Height / imageSize.Height);
    }
}

internal static class GeometryGuard
{
    public static void FiniteNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and non-negative.");
        }
    }

    public static void FinitePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and positive.");
        }
    }
}
