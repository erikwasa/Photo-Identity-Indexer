using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Catalogue;

/// <summary>
/// Provider-neutral projection of one immutable asset revision together with the source location
/// needed by application services that resolve or hydrate the authoritative original.
/// </summary>
public interface IAssetRevisionStorageDescriptor
{
    AssetRevisionId RevisionId { get; }
    AssetId AssetId { get; }
    SourceId SourceId { get; }
    string SourceKind { get; }
    string RootLocator { get; }
    string SourceKey { get; }
    Sha256Digest ContentHash { get; }
    long SizeBytes { get; }
    string? MediaType { get; }
}

public sealed record AssetRevisionLookup(
    AssetRevisionId RevisionId,
    AssetId AssetId,
    SourceId SourceId,
    string SourceKind,
    string RootLocator,
    string SourceKey,
    Sha256Digest ContentHash,
    long SizeBytes,
    string? MediaType) : IAssetRevisionStorageDescriptor;

/// <summary>
/// Database-neutral read boundary for resolving immutable catalogue revisions without exposing
/// a concrete persistence adapter to application services.
/// </summary>
public interface IAssetRevisionLookupRepository
{
    Task<AssetRevisionLookup?> GetRevisionAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<AssetRevisionLookup?> FindRevisionAsync(
        string sourceKey,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default);
}
