using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoMetadataRefreshCommandTests
{
    private static readonly string SourceRoot =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "photoidentity-metadata-refresh"));
    private static readonly string ToolPath =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "approved-ffprobe.exe"));
    private static readonly string Revision = Guid.NewGuid().ToString("D");
    private static readonly string Digest = new('a', 64);

    private static string[] Required() =>
    [
        "--enable-video",
        "--revision", Revision,
        "--source-root", SourceRoot,
        "--ffprobe", ToolPath,
        "--ffprobe-sha256", Digest,
        "--postgres-connection-env", "PHOTOIDENTITY_REFRESH_TEST",
        "--database", "photoidentity_main"
    ];

    [Fact]
    public void Refresh_is_one_video_and_dry_run_by_default()
    {
        VideoMetadataRefreshOptions options = VideoMetadataRefreshOptions.Parse(Required());
        Assert.False(options.Apply);
        Assert.Equal(20, options.TimeoutSeconds);
        Assert.Equal(Guid.Parse(Revision), options.RevisionId);
        Assert.Equal(Digest, options.FfprobeSha256);
    }

    [Fact]
    public void Apply_requires_both_explicit_flags()
    {
        Assert.Throws<ArgumentException>(() =>
            VideoMetadataRefreshOptions.Parse([.. Required(), "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoMetadataRefreshOptions.Parse([.. Required(), "--acknowledge-metadata-only"]));

        VideoMetadataRefreshOptions approved = VideoMetadataRefreshOptions.Parse(
            [.. Required(), "--apply", "--acknowledge-metadata-only", "--timeout-seconds", "40"]);
        Assert.True(approved.Apply);
        Assert.Equal(40, approved.TimeoutSeconds);
    }

    [Fact]
    public void Wrong_scope_paths_credentials_and_bounds_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            VideoMetadataRefreshOptions.Parse(Required().Skip(1).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoMetadataRefreshOptions.Parse([.. Required(), "--unknown"]));
        Assert.Throws<ArgumentException>(() =>
            VideoMetadataRefreshOptions.Parse([.. Required(), "--revision", Guid.NewGuid().ToString()]));
        Assert.Throws<ArgumentException>(() =>
            VideoMetadataRefreshOptions.Parse([.. Required(), "--timeout-seconds", "121"]));
        Assert.Throws<ArgumentException>(() =>
            VideoMetadataRefreshOptions.Parse([.. Required(), "--timeout-seconds", "0"]));

        string[] invalidDatabase = Required();
        invalidDatabase[^1] = "photoidentity_main;DROP DATABASE";
        Assert.Throws<ArgumentException>(() => VideoMetadataRefreshOptions.Parse(invalidDatabase));

        string[] invalidVariable = Required();
        invalidVariable[^3] = "Host=127.0.0.1;Password=private";
        Assert.Throws<ArgumentException>(() => VideoMetadataRefreshOptions.Parse(invalidVariable));

        string[] invalidToolHash = Required();
        invalidToolHash[Array.IndexOf(invalidToolHash, "--ffprobe-sha256") + 1] = "1234";
        Assert.Throws<ArgumentException>(() => VideoMetadataRefreshOptions.Parse(invalidToolHash));
    }
}
