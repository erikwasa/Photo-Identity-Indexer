using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Core.Catalogue;

/// <summary>
/// A deliberate, already-verified local video observation. A caller must establish
/// local availability and independently compute the SHA-256 from readable source bytes.
/// Merely finding a OneDrive placeholder must not construct this record.
/// </summary>
public sealed record VerifiedLocalVideo(
    ArchiveCatalogueSource Source,
    SourceAsset Asset,
    Sha256Digest ContentHash,
    DateTimeOffset VerifiedAtUtc);

/// <summary>
/// Stable catalogue identities for the explicit video lane. A re-observed content hash
/// has the same revision ID; a changed hash gets a new immutable revision.
/// </summary>
public sealed record RegisteredVideoRevision(
    AssetId AssetId,
    AssetRevisionId RevisionId,
    bool NewAsset,
    bool NewRevision);
