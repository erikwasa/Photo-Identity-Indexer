namespace PhotoIdentity.Core.Processing;

/// <summary>
/// WI-0189: deterministic, side-effect-free quota planning for application-owned
/// playable proxies. This policy never deletes anything; callers must independently
/// verify canonical cache ownership, revision fencing and lease state before eviction.
/// </summary>
public sealed record VideoProxyRetentionLimits(long MaximumCacheBytes, long MaximumProxyBytes)
{
    public void Validate()
    {
        if (MaximumCacheBytes <= 0 || MaximumProxyBytes <= 0 ||
            MaximumProxyBytes > MaximumCacheBytes)
            throw new ArgumentOutOfRangeException(nameof(MaximumCacheBytes),
                "Proxy and cache budgets must be positive and the per-file cap cannot exceed the cache cap.");
    }
}

/// <summary>
/// Opaque catalogue identity only. Paths and source names deliberately cannot
/// enter the planning API. Oldest access first; stable identity breaks ties.
/// </summary>
public sealed record VideoProxyRetentionEntry(
    Guid DerivativeId, long Bytes, DateTimeOffset LastAccessUtc, bool Pinned);

public sealed record VideoProxyRetentionPlan(
    bool Admitted, IReadOnlyList<Guid> EvictionIds, long RemainingBytes, string Reason);

public static class VideoProxyRetentionPolicy
{
    public static VideoProxyRetentionPlan Plan(
        VideoProxyRetentionLimits limits,
        IReadOnlyCollection<VideoProxyRetentionEntry> existing,
        long incomingBytes)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(existing);
        limits.Validate();
        if (incomingBytes <= 0 || incomingBytes > limits.MaximumProxyBytes)
            return new(false, Array.Empty<Guid>(), 0, "proxy-size-exceeds-limit");

        long used = 0;
        HashSet<Guid> seen = new();
        foreach (VideoProxyRetentionEntry item in existing)
        {
            if (item.DerivativeId == Guid.Empty || !seen.Add(item.DerivativeId) ||
                item.Bytes <= 0 || item.Bytes > limits.MaximumCacheBytes ||
                used > limits.MaximumCacheBytes - item.Bytes)
                return new(false, Array.Empty<Guid>(), 0, "invalid-cache-inventory");
            used += item.Bytes;
        }

        List<Guid> victims = new();
        foreach (VideoProxyRetentionEntry item in existing
            .Where(x => !x.Pinned)
            .OrderBy(x => x.LastAccessUtc)
            .ThenBy(x => x.DerivativeId))
        {
            if (used <= limits.MaximumCacheBytes - incomingBytes)
                break;
            used -= item.Bytes;
            victims.Add(item.DerivativeId);
        }

        if (used > limits.MaximumCacheBytes - incomingBytes)
            return new(false, Array.Empty<Guid>(), used, "insufficient-evictable-capacity");
        return new(true, victims, used + incomingBytes, "admitted");
    }
}
