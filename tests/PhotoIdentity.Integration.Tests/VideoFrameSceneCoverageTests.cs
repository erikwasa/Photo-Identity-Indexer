using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoFrameSceneCoverageTests
{
    private static string[] Args(string cuts) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_LOCAL_TEST",
        "--database", "photoidentity_test",
        "--job-id", "ad20d6a4-90c4-43d3-8cdb-1ebf37a136b3",
        "--scene-boundaries-ms", cuts
    ];

    [Fact]
    public void Parses_only_strict_ordered_numeric_operator_annotations()
    {
        var options = VideoFrameSceneCoverageOptions.Parse(Args("1000,3000,7000"));
        Assert.Equal(new long[] { 1000, 3000, 7000 }, options.SceneCutsMs);
        Assert.Empty(VideoFrameSceneCoverageOptions.Parse(Args("none")).SceneCutsMs);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoverageOptions.Parse(Args("")));
        foreach (string invalid in new[]
        {
            "0", "1000,1000", "3000,1000", "1000,-2", "abc",
            "1000, 2000", "1000,", "none,1000",
            string.Join(",", Enumerable.Range(1, 129))
        })
            Assert.Throws<ArgumentException>(() =>
                VideoFrameSceneCoverageOptions.Parse(Args(invalid)));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoverageOptions.Parse([.. Args("none"), "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoverageOptions.Parse([.. Args("none"), "--scene-boundaries-ms", "1000"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoverageOptions.Parse(Args("1000")[..^1]));
    }

    [Fact]
    public void Counts_missed_scenes_without_exposing_timestamp_or_caption_payload()
    {
        VideoFrameSceneCoverageSummary result = VideoFrameSceneCoveragePolicy.Assess(
            12300, [3075, 9225], [3000, 6000, 9000]);
        Assert.Equal(2, result.SampledFrames);
        Assert.Equal(4, result.TotalScenes);
        Assert.Equal(2, result.CoveredScenes);
        Assert.Equal(2, result.MissedScenes);
        Assert.Equal(50, result.CoveragePercent);
        Assert.Equal(1, result.MaximumFramesInOneScene);
        Assert.Equal(new[] { 0, 2 }, result.MissedSceneIndices);
    }

    [Fact]
    public void Scene_start_is_in_new_scene_and_sparse_short_and_long_are_bounded()
    {
        var boundary = VideoFrameSceneCoveragePolicy.Assess(
            8000, [2000, 6000], [2000, 4000, 6000]);
        Assert.Equal(new[] { 0, 2 }, boundary.MissedSceneIndices);
        Assert.Equal(2, boundary.CoveredScenes);

        var oneScene = VideoFrameSceneCoveragePolicy.Assess(200, [100], []);
        Assert.Equal(100, oneScene.CoveragePercent);
        Assert.Empty(oneScene.MissedSceneIndices);

        var longVideo = VideoFrameSceneCoveragePolicy.Assess(
            3_600_000, [450_000, 3_150_000], [900_000, 1_800_000, 2_700_000]);
        Assert.Equal(2, longVideo.MissedScenes);
        Assert.Equal(50, longVideo.CoveragePercent);
    }

    [Fact]
    public void Rejects_invalid_frames_boundaries_and_nonfinite_budget_expansion()
    {
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoveragePolicy.Assess(10000, [2000, 2000], []));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoveragePolicy.Assess(10000, [9000, 1000], []));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoveragePolicy.Assess(10000, [10000], []));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoveragePolicy.Assess(10000, [2000], [10000]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneCoveragePolicy.Assess(10000, [2000], [5000, 5000]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFrameSceneCoveragePolicy.Assess(10000, [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFrameSceneCoveragePolicy.Assess(10000,
                Enumerable.Range(1, 25).Select(i => (long)i * 100).ToArray(), []));
    }
}
