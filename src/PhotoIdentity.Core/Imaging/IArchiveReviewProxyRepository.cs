using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Imaging;

/// <summary>
/// Provider-neutral durable completion metadata for one review-proxy derivative.
/// </summary>
public sealed record ArchiveReviewProxyMetadata
{
    public ArchiveReviewProxyMetadata(
        AssetRevisionId assetRevisionId,
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
            throw new ArgumentOutOfRangeException(
                nameof(encodedByteLength),
                "Encoded byte length must be positive.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Height must be positive.");
        }

        string normalizedPath = relativePath.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPath) ||
            normalizedPath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment == ".."))
        {
            throw new ArgumentException(
                "Review proxy storage path must be relative to the configured derivative root.",
                nameof(relativePath));
        }

        AssetRevisionId = assetRevisionId;
        ProfileId = profileId.Trim();
        EncodedByteLength = encodedByteLength;
        ContentHash = contentHash;
        Width = width;
        Height = height;
        GeneratedAtUtc = generatedAtUtc.ToUniversalTime();
        RelativePath = normalizedPath;
    }

    public AssetRevisionId AssetRevisionId { get; }
    public string ProfileId { get; }
    public long EncodedByteLength { get; }
    public Sha256Digest ContentHash { get; }
    public int Width { get; }
    public int Height { get; }
    public DateTimeOffset GeneratedAtUtc { get; }
    public string RelativePath { get; }
}

/// <summary>
/// Persists immutable review-proxy profile registrations and per-revision completion metadata.
/// </summary>
public interface IArchiveReviewProxyRepository
{
    Task RegisterProfileAsync(
        ReviewProxyProfile profile,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ReviewProxyProfile?> GetProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<ArchiveReviewProxyMetadata> RecordCompletionAsync(
        ArchiveReviewProxyMetadata proxy,
        CancellationToken cancellationToken = default);

    Task<ArchiveReviewProxyMetadata?> GetAsync(
        AssetRevisionId revisionId,
        string profileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata>> GetManyAsync(
        IReadOnlyCollection<AssetRevisionId> revisionIds,
        string profileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetRevisionId>> GetPendingCurrentRevisionIdsAsync(
        SourceId sourceId,
        string profileId,
        CancellationToken cancellationToken = default);
}
