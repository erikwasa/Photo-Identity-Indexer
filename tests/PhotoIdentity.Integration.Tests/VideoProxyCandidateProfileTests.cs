using System.Buffers.Binary;
using System.Text;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProxyCandidateProfileTests
{
    [Fact]
    public void Sdr_portrait_and_supported_camera_audio_are_pilot_eligible()
    {
        VideoRevisionInspection portrait = Input() with
        {
            CodedWidth = 1080,
            CodedHeight = 1920,
            DisplayRotationDegrees = -90,
            AudioCodec = "pcm_s16le"
        };
        Assert.True(VideoProxyCandidateProfile.Eligible(portrait));
        Assert.True(VideoProxyCandidateProfile.Eligible(
            portrait with { AudioCodec = null, VideoCodec = "hevc", PixelFormat = "yuv420p10le" }));
    }

    [Theory]
    [InlineData("arib-std-b67")]
    [InlineData("smpte2084")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void Hdr_or_unverified_transfer_is_not_silently_treated_as_sdr(string? transfer) =>
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { ColorTransfer = transfer }));

    [Fact]
    public void Unrecognized_codec_duration_rotation_and_audio_are_not_admitted()
    {
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { VideoCodec = "vp9" }));
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { CodedWidth = 8192 }));
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { DurationSeconds = 650m }));
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { DisplayRotationDegrees = null }));
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { DisplayRotationDegrees = 47 }));
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { AudioCodec = "opus" }));
        Assert.False(VideoProxyCandidateProfile.Eligible(Input() with { InspectionStatus = "unsupported" }));
        Assert.False(VideoProxyCandidateProfile.Eligible(null));
    }

    [Fact]
    public void Argument_array_forces_local_software_sdr_orientation_and_fast_start()
    {
        string input = Path.GetFullPath("already-staged.mov");
        string scratch = Path.GetFullPath("private-tool-root");
        var args = VideoProxyCandidateProfile.BuildArguments(input, scratch).ToArray();

        Assert.Contains("-protocol_whitelist", args);
        Assert.Equal("file", args[Array.IndexOf(args, "-protocol_whitelist") + 1]);
        Assert.Contains("-nostdin", args);
        Assert.Contains("-threads", args);
        Assert.Equal("1", args[Array.IndexOf(args, "-threads") + 1]);
        Assert.Equal("libx264", args[Array.IndexOf(args, "-c:v") + 1]);
        Assert.Equal("aac", args[Array.IndexOf(args, "-c:a") + 1]);
        Assert.Contains("-sn", args);
        Assert.Contains("-dn", args);
        Assert.Contains("-map_metadata", args);
        Assert.Equal("-1", args[Array.IndexOf(args, "-map_metadata") + 1]);
        Assert.Equal("+faststart", args[Array.IndexOf(args, "-movflags") + 1]);
        Assert.Equal("mp4", args[Array.IndexOf(args, "-f") + 1]);
        Assert.Contains("force_original_aspect_ratio=decrease",
            args[Array.IndexOf(args, "-vf") + 1]);
        Assert.Contains("fps=30", args[Array.IndexOf(args, "-vf") + 1]);
        Assert.Equal(Path.Combine(scratch, "output.mp4"), args[^1]);
        Assert.DoesNotContain(args, arg =>
            arg.Contains("nvenc", StringComparison.OrdinalIgnoreCase) ||
            arg.Contains("cuda", StringComparison.OrdinalIgnoreCase) ||
            arg.Contains("http:", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<ArgumentException>(() =>
            VideoProxyCandidateProfile.BuildArguments("relative.mov", scratch));
    }

    [Fact]
    public void Strict_probe_accepts_exact_sdr_video_with_optional_aac()
    {
        var bytes = Probe();
        VideoProxyInspection result =
            VideoProxyCandidateProfile.ValidateOutputJson(bytes, 4096);
        Assert.Equal(720, result.Width);
        Assert.Equal(404, result.Height);
        Assert.True(result.HasAudio);
        Assert.Equal(10.5, result.DurationSeconds);

        byte[] silent = Probe(
            """
            [{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p",
              "width":720,"height":404,"r_frame_rate":"30000/1001",
              "color_transfer":"bt709","color_primaries":"bt709","color_space":"bt709"}]
            """);
        Assert.False(VideoProxyCandidateProfile.ValidateOutputJson(silent, 4096).HasAudio);
    }

    [Fact]
    public void Probe_rejects_unsupported_codec_color_tracks_and_dimensions()
    {
        string json = Encoding.UTF8.GetString(Probe());
        foreach (string invalid in new[]
        {
            json.Replace("h264", "hevc"),
            json.Replace("yuv420p", "yuv420p10le"),
            json.Replace("bt709", "arib-std-b67"),
            json.Replace("\"width\":720", "\"width\":1920"),
            json.Replace("\"height\":404", "\"height\":405"),
            json.Replace("30/1", "120/1"),
            json.Replace("\"codec_name\":\"aac\"", "\"codec_name\":\"opus\""),
            json.Replace("\"channels\":2", "\"channels\":6"),
            json.Replace("\"duration\":\"10.5\"", "\"duration\":\"900\""),
            json.Replace("\"size\":\"4096\"", "\"size\":\"5000\""),
            json.Replace("mov,mp4,m4a,3gp,3g2,mj2", "matroska,webm"),
            json.Replace("\"codec_type\":\"audio\"", "\"codec_type\":\"subtitle\""),
            json.Replace("\"channels\":2", "\"channels\":2,\"side_data_list\":[{\"rotation\":90}]")
                .Replace("\"codec_type\":\"video\"", "\"codec_type\":\"video\"")
        })
        {
            Assert.ThrowsAny<Exception>(() =>
                VideoProxyCandidateProfile.ValidateOutputJson(
                    Encoding.UTF8.GetBytes(invalid), 4096));
        }
    }

    [Fact]
    public void Duplicate_video_streams_and_retained_display_rotation_are_rejected()
    {
        string json = Encoding.UTF8.GetString(Probe());
        string video = """
            {"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p",
             "width":720,"height":404,"r_frame_rate":"30/1",
             "color_transfer":"bt709","color_primaries":"bt709","color_space":"bt709"}
            """;
        Assert.Throws<InvalidDataException>(() =>
            VideoProxyCandidateProfile.ValidateOutputJson(
                Encoding.UTF8.GetBytes(json.Replace("\"streams\":[", "\"streams\":[" + video + ",")),
                4096));
        string rotated = json.Replace("\"width\":720",
            "\"side_data_list\":[{\"rotation\":-90}],\"width\":720");
        Assert.Throws<InvalidDataException>(() =>
            VideoProxyCandidateProfile.ValidateOutputJson(
                Encoding.UTF8.GetBytes(rotated), 4096));
        Assert.ThrowsAny<Exception>(() =>
            VideoProxyCandidateProfile.ValidateOutputJson(
                Encoding.UTF8.GetBytes("{\"streams\":[]}"), 4096));
    }

    [Fact]
    public void Fast_start_requires_ftyp_moov_before_mdat_and_complete_box_boundaries()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-proxy-box-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            File.WriteAllBytes(path, Boxes("ftyp", "free", "moov", "mdat"));
            VideoProxyCandidateProfile.ValidateFastStart(path);
            File.WriteAllBytes(path, Boxes("ftyp", "mdat", "moov"));
            Assert.Throws<InvalidDataException>(() =>
                VideoProxyCandidateProfile.ValidateFastStart(path));
            File.WriteAllBytes(path, Boxes("free", "ftyp", "moov", "mdat"));
            Assert.Throws<InvalidDataException>(() =>
                VideoProxyCandidateProfile.ValidateFastStart(path));
            byte[] corrupt = Boxes("ftyp", "moov", "mdat");
            corrupt[0] = 0xff;
            File.WriteAllBytes(path, corrupt);
            Assert.Throws<InvalidDataException>(() =>
                VideoProxyCandidateProfile.ValidateFastStart(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static byte[] Boxes(params string[] types)
    {
        using MemoryStream file = new();
        foreach (string type in types)
        {
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(header[..4], 48);
            Encoding.ASCII.GetBytes(type, header[4..]);
            file.Write(header);
            file.Write(new byte[40]);
        }
        return file.ToArray();
    }

    private static byte[] Probe(string? streams = null) => Encoding.UTF8.GetBytes(
        """
        {"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"10.5","size":"4096"},
         "streams":
        """ +
        (streams ?? """
        [{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p",
          "width":720,"height":404,"r_frame_rate":"30/1",
          "color_transfer":"bt709","color_primaries":"bt709","color_space":"bt709"},
         {"codec_type":"audio","codec_name":"aac","channels":2}]
        """) + "}");

    private static VideoRevisionInspection Input() => new(
        AssetRevisionId.From(Guid.NewGuid()),
        "supported", null, "mov,mp4", "h264", "High", "aac", "yuv420p", "bt709",
        1920, 1080, 0, 10.5m, "30/1",
        null, null, null, null, null, null,
        DateTimeOffset.UtcNow, new string('a', 64), "wi0151-inspection-v2");
}
