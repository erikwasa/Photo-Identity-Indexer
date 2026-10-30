using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProxyRetentionPolicyTests
{
    private static readonly VideoProxyRetentionLimits Limits = new(1000, 400);

    [Fact]
    public void Fits_without_eviction_and_reserves_incoming_bytes()
    {
        var result = VideoProxyRetentionPolicy.Plan(Limits,
            [Entry(1, 250, 0)], 400);
        Assert.True(result.Admitted);
        Assert.Empty(result.EvictionIds);
        Assert.Equal(650, result.RemainingBytes);
    }

    [Fact]
    public void Evicts_oldest_unpinned_first_with_deterministic_tie_break()
    {
        var first = Entry(1, 250, 0);
        var second = Entry(2, 250, 0);
        var pinned = Entry(3, 400, -1, pinned: true);
        var result = VideoProxyRetentionPolicy.Plan(Limits,
            [pinned, second, first], 400);
        Assert.True(result.Admitted);
        Assert.Equal([first.DerivativeId, second.DerivativeId], result.EvictionIds);
        Assert.Equal(800, result.RemainingBytes);
    }

    [Fact]
    public void No_partial_eviction_when_capacity_cannot_be_reclaimed()
    {
        var result = VideoProxyRetentionPolicy.Plan(Limits,
            [Entry(1, 900, 0, pinned: true)], 200);
        Assert.False(result.Admitted);
        Assert.Equal("insufficient-evictable-capacity", result.Reason);
        Assert.Empty(result.EvictionIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(401)]
    public void Invalid_incoming_size_is_rejected(long bytes)
    {
        var result = VideoProxyRetentionPolicy.Plan(Limits, [], bytes);
        Assert.False(result.Admitted);
        Assert.Empty(result.EvictionIds);
    }

    [Fact]
    public void Invalid_inventory_and_overflow_fail_closed()
    {
        var a = Entry(1, 300, 0);
        Assert.Equal("invalid-cache-inventory",
            VideoProxyRetentionPolicy.Plan(Limits, [a, a], 100).Reason);
        Assert.Equal("invalid-cache-inventory",
            VideoProxyRetentionPolicy.Plan(Limits, [Entry(1, -5, 0)], 100).Reason);
        Assert.Equal("invalid-cache-inventory",
            VideoProxyRetentionPolicy.Plan(
                new VideoProxyRetentionLimits(long.MaxValue, 100),
                [Entry(1, long.MaxValue, 0), Entry(2, 1, 1)], 100).Reason);
    }


    [Fact]
    public void Existing_over_quota_inventory_can_be_reclaimed_without_overflow()
    {
        var oldest = Entry(1, 800, 0);
        var newer = Entry(2, 800, 1);
        VideoProxyRetentionPlan plan = VideoProxyRetentionPolicy.Plan(
            Limits, [newer, oldest], 100);
        Assert.True(plan.Admitted);
        Assert.Equal([oldest.DerivativeId], plan.EvictionIds);
        Assert.Equal(900, plan.RemainingBytes);
    }

    [Fact]
    public void Invalid_limits_throw_without_an_eviction_plan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoProxyRetentionPolicy.Plan(new(100, 200), [], 50));
    }

    private static VideoProxyRetentionEntry Entry(
        int id, long bytes, int minutes, bool pinned = false) =>
        new(new Guid(id, 0, 0, new byte[8]), bytes,
            DateTimeOffset.UnixEpoch.AddMinutes(minutes), pinned);
}
