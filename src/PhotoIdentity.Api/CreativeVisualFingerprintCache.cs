using System.Collections.Concurrent;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Api;

/// <summary>
/// Process-local cache for deterministic perceptual hashes derived from durable review-proxy bytes.
/// Proxy content hash plus algorithm version make reuse invalidation-safe without treating this
/// optional presentation evidence as catalogue truth.
/// </summary>
public sealed class CreativeVisualFingerprintCache
{
    private readonly ConcurrentDictionary<CacheKey, PhotoPerceptualHash64> _entries = new();

    public bool TryGet(
        AssetRevisionId revisionId,
        Sha256Digest proxyContentHash,
        string algorithmVersion,
        out PhotoPerceptualHash64 hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmVersion);
        return _entries.TryGetValue(
            new CacheKey(revisionId, proxyContentHash, algorithmVersion),
            out hash);
    }

    public void Store(
        AssetRevisionId revisionId,
        Sha256Digest proxyContentHash,
        string algorithmVersion,
        PhotoPerceptualHash64 hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmVersion);
        _entries[new CacheKey(revisionId, proxyContentHash, algorithmVersion)] = hash;
    }

    internal int Count => _entries.Count;

    private readonly record struct CacheKey(
        AssetRevisionId RevisionId,
        Sha256Digest ProxyContentHash,
        string AlgorithmVersion);
}
