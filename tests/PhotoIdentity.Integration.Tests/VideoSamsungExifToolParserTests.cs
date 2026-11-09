using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoSamsungExifToolParserTests
{
    [Fact]
    public void Official_grouped_SamsungModel_is_accepted_without_guessing_other_fields()
    {
        const string json = """
            [{
              "SourceFile": "private-video.mp4",
              "File:FileType": "MP4",
              "Samsung:SamsungModel": "SM-G736B"
            }]
            """;
        SamsungVideoCameraMetadata camera =
            Assert.IsType<SamsungVideoCameraMetadata>(
                SamsungVideoCameraMetadataParser.Parse(json));
        Assert.Equal("Samsung", camera.CameraMake);
        Assert.Equal("SM-G736B", camera.CameraModel);
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("[{\"QuickTime:Model\":\"SM-G736B\"}]")]
    [InlineData("[{\"Other:SamsungModel\":\"SM-G736B\"}]")]
    [InlineData("[{\"Samsung:SamsungModel\":\"X\\u000a\"}]")]
    [InlineData("[{\"Samsung:SamsungModel\":\"\"}]")]
    [InlineData("[{\"Samsung:SamsungModel\":123}]")]
    [InlineData("[{\"Samsung:SamsungModel\":\" \"}]")]
    public void Missing_non_samsung_or_invalid_model_is_not_guessed(string json)
    {
        Assert.Null(SamsungVideoCameraMetadataParser.Parse(json));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[{},{}]")]
    [InlineData("not-json")]
    public void Multiple_sources_or_malformed_json_are_rejected(string json)
    {
        Assert.ThrowsAny<Exception>(() => SamsungVideoCameraMetadataParser.Parse(json));
    }

    [Fact]
    public void Conflicting_model_tag_keys_do_not_choose_arbitrarily()
    {
        const string json = """
            [{
              "Samsung:SamsungModel": "SM-G736B",
              "samsung:samsungmodel": "SM-G736C"
            }]
            """;
        Assert.Null(SamsungVideoCameraMetadataParser.Parse(json));
    }

    [Fact]
    public void Parser_does_not_expose_source_file_in_cam_metadata()
    {
        const string json = """
            [{"SourceFile":"private\\sensitive\\sample.mp4",
              "Samsung:SamsungModel":"SM-G736B"}]
            """;
        var camera = Assert.IsType<SamsungVideoCameraMetadata>(
            SamsungVideoCameraMetadataParser.Parse(json));
        Assert.DoesNotContain("sample.mp4",
            System.Text.Json.JsonSerializer.Serialize(camera), StringComparison.Ordinal);
    }
}
