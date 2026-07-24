using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Sources;

public enum AssetAvailability
{
    Local,
    OnlineOnly,
    Downloading,
    Unavailable,
    Error,
}

public readonly record struct SourceAssetReference
{
    public SourceAssetReference(SourceId sourceId, string itemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        SourceId = sourceId;
        ItemKey = itemKey.Trim();
    }

    public SourceId SourceId { get; }
    public string ItemKey { get; }
}

public sealed record SourceAsset
{
    public SourceAsset(
        SourceAssetReference reference,
        string relativePath,
        string mediaType,
        long sizeBytes,
        DateTimeOffset lastWriteTimeUtc,
        AssetAvailability availability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        Reference = reference;
        RelativePath = relativePath.Trim();
        MediaType = mediaType.Trim();
        SizeBytes = sizeBytes;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Availability = availability;
    }

    public SourceAssetReference Reference { get; }
    public string RelativePath { get; }
    public string MediaType { get; }
    public long SizeBytes { get; }
    public DateTimeOffset LastWriteTimeUtc { get; }
    public AssetAvailability Availability { get; }
}

public sealed record SourceScanOptions(string? RelativeRoot = null, bool Recursive = true);

public interface IAssetSource
{
    IAsyncEnumerable<SourceAsset> EnumerateAsync(
        SourceScanOptions options,
        CancellationToken cancellationToken);

    Task<AssetAvailability> GetAvailabilityAsync(
        SourceAssetReference asset,
        CancellationToken cancellationToken);

    Task<Stream> OpenContentAsync(
        SourceAssetReference asset,
        CancellationToken cancellationToken);
}

public sealed record StagingOptions
{
    public StagingOptions(string targetDirectory, bool verifyContentHash = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        TargetDirectory = targetDirectory.Trim();
        VerifyContentHash = verifyContentHash;
    }

    public string TargetDirectory { get; }
    public bool VerifyContentHash { get; }
}

public sealed record StagedAsset
{
    public StagedAsset(
        SourceAssetReference source,
        string localPath,
        long sizeBytes,
        Sha256Digest contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        Source = source;
        LocalPath = localPath.Trim();
        SizeBytes = sizeBytes;
        ContentHash = contentHash;
    }

    public SourceAssetReference Source { get; }
    public string LocalPath { get; }
    public long SizeBytes { get; }
    public Sha256Digest ContentHash { get; }
}

public interface IAssetStager
{
    Task<StagedAsset> StageAsync(
        SourceAssetReference asset,
        StagingOptions options,
        CancellationToken cancellationToken);
}
