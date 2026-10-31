using System.Text.Json;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoRegisteredFramePilotTests
{
    private static string[] Valid(string root) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0190_TEST",
        "--database", "photoidentity_catalogue",
        "--job-id", Guid.Parse("00000000-0000-4000-8000-000000000001").ToString(),
        "--cache-root", root,
        "--scratch-root", root,
        "--ffmpeg", Path.Combine(root, "ffmpeg.exe"),
        "--ffmpeg-sha256", new string('a', 64),
        "--clip-model", Path.Combine(root, "clip.onnx"),
        "--clip-vocab", Path.Combine(root, "vocab.json"),
        "--clip-merges", Path.Combine(root, "merges.txt"),
        "--max-frames", "4"
    ];

    [Fact]
    public void Read_only_pilot_is_explicit_no_original_source_and_defaults_to_dry_run()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "wi0190-options"));
        VideoRegisteredFramePilotOptions options =
            VideoRegisteredFramePilotOptions.Parse(Valid(root));
        Assert.False(options.Apply);
        Assert.Equal(4, options.MaximumFrames);
        Assert.True(VideoRegisteredFramePilotOptions.Parse(
            [.. Valid(root), "--apply"]).Apply);
        Assert.Equal(1, VideoRegisteredFramePilotOptions.Parse(
            [.. Valid(root).SkipLast(2), "--max-frames", "1"]).MaximumFrames);
        foreach (string[] forbidden in new[]
        {
            new[] { "--source", "C:\\private\\family.mov" },
            new[] { "--original-path", "C:\\private\\family.mov" },
            new[] { "--online-only", "yes" },
            new[] { "--apply", "--apply" },
            new[] { "--max-frames", "5" },
            new[] { "--max-frames", "0" },
            new[] { "--job-id", Guid.NewGuid().ToString() },
            new[] { "--ffmpeg-sha256", new string('b', 64) },
            new[] { "--postgres-connection-env", "DIFFERENT" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoRegisteredFramePilotOptions.Parse([.. Valid(root), .. forbidden]));
    }

    [Theory]
    [InlineData("video-derivatives/00000000000040008000000000000001/11111111111141118111111111111111.mp4", true)]
    [InlineData("video-derivatives/00000000000040008000000000000002/11111111111141118111111111111111.mp4", false)]
    [InlineData("../video-derivatives/00000000000040008000000000000001/11111111111141118111111111111111.mp4", false)]
    [InlineData("video-derivatives/00000000000040008000000000000001/../../private.mp4", false)]
    public void Registry_path_shape_does_not_accept_unregistered_or_traversing_paths(
        string path, bool expected)
    {
        Guid job = Guid.Parse("00000000-0000-4000-8000-000000000001");
        Assert.Equal(expected, PostgresVideoFrameProxyPilotSource.IsCanonicalProxyPath(path, job));
    }

    [Fact]
    public async Task Dry_run_needs_only_marked_empty_temp_root_not_models_or_database()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-wi0190-registered-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, VideoSyntheticProxyPilotCommandRunner.RootMarker),
                VideoSyntheticProxyPilotCommandRunner.MarkerText);
            var options = VideoRegisteredFramePilotOptions.Parse(Valid(root));
            using StringWriter writer = new();
            Assert.Equal(0, await VideoRegisteredFramePilotCommandRunner.RunAsync(
                options, writer));
            using JsonDocument report = JsonDocument.Parse(writer.ToString());
            JsonElement result = report.RootElement;
            Assert.True(result.GetProperty("dryRun").GetBoolean());
            Assert.False(result.GetProperty("originalsOpened").GetBoolean());
            Assert.False(result.GetProperty("oneDriveHydrationRequested").GetBoolean());
            Assert.False(result.GetProperty("evidencePersisted").GetBoolean());
            Assert.False(result.GetProperty("schemaChanged").GetBoolean());
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task Frame_model_budget_is_independent_and_can_outlast_short_decode_work()
    {
        // This exercises a model-only clock without FFmpeg, Ollama, or media.
        // The production frame decoder retains its own 60-second tool budget.
        int value = await VideoFrameModelInferenceBudget.RunAsync(
            async token =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
                return 42;
            },
            CancellationToken.None,
            TimeSpan.FromSeconds(1));
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task Frame_model_timeout_is_classified_as_timeout_without_unbounded_fallback()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            VideoFrameModelInferenceBudget.RunAsync(
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return 0;
                },
                CancellationToken.None,
                TimeSpan.FromMilliseconds(35)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            VideoFrameModelInferenceBudget.RunAsync(
                _ => Task.FromResult(1),
                CancellationToken.None,
                TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public async Task Overall_pilot_cancellation_is_not_misreported_as_per_frame_timeout()
    {
        using CancellationTokenSource overall = new();
        overall.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VideoFrameModelInferenceBudget.RunAsync(
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return 0;
                }, overall.Token));
    }

}
