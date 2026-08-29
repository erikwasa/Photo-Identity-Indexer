using System.Collections.Concurrent;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Api;

public sealed record SlideshowOriginalLeaseMember(
    AssetRevisionId RevisionId,
    AssetId AssetId);

/// <summary>
/// Ephemeral eviction protection for active slideshow sessions. Durable hydration ownership remains
/// in the existing managed-hydration repository; these leases only prevent managed LRU eviction
/// while a live slideshow promises best-quality originals. Expiry is deliberately short and is
/// refreshed by the active browser so a crash/restart or abandoned tab cannot strand content.
/// </summary>
public sealed class SlideshowOriginalLeaseRegistry
{
    public static TimeSpan DefaultLeaseDuration { get; } = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, Lease> _leases = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuration;

    public SlideshowOriginalLeaseRegistry(TimeProvider timeProvider)
        : this(timeProvider, DefaultLeaseDuration)
    {
    }

    internal SlideshowOriginalLeaseRegistry(
        TimeProvider timeProvider,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        _timeProvider = timeProvider;
        _leaseDuration = leaseDuration;
    }

    public void Protect(
        Guid sessionId,
        IEnumerable<SlideshowOriginalLeaseMember> members)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Slideshow session identifier cannot be empty.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(members);
        SlideshowOriginalLeaseMember[] materialized = members
            .DistinctBy(member => member.RevisionId)
            .ToArray();
        _leases[sessionId] = new Lease(
            materialized,
            _timeProvider.GetUtcNow().Add(_leaseDuration));
    }

    public bool Touch(Guid sessionId)
    {
        CleanupExpired();

        if (!_leases.TryGetValue(sessionId, out Lease? lease))
        {
            return false;
        }

        _leases[sessionId] = lease with
        {
            ExpiresAtUtc = _timeProvider.GetUtcNow().Add(_leaseDuration),
        };
        return true;
    }

    public void Release(Guid sessionId) =>
        _leases.TryRemove(sessionId, out _);

    public bool IsProtected(
        AssetRevisionId? revisionId,
        AssetId assetId)
    {
        CleanupExpired();

        foreach (Lease lease in _leases.Values)
        {
            if (lease.Members.Any(member =>
                (revisionId is AssetRevisionId value && member.RevisionId == value) ||
                member.AssetId == assetId))
            {
                return true;
            }
        }

        return false;
    }

    public bool Contains(Guid sessionId)
    {
        CleanupExpired();
        return _leases.ContainsKey(sessionId);
    }

    internal int ActiveLeaseCount
    {
        get
        {
            CleanupExpired();
            return _leases.Count;
        }
    }

    private void CleanupExpired()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((Guid sessionId, Lease lease) in _leases)
        {
            if (lease.ExpiresAtUtc <= now)
            {
                _leases.TryRemove(sessionId, out _);
            }
        }
    }

    private sealed record Lease(
        IReadOnlyList<SlideshowOriginalLeaseMember> Members,
        DateTimeOffset ExpiresAtUtc);
}
