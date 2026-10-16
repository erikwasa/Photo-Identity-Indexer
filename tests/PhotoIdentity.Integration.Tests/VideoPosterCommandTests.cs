using PhotoIdentity.Cli;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoPosterCommandTests
{
    [Fact]
    public void Posters_require_explicit_video_opt_in_local_catalogue_and_separate_roots()
    {
        string[] args =
        [
            "--source", "C:\\Archive", "--derivatives", "D:\\VideoDerivatives",
            "--ffmpeg", "C:\\Tools\\ffmpeg.exe",
            "--postgres-connection-env", "PRIVATE_POSTGRES",
            "--database", "photoidentity"
        ];
        Assert.Throws<ArgumentException>(() => VideoPosterOptions.Parse(args));
        VideoPosterOptions options = VideoPosterOptions.Parse(["--enable-video", .. args]);
        Assert.False(options.Apply);
        Assert.Equal(8, options.MaximumVideos);
        Assert.Equal(20, options.TimeoutSeconds);
        Assert.Equal("photoidentity", options.DatabaseName);
        Assert.True(VideoPosterOptions.Parse(["--enable-video", .. args, "--apply"]).Apply);

        Assert.Throws<ArgumentException>(() => VideoPosterOptions.Parse(
            ["--enable-video", .. args, "--max-videos", "26"]));
        Assert.Throws<ArgumentException>(() => VideoPosterOptions.Parse(
            ["--enable-video", .. args, "--timeout-seconds", "121"]));
        Assert.Throws<ArgumentException>(() => VideoPosterOptions.Parse(
            ["--enable-video", .. args, "--apply", "--apply"]));
        Assert.Throws<ArgumentException>(() => VideoPosterOptions.Parse(
            ["--enable-video", .. args, "--unknown", "value"]));
    }

    [Fact]
    public void Poster_identity_is_revision_and_tool_bound_and_untrusted_paths_are_refused()
    {
        var revision = AssetRevisionId.New();
        string tool = new('a', 64);
        Assert.Equal($"video-posters/{VideoPosterCommandRunner.Profile}/{revision.Value:D}/{tool}.jpg",
            VideoPosterCommandRunner.RelativePosterPath(revision, tool));
        Assert.Throws<ArgumentException>(() => VideoPosterCommandRunner.RelativePosterPath(
            revision, "../secret"));
        Assert.Throws<ArgumentException>(() => VideoPosterCommandRunner.RelativePosterPath(
            revision, new string('A', 64)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("bt709", true)]
    [InlineData("arib-std-b67", false)]
    [InlineData("smpte2084", false)]
    public void Sdr_only_policy_defer_HLG_and_PQ_poster_generation(string? transfer, bool eligible)
    {
        VideoRevisionInspection inspection = new(
            AssetRevisionId.New(), "supported", null, "mov,mp4",
            "hevc", "Main 10", "aac", "yuv420p10le", transfer,
            1920, 1080, -90, 42m, "30/1",
            null, null, null, null, null, null,
            DateTimeOffset.UtcNow, new string('b', 64), "wi0151-ffprobe-metadata-v2");
        Assert.Equal(eligible, VideoPosterCommandRunner.IsEligible(inspection));
        Assert.False(VideoPosterCommandRunner.IsEligible(
            inspection with { InspectionStatus = "unsupported" }));
    }

    [Fact]
    public void Jpeg_dimensions_are_bounded_and_unrelated_files_are_refused()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "photoidentity-wi0151-jpeg-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            byte[] jpeg = new byte[120];
            jpeg[0] = 0xff; jpeg[1] = 0xd8;
            jpeg[2] = 0xff; jpeg[3] = 0xc0; // SOF0
            jpeg[4] = 0; jpeg[5] = 17; // frame segment length
            jpeg[6] = 8; // precision
            jpeg[7] = 0x02; jpeg[8] = 0xd0; // 720 high
            jpeg[9] = 0x05; jpeg[10] = 0x00; // 1280 wide
            File.WriteAllBytes(path, jpeg);
            Assert.Equal((1280, 720), VideoPosterCommandRunner.ReadJpegDimensions(path));
            jpeg[9] = 0x05; jpeg[10] = 0x01;
            File.WriteAllBytes(path, jpeg);
            Assert.Throws<InvalidDataException>(() =>
                VideoPosterCommandRunner.ReadJpegDimensions(path));
            File.WriteAllText(path, "not a jpeg");
            Assert.Throws<InvalidDataException>(() =>
                VideoPosterCommandRunner.ReadJpegDimensions(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Derivative_root_cannot_contain_source_or_be_inside_it()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-wi0151-roots-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string nested = Path.Combine(source, "derivatives");
        string separate = Path.Combine(root, "derivatives");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(separate);
        try
        {
            Assert.Throws<ArgumentException>(() =>
                VideoPosterCommandRunner.ValidateRoots(source, nested));
            Assert.Throws<ArgumentException>(() =>
                VideoPosterCommandRunner.ValidateRoots(nested, source));
            VideoPosterCommandRunner.ValidateRoots(source, separate);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
