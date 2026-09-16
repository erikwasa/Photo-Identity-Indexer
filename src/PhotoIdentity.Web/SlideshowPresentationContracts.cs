namespace PhotoIdentity.Web.Contracts;

public sealed record SlideshowFaceBoxResponse(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record SlideshowFaceGeometryResponse(
    IReadOnlyList<SlideshowFaceBoxResponse> Faces);
