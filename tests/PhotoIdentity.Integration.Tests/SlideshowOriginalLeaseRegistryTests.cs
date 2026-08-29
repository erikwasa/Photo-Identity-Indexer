using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowOriginalLeaseRegistryTests
{
    [Fact]
    public void Active_lease_protects_revision_and_asset_until_released()
    {
        ManualTimeProvider time = new(new DateTimeOffset(
            2026, 8, 29, 19, 0, 0, TimeSpan.Zero));
        SlideshowOriginalLeaseRegistry registry = new(time);
        Guid sessionId = Guid.NewGuid();
        AssetRevisionId revisionId = AssetRevisionId.New();
        AssetId assetId = AssetId.New();

        registry.Protect(
            sessionId,
            [new SlideshowOriginalLeaseMember(revisionId, assetId)]);

        Assert.True(registry.Contains(sessionId));
        Assert.True(registry.IsProtected(revisionId, assetId));

        registry.Release(sessionId);

        Assert.False(registry.Contains(sessionId));
        Assert.False(registry.IsProtected(revisionId, assetId));
    }

    [Fact]
    public void Abandoned_lease_expires_without_durable_cleanup()
    {
        ManualTimeProvider time = new(new DateTimeOffset(
            2026, 8, 29, 19, 30, 0, TimeSpan.Zero));
        SlideshowOriginalLeaseRegistry registry = new(time);
        Guid sessionId = Guid.NewGuid();
        AssetRevisionId revisionId = AssetRevisionId.New();
        AssetId assetId = AssetId.New();

        registry.Protect(
            sessionId,
            [new SlideshowOriginalLeaseMember(revisionId, assetId)]);

        time.Advance(SlideshowOriginalLeaseRegistry.DefaultLeaseDuration + TimeSpan.FromSeconds(1));

        Assert.False(registry.Contains(sessionId));
        Assert.False(registry.IsProtected(revisionId, assetId));
    }

    [Fact]
    public void Heartbeat_extends_the_ephemeral_lease()
    {
        ManualTimeProvider time = new(new DateTimeOffset(
            2026, 8, 29, 20, 0, 0, TimeSpan.Zero));
        SlideshowOriginalLeaseRegistry registry = new(time);
        Guid sessionId = Guid.NewGuid();
        AssetRevisionId revisionId = AssetRevisionId.New();
        AssetId assetId = AssetId.New();

        registry.Protect(
            sessionId,
            [new SlideshowOriginalLeaseMember(revisionId, assetId)]);

        time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(registry.Touch(sessionId));

        time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(registry.Contains(sessionId));
        Assert.True(registry.IsProtected(revisionId, assetId));
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _utcNow = initial;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }
}
