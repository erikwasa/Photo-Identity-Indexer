using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoScratchCapacityGuardTests
{
    [Fact]
    public void Exact_budget_and_headroom_are_required_without_overflow()
    {
        long reserve = VideoScratchCapacityGuard.MinimumHeadroomBytes;
        Assert.True(VideoScratchCapacityGuard.HasCapacity(reserve + 1000, 1000));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(reserve + 999, 1000));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(long.MaxValue, long.MaxValue));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(-1, 1000));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(reserve + 1000, 0));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(reserve + 1000, -1));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(reserve + 1000, 1000, 0));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(0, 1));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(8L * 1024 * 1024 * 1024 + reserve,
            8L * 1024 * 1024 * 1024 + 1));
    }

    [Fact]
    public void Refuses_missing_or_unbounded_scratch_without_opening_media()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(),
            "photoidentity-scratch-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() =>
            VideoScratchCapacityGuard.EnsureCapacity(nonexistent, 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoScratchCapacityGuard.EnsureCapacity(nonexistent, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoScratchCapacityGuard.EnsureCapacity(nonexistent,
                8L * 1024 * 1024 * 1024 + 1));
    }

    [Fact]
    public void Capacity_calculation_is_usable_for_a_single_staged_input_and_tool()
    {
        const long input = 1024L * 1024 * 1024;
        const long tool = 2L * 1024 * 1024 * 1024;
        long reserve = VideoScratchCapacityGuard.MinimumHeadroomBytes;
        Assert.True(VideoScratchCapacityGuard.HasCapacity(input + tool + reserve,
            input + tool));
        Assert.False(VideoScratchCapacityGuard.HasCapacity(input + tool + reserve - 1,
            input + tool));
    }
}
