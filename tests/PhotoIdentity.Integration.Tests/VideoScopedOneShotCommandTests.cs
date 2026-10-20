using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoScopedOneShotCommandTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly Guid Job = Guid.NewGuid();
    private static readonly Guid Revision = Guid.NewGuid();

    private static string[] OneShot() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_TEST",
        "--database", "photoidentity_catalogue",
        "--job-id", Job.ToString(),
        "--cache-root", Path.GetFullPath("private-video-test-cache"),
        "--ffmpeg", Path.GetFullPath("ffmpeg.exe"),
        "--ffmpeg-sha256", Hash,
        "--ffprobe", Path.GetFullPath("ffprobe.exe"),
        "--ffprobe-sha256", Hash
    ];

    [Fact]
    public void One_shot_is_dry_run_by_default_and_apply_requires_separate_acknowledgement()
    {
        VideoScopedOneShotOptions options =
            VideoScopedOneShotOptions.Parse(OneShot());
        Assert.False(options.Apply);
        Assert.False(options.Acknowledged);
        Assert.Equal(Job, options.JobId);
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse([.. OneShot(), "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse(
                [.. OneShot(), "--acknowledge-isolated-v37-rehearsal"]));
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse(
                [.. OneShot(), "--apply", "--apply",
                    "--acknowledge-isolated-v37-rehearsal"]));
        VideoScopedOneShotOptions applied = VideoScopedOneShotOptions.Parse(
        [
            .. OneShot(), "--apply", "--acknowledge-isolated-v37-rehearsal"
        ]);
        Assert.True(applied.Apply);
        Assert.True(applied.Acknowledged);
    }

    [Fact]
    public void One_shot_refuses_unselected_or_unsafe_arguments()
    {
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse([]));
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse(
                [.. OneShot(), "--job-id", Guid.NewGuid().ToString()]));
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse(
                [.. OneShot(), "--source-path", @"C:\Photos\private.mov"]));
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse(
                [.. OneShot(), "--ffprobe-sha256", new string('F', 64)]));
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse(
                [.. OneShot().Take(5), "not-a-guid", .. OneShot().Skip(6)]));
        Assert.Throws<ArgumentException>(() =>
            VideoScopedOneShotOptions.Parse(
                [.. OneShot().Take(3), "unsafe-name!", .. OneShot().Skip(4)]));
    }

    [Fact]
    public void Selected_enqueue_never_accepts_a_source_path_or_execution_flags()
    {
        string[] options =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--database", "photoidentity_catalogue",
            "--revision-id", Revision.ToString(),
            "--ffmpeg-sha256", Hash
        ];
        VideoSelectedEnqueueOptions dry =
            VideoSelectedEnqueueOptions.Parse(options);
        Assert.False(dry.Apply);
        Assert.Equal(Revision, dry.RevisionId);
        Assert.True(VideoSelectedEnqueueOptions.Parse([.. options, "--apply"]).Apply);
        Assert.Throws<ArgumentException>(() =>
            VideoSelectedEnqueueOptions.Parse([.. options, "--apply", "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoSelectedEnqueueOptions.Parse(
                [.. options, "--video-file", @"C:\Photos\private.mov"]));
        Assert.Throws<ArgumentException>(() =>
            VideoSelectedEnqueueOptions.Parse(
                [.. options, "--revision-id", Guid.NewGuid().ToString()]));
        Assert.Throws<ArgumentException>(() =>
            VideoSelectedEnqueueOptions.Parse(
                [.. options, "--ffmpeg-sha256", new string('G', 64)]));
    }

    [Fact]
    public async Task Without_postgres_setting_no_application_database_is_opened()
    {
        const string key = "PHOTOIDENTITY_WI0188_SCOPE_MISSING_TEST";
        string? before = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, null);
            var selected = VideoScopedOneShotOptions.Parse([
                .. OneShot().Skip(0).Take(0),
                "--postgres-connection-env", key,
                .. OneShot().Skip(2)
            ]);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoScopedOneShotCommandRunner.RunAsync(selected, TextWriter.Null));
        }
        finally { Environment.SetEnvironmentVariable(key, before); }
    }
}
