using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record FaceReviewDerivativeRecord
{
    public FaceReviewDerivativeRecord(
        FaceOccurrenceId faceOccurrenceId,
        string profileId,
        long encodedByteLength,
        Sha256Digest contentHash,
        int width,
        int height,
        DateTimeOffset generatedAtUtc,
        string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (encodedByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(encodedByteLength));
        }
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        string normalizedPath = relativePath.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPath) ||
            normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            throw new ArgumentException(
                "Face review derivative path must be relative to the configured derivative root.",
                nameof(relativePath));
        }

        FaceOccurrenceId = faceOccurrenceId;
        ProfileId = profileId.Trim();
        EncodedByteLength = encodedByteLength;
        ContentHash = contentHash;
        Width = width;
        Height = height;
        GeneratedAtUtc = generatedAtUtc.ToUniversalTime();
        RelativePath = normalizedPath;
    }

    public FaceOccurrenceId FaceOccurrenceId { get; }
    public string ProfileId { get; }
    public long EncodedByteLength { get; }
    public Sha256Digest ContentHash { get; }
    public int Width { get; }
    public int Height { get; }
    public DateTimeOffset GeneratedAtUtc { get; }
    public string RelativePath { get; }
}

public sealed record FaceReviewGeometry(
    FaceOccurrenceId FaceOccurrenceId,
    NormalizedBoundingBox BoundingBox);
