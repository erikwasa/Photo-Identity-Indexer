using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Transfer.Bundles;

public enum PortableBundleProfile
{
    FullImage,
    ReducedImage,
    FaceCrops,
}

public static class PortableBundleRoles
{
    public const string SourceImage = "source-image";
    public const string ReducedImage = "reduced-image";
    public const string FaceCrop = "face-crop";
    public const string ResultCrop = "result-crop";
}

public sealed record PortableBundleFile(
    string Path,
    string Role,
    long Length,
    string Sha256);

public sealed record PortableJobManifest(
    int SchemaVersion,
    string BundleId,
    string AssetRevisionId,
    string SourceContentSha256,
    PortableBundleProfile Profile,
    string ConfigurationJson,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<PortableBundleFile> Files);

public sealed record PortablePoint(double X, double Y)
{
    public NormalizedPoint ToCore() => new(X, Y);

    public static PortablePoint FromCore(NormalizedPoint point) => new(point.X, point.Y);
}

public sealed record PortableBoundingBox(double X, double Y, double Width, double Height)
{
    public NormalizedBoundingBox ToCore() => new(X, Y, Width, Height);

    public static PortableBoundingBox FromCore(NormalizedBoundingBox box) =>
        new(box.X, box.Y, box.Width, box.Height);
}

public sealed record PortableLandmarks(
    PortablePoint LeftEye,
    PortablePoint RightEye,
    PortablePoint Nose,
    PortablePoint MouthLeft,
    PortablePoint MouthRight)
{
    public NormalizedFaceLandmarks ToCore() => new(
        LeftEye.ToCore(),
        RightEye.ToCore(),
        Nose.ToCore(),
        MouthLeft.ToCore(),
        MouthRight.ToCore());

    public static PortableLandmarks FromCore(NormalizedFaceLandmarks landmarks) => new(
        PortablePoint.FromCore(landmarks.LeftEye),
        PortablePoint.FromCore(landmarks.RightEye),
        PortablePoint.FromCore(landmarks.Nose),
        PortablePoint.FromCore(landmarks.MouthLeft),
        PortablePoint.FromCore(landmarks.MouthRight));
}

public sealed record PortableFaceResult(
    int Ordinal,
    double Confidence,
    PortableBoundingBox BoundingBox,
    PortableLandmarks Landmarks,
    string CropPath,
    int CropWidth,
    int CropHeight,
    IReadOnlyList<float> Embedding);

public sealed record PortableResultManifest(
    int SchemaVersion,
    string BundleId,
    string JobManifestSha256,
    string AssetRevisionId,
    string SourceContentSha256,
    PortableBundleProfile Profile,
    string DetectorModelId,
    string DetectorModelSha256,
    string EmbedderModelId,
    string EmbedderModelSha256,
    string AlignmentProtocol,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<PortableBundleFile> Files,
    IReadOnlyList<PortableFaceResult> Faces);

public sealed record PortableJobInput(string SourcePath, string BundlePath, string Role);

public sealed record PortableJobBundleRequest(
    AssetRevisionId AssetRevisionId,
    Sha256Digest SourceContentHash,
    PortableBundleProfile Profile,
    string ConfigurationJson,
    IReadOnlyList<PortableJobInput> Inputs,
    DateTimeOffset CreatedAtUtc,
    string? BundleId = null);

public sealed record PortableProcessedFace(
    int Ordinal,
    double Confidence,
    NormalizedBoundingBox BoundingBox,
    NormalizedFaceLandmarks Landmarks,
    string CropPath,
    int CropWidth,
    int CropHeight,
    IReadOnlyList<float> Embedding);

public sealed record PortableProcessingOutput(
    ModelId DetectorModelId,
    Sha256Digest DetectorModelHash,
    ModelId EmbedderModelId,
    Sha256Digest EmbedderModelHash,
    AlignmentProtocolId AlignmentProtocol,
    IReadOnlyList<PortableProcessedFace> Faces,
    DateTimeOffset CompletedAtUtc);

public sealed record ExtractedPortableJob(
    PortableJobManifest Manifest,
    Sha256Digest ManifestHash,
    string RootDirectory)
{
    public string ResolveFile(PortableBundleFile file) =>
        PortableBundlePath.ResolveWithinRoot(RootDirectory, file.Path);
}

public sealed record ExtractedPortableResult(
    PortableResultManifest Manifest,
    Sha256Digest ManifestHash,
    string RootDirectory)
{
    public string ResolveFile(PortableBundleFile file) =>
        PortableBundlePath.ResolveWithinRoot(RootDirectory, file.Path);
}

public interface IPortableBundleProcessor
{
    Task<PortableProcessingOutput> ProcessAsync(
        ExtractedPortableJob job,
        string outputDirectory,
        CancellationToken cancellationToken);
}

public sealed class PortableBundleValidationException : InvalidDataException
{
    public PortableBundleValidationException(string message)
        : base(message)
    {
    }

    public PortableBundleValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class PortableBundlePath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/').Trim('/');
        string[] segments = normalized.Split('/');
        if (normalized.Length == 0 ||
            normalized.Contains(':') ||
            Path.IsPathRooted(normalized) ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new PortableBundleValidationException($"Bundle path '{path}' is unsafe.");
        }

        return normalized;
    }

    public static string ResolveWithinRoot(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root);
        string normalized = Normalize(path).Replace('/', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(prefix, comparison))
        {
            throw new PortableBundleValidationException($"Bundle path '{path}' escapes its extraction root.");
        }

        return resolved;
    }
}
