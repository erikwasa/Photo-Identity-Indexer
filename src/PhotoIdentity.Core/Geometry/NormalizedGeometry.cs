namespace PhotoIdentity.Core.Geometry;

public readonly record struct NormalizedPoint
{
    public NormalizedPoint(double x, double y)
    {
        ValidateUnitInterval(x, nameof(x));
        ValidateUnitInterval(y, nameof(y));
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }

    public PixelPoint ToPixels(ImageSize imageSize) => new(X * imageSize.Width, Y * imageSize.Height);

    private static void ValidateUnitInterval(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Normalised coordinates must be between 0 and 1.");
        }
    }
}

public readonly record struct NormalizedBoundingBox
{
    public NormalizedBoundingBox(double x, double y, double width, double height)
    {
        GeometryGuard.FiniteNonNegative(x, nameof(x));
        GeometryGuard.FiniteNonNegative(y, nameof(y));
        GeometryGuard.FinitePositive(width, nameof(width));
        GeometryGuard.FinitePositive(height, nameof(height));

        if (x > 1 || y > 1 || x + width > 1 || y + height > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Normalised bounding box must fit within the unit square.");
        }

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

    public double IntersectionOverUnion(NormalizedBoundingBox other)
    {
        double intersectionWidth = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X));
        double intersectionHeight = Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
        double intersection = intersectionWidth * intersectionHeight;
        double union = Area + other.Area - intersection;
        return union == 0 ? 0 : intersection / union;
    }

    public PixelBoundingBox ToPixels(ImageSize imageSize) => new(
        X * imageSize.Width,
        Y * imageSize.Height,
        Width * imageSize.Width,
        Height * imageSize.Height);
}
