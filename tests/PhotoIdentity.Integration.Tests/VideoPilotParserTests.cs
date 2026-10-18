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
                  "color_primaries":"bt2020","color_space":"bt2020nc",
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
        Assert.Equal("bt2020", media.ColorPrimaries);
        Assert.Equal("bt2020nc", media.ColorSpace);
        Assert.True(VideoPilotCommandRunner.IsHlgPreviewCandidate(media));
        Assert.False(media.HasCreationTime);
        Assert.False(media.HasLocation);
        Assert.True(VideoPilotCommandRunner.IsSupported(media));
    }

    [Fact]
    public void Hlg_preview_requires_all_explicit_colour_evidence_and_never_accepts_pq()
    {
        VideoPilotMetadata hlg = new(
            "mov,mp4", "hevc", "Main 10", "yuv420p10le", "arib-std-b67",
            "aac", 1920, 1080, -90, 5, "30/1", false, false)
        {
            ColorPrimaries = "bt2020",
            ColorSpace = "bt2020nc"
        };
        Assert.True(VideoPilotCommandRunner.IsHlgPreviewCandidate(hlg));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { ColorPrimaries = null }));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { ColorSpace = null }));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { ColorPrimaries = "bt709" }));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { ColorSpace = "bt709" }));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { ColorTransfer = "smpte2084" }));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { PixelFormat = "yuv420p" }));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { Profile = "Main" }));
        Assert.False(VideoPilotCommandRunner.IsHlgPreviewCandidate(
            hlg with { DurationSeconds = 601 }));

        string filter = VideoPilotMediaTools.HlgPreviewFilter;
        Assert.Contains("setparams=color_primaries=bt2020:color_trc=arib-std-b67:colorspace=bt2020nc", filter);
        Assert.Contains("zscale=t=linear:npl=100,format=gbrpf32le", filter);
        Assert.Contains("tonemap=mobius:param=0.3:desat=0", filter);
        Assert.Contains("zscale=p=709:t=709:m=709:r=limited,format=yuv420p", filter);
        Assert.Contains("scale=1280:1280:force_original_aspect_ratio=decrease", filter);
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
    public void Apple_embedded_capture_and_ISO6709_coordinates_take_precedence_over_generic_creation()
    {
        const string json = """
            {
              "streams":[{"codec_type":"video","codec_name":"hevc","profile":"Main 10",
                          "width":1920,"height":1080,
                          "tags":{"creation_time":"2020-01-01T01:02:03Z"}}],
              "format":{"format_name":"mov,mp4","duration":"10.5",
                "tags":{
                  "creation_time":"2025-12-01T12:00:00Z",
                  "com.apple.quicktime.creationdate":"2026-02-16T14:12:16+0100",
                  "com.apple.quicktime.location.ISO6709":"+59.325000+018.065000+010.2/",
                  "com.apple.quicktime.make":"Apple",
                  "com.apple.quicktime.model":"iPhone 15 Pro"
                }}
            }
            """;
        VideoPilotMetadata result = VideoPilotMetadataParser.Parse(json);
        Assert.True(result.HasCreationTime);
        Assert.True(result.HasLocation);
        Assert.Equal(new DateTimeOffset(2026, 2, 16, 13, 12, 16, TimeSpan.Zero),
            result.CaptureAtUtc);
        Assert.Equal("format:com.apple.quicktime.creationdate", result.CaptureTimeProvenance);
        Assert.Equal(59.325, result.GpsLatitude);
        Assert.Equal(18.065, result.GpsLongitude);
        Assert.Equal("Apple", result.CameraMake);
        Assert.Equal("iPhone 15 Pro", result.CameraModel);

        // Pilot report must not leak private camera, location, or capture fields.
        string serialized = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("59.325", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("iPhone 15 Pro", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-02-16", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureTimeProvenance", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-02-16 14:12:16", null)]
    [InlineData("2026-02-16T14:12:16Z", "2026-02-16T14:12:16+00:00")]
    [InlineData("2026-02-16T14:12:16-05:30", "2026-02-16T19:42:16+00:00")]
    [InlineData("2026-02-16T14:12:16+2530", null)]
    [InlineData("2026-02-16T14:12:16+02", null)]
    [InlineData("invalid", null)]
    public void Capture_time_requires_explicit_valid_timezone(string tag, string? utc)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            format = new
            {
                tags = new Dictionary<string, string>
                {
                    ["com.apple.quicktime.creationdate"] = tag
                }
            }
        });
        VideoPilotMetadata result = VideoPilotMetadataParser.Parse(json);
        Assert.True(result.HasCreationTime);
        Assert.Equal(utc is null ? null : DateTimeOffset.Parse(
            utc, System.Globalization.CultureInfo.InvariantCulture), result.CaptureAtUtc);
        Assert.Equal(utc is null ? null : "format:com.apple.quicktime.creationdate",
            result.CaptureTimeProvenance);
    }

    [Fact]
    public void Invalid_Apple_local_clock_falls_back_to_valid_generic_UTC_and_stream_tags()
    {
        const string json = """
            {"format":{"tags":{"com.apple.quicktime.creationdate":"2026-02-16T14:12:16",
              "creation_time":"2026-02-16T14:12:16.500Z",
              "com.apple.quicktime.location.ISO6709":"0,0"}},
             "streams":[{"codec_type":"video","tags":{
               "com.apple.quicktime.location.ISO6709":"-33.1234+151.2345/",
               "com.apple.quicktime.make":"Apple"}}]}
            """;
        VideoPilotMetadata result = VideoPilotMetadataParser.Parse(json);
        Assert.Equal(new DateTimeOffset(2026, 2, 16, 14, 12, 16, 500, TimeSpan.Zero),
            result.CaptureAtUtc);
        Assert.Equal("format:creation_time", result.CaptureTimeProvenance);
        Assert.Equal(-33.1234, result.GpsLatitude);
        Assert.Equal(151.2345, result.GpsLongitude);
        Assert.Equal("Apple", result.CameraMake);
        Assert.Null(result.CameraModel);
    }

    [Theory]
    [InlineData("+91.0000+018.0000/")]
    [InlineData("+59.0000+181.0000/")]
    [InlineData("+59.0000+018.0000")]
    [InlineData("59.0000,18.0000")]
    [InlineData("+5900.00+01800.00/")]
    [InlineData("+59.0+018.0/extra")]
    [InlineData("bad")]
    public void Malformed_or_ambiguous_location_remains_unknown(string coordinate)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            format = new
            {
                tags = new Dictionary<string, string>
                {
                    ["com.apple.quicktime.location.ISO6709"] = coordinate
                }
            }
        });
        VideoPilotMetadata result = VideoPilotMetadataParser.Parse(json);
        Assert.True(result.HasLocation);
        Assert.Null(result.GpsLatitude);
        Assert.Null(result.GpsLongitude);
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
        Assert.False(parsed.PreviewHlg);
        VideoPilotOptions preview = VideoPilotOptions.Parse(
            ["--enable-video", "--preview-hlg", .. args]);
        Assert.True(preview.PreviewHlg);
        Assert.Throws<ArgumentException>(() => VideoPilotOptions.Parse(
            ["--preview-hlg", .. args]));
        Assert.Throws<ArgumentException>(() => VideoPilotOptions.Parse(
            ["--enable-video", "--preview-hlg", "--preview-hlg", .. args]));
        Assert.Throws<ArgumentException>(() => VideoPilotOptions.Parse(
            ["--enable-video", .. args, "--max-videos", "999"]));
        Assert.Throws<ArgumentException>(() => VideoPilotOptions.Parse(
            ["--enable-video", .. args, "--unknown", "value"]));
    }
}
