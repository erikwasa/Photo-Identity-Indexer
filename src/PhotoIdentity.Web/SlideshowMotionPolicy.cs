using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public sealed record SlideshowMotionPlan(
    bool Enabled,
    double Scale,
    double OriginXPercent,
    double OriginYPercent)
{
    public static SlideshowMotionPlan Static { get; } = new(false, 1d, 50d, 50d);
}

public static class SlideshowMotionPolicy
{
    private const double MotionScale = 1.03d;
    private const double FaceSafetyMargin = 0.015d;
    private const double MaximumProtectedSpan = 0.68d;
    private const double MaximumSingleFaceWidth = 0.48d;
    private const double MaximumSingleFaceHeight = 0.60d;
    private const int MaximumMovingFaceCount = 2;

    public static SlideshowMotionPlan Create(
        string? revisionId,
        IReadOnlyList<SlideshowFaceBoxResponse>? faces,
        bool geometryReliable)
    {
        if (!geometryReliable)
        {
            return SlideshowMotionPlan.Static;
        }

        IReadOnlyList<SlideshowFaceBoxResponse> protectedFaces = faces ?? [];
        if (protectedFaces.Count > MaximumMovingFaceCount ||
            protectedFaces.Any(face => !IsValid(face)))
        {
            return SlideshowMotionPlan.Static;
        }

        if (protectedFaces.Count == 0)
        {
            (double x, double y) = DeterministicNoFaceOrigin(revisionId);
            return new SlideshowMotionPlan(true, MotionScale, x * 100d, y * 100d);
        }

        double left = protectedFaces.Min(face => face.X);
        double top = protectedFaces.Min(face => face.Y);
        double right = protectedFaces.Max(face => face.X + face.Width);
        double bottom = protectedFaces.Max(face => face.Y + face.Height);

        if (right - left > MaximumProtectedSpan ||
            bottom - top > MaximumProtectedSpan ||
            protectedFaces.Any(face =>
                face.Width > MaximumSingleFaceWidth ||
                face.Height > MaximumSingleFaceHeight))
        {
            return SlideshowMotionPlan.Static;
        }

        double originX = Math.Clamp((left + right) / 2d, 0.25d, 0.75d);
        double originY = Math.Clamp((top + bottom) / 2d, 0.25d, 0.75d);

        foreach (SlideshowFaceBoxResponse face in protectedFaces)
        {
            double transformedLeft = Transform(face.X, originX);
            double transformedTop = Transform(face.Y, originY);
            double transformedRight = Transform(face.X + face.Width, originX);
            double transformedBottom = Transform(face.Y + face.Height, originY);

            if (transformedLeft < FaceSafetyMargin ||
                transformedTop < FaceSafetyMargin ||
                transformedRight > 1d - FaceSafetyMargin ||
                transformedBottom > 1d - FaceSafetyMargin)
            {
                return SlideshowMotionPlan.Static;
            }
        }

        return new SlideshowMotionPlan(
            true,
            MotionScale,
            originX * 100d,
            originY * 100d);
    }

    private static double Transform(double coordinate, double origin) =>
        origin + MotionScale * (coordinate - origin);

    private static bool IsValid(SlideshowFaceBoxResponse face)
    {
        if (!double.IsFinite(face.X) ||
            !double.IsFinite(face.Y) ||
            !double.IsFinite(face.Width) ||
            !double.IsFinite(face.Height) ||
            face.X < 0d ||
            face.Y < 0d ||
            face.Width <= 0d ||
            face.Height <= 0d)
        {
            return false;
        }

        return face.X + face.Width <= 1d &&
            face.Y + face.Height <= 1d;
    }

    private static (double X, double Y) DeterministicNoFaceOrigin(string? revisionId)
    {
        int selector = 0;
        if (Guid.TryParse(revisionId, out Guid parsed))
        {
            selector = parsed.ToByteArray()[0] & 0x03;
        }
        else if (!string.IsNullOrWhiteSpace(revisionId))
        {
            foreach (char character in revisionId)
            {
                selector = ((selector * 31) + character) & 0x03;
            }
        }

        return selector switch
        {
            0 => (0.46d, 0.46d),
            1 => (0.54d, 0.46d),
            2 => (0.46d, 0.54d),
            _ => (0.54d, 0.54d),
        };
    }
}
