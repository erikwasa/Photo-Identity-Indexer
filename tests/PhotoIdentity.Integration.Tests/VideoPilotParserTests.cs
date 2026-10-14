using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoPilotParserTests
{
    [Fact]
    public void Technical_stream_parser_handles_rotated_hevc_main10_hlg_and_missing_tags()
    {
        const string json = """
            {
              "streams": [
                {
                  "codec_type":"video",
                  "codec_name":"hevc",
                  "profile":"Main 10",
                  "pix_fmt":"yuv420p10le",
                  "color_transfer":"arib-std-b67",
                  "width":1920,"height":1080,
                  "avg_frame_rate":"56600/1887",
                  "side_data_list":[{"rotation":-90}]
                },
                { "codec_type":"audio","codec_name":"aac" }
              ],
              "format": {"format_name":"mov,mp4","duration":"11.25"}
            }
            """;
        VideoPilotMetadata media = VideoPilotMetadataParser.Parse(json);
        Assert.Equal("hevc", media.VideoCodec);
        Assert.Equal("Main 10", media.Profile);
        Assert.Equal("aac", media.AudioCodec);
        Assert.Equal(-90, media.RotationDegrees);
        Assert.Equal(11.25, media.DurationSeconds);
        Assert.Equal("arib-std-b67", media.ColorTransfer);
        Assert.False(media.HasCreationTime);
        Assert.False(media.HasLocation);
        Assert.True(VideoPilotCommandRunner.IsSupported(media));
    }

    [Fact]
    public void Optional_tags_are_safe_for_empty_missing_or_populated_formats()
    {
        VideoPilotMetadata withoutTags = VideoPilotMetadataParser.Parse(
            """{"format":{"duration":"1"},"streams":[{"codec_type":"video","codec_name":"h264","profile":"High","width":1280,"height":720}]}""");
        VideoPilotMetadata emptyTags = VideoPilotMetadataParser.Parse(
            """{"format":{"duration":"1","tags":{}},"streams":[{"codec_type":"video","codec_name":"h264","profile":"Main","width":1280,"height":720}]}""");
        VideoPilotMetadata presentTags = VideoPilotMetadataParser.Parse(
            """{"format":{"duration":"1","tags":{"creation_time":"example","com.apple.quicktime.location.ISO6709":"private"}},"streams":[]}""");
        Assert.False(withoutTags.HasCreationTime);
        Assert.False(emptyTags.HasLocation);
        Assert.True(presentTags.HasCreationTime);
        Assert.True(presentTags.HasLocation);
        Assert.Null(emptyTags.RotationDegrees);
        Assert.Null(presentTags.VideoCodec);
    }

    [Fact]
    public void Unsupported_formats_do_not_silently_fall_back_to_photo_behavior()
    {
        Assert.False(VideoPilotCommandRunner.IsSupported(new VideoPilotMetadata(
            "mov", "mpeg4", "Simple", null, null, null, 640, 480, null, 1, null, false, false)));
        Assert.False(VideoPilotCommandRunner.IsSupported(new VideoPilotMetadata(
            "mov", "hevc", "Main", null, null, null, 640, 480, null, 601, null, false, false)));
    }

    [Fact]
    public void Pilot_options_require_explicit_opt_in_and_bounded_execution()
    {
        string[] args =
        [
            "--source", "C:\\Archive", "--derivatives", "C:\\Derivatives",
            "--ffprobe", "C:\\ffprobe.exe", "--ffmpeg", "C:\\ffmpeg.exe"
        ];
        Assert.Throws<ArgumentException>(() => VideoPilotOptions.Parse(args));
        VideoPilotOptions parsed = VideoPilotOptions.Parse(["--enable-video", .. args]);
        Assert.Equal(8, parsed.MaximumVideos);
        Assert.Equal(20, parsed.TimeoutSeconds);
        Assert.Throws<ArgumentException>(() => VideoPilotOptions.Parse(
            ["--enable-video", .. args, "--max-videos", "999"]));
        Assert.Throws<ArgumentException>(() => VideoPilotOptions.Parse(
            ["--enable-video", .. args, "--unknown", "value"]));
    }
}
