using System.Text;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>Metadata/argument-only regression. No original, FFmpeg or DB access.</summary>
public sealed class VideoRec709FullRangeEvaluationProfileTests
{
    private const long SourceSize = 4096;

    [Fact]
    public void Explicit_full_range_bt709_and_existing_revision_evidence_are_required()
    {
        VideoRevisionInspection current = Current();
        VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
            Encoding.UTF8.GetBytes(InputProbe()), current, SourceSize);

        // The normal production worker and both registered presets stay closed.
        Assert.False(VideoProxyCandidateProfile.Eligible(current));
        Assert.Throws<ArgumentException>(() =>
            VideoProxyEncodingProfiles.Resolve("full-range-720p"));
    }

    [Fact]
    public void Unknown_colour_signalling_rotation_and_changed_revision_fail_closed()
    {
        string valid = InputProbe();
        foreach (string changed in new[]
        {
            valid.Replace("\"color_range\":\"pc\"", "\"color_range\":\"tv\""),
            valid.Replace("\"color_range\":\"pc\",", ""),
            valid.Replace("\"color_transfer\":\"bt709\"", "\"color_transfer\":\"arib-std-b67\""),
            valid.Replace("\"color_primaries\":\"bt709\"", "\"color_primaries\":\"bt2020\""),
            valid.Replace("\"color_space\":\"bt709\"", "\"color_space\":\"bt2020nc\""),
            valid.Replace("\"pix_fmt\":\"yuvj420p\"", "\"pix_fmt\":\"yuv420p\""),
            valid.Replace("\"codec_name\":\"h264\"", "\"codec_name\":\"vp9\""),
            valid.Replace("\"width\":1920", "\"width\":1280"),
            valid.Replace("\"rotation\":-90", "\"rotation\":90"),
            valid.Replace("\"rotation\":-90", "\"not_rotation\":-90"),
            valid.Replace("\"duration\":\"10.5\"", "\"duration\":\"900\""),
            valid.Replace("\"size\":\"4096\"", "\"size\":\"4097\""),
            valid.Replace("\"format_name\":\"mov,mp4,m4a,3gp,3g2,mj2\"",
                "\"format_name\":\"matroska,webm\""),
            valid.Replace("\"codec_name\":\"aac\"", "\"codec_name\":\"opus\""),
            valid.Replace("\"codec_type\":\"audio\"", "\"codec_type\":\"subtitle\""),
            valid.Replace("\"streams\":[", "\"streams\":[] , \"unused\":["),
        })
        {
            // Reject ineffective fixture mutations before testing the policy.
            // Otherwise a no-op Replace can make a valid fixture look like
            // an incorrect admission decision.
            Assert.NotEqual(valid, changed);
            Assert.ThrowsAny<Exception>(() =>
                VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                    Encoding.UTF8.GetBytes(changed), Current(), SourceSize));
        }

        foreach (VideoRevisionInspection mismatch in new[]
        {
            Current() with { ColorTransfer = "arib-std-b67" },
            Current() with { ColorTransfer = null },
            Current() with { CodedHeight = 720 },
            Current() with { DisplayRotationDegrees = null },
            Current() with { PixelFormat = "yuv420p" },
            Current() with { InspectionStatus = "unsupported" },
            Current() with { AudioCodec = null },
            Current() with { DurationSeconds = 300m }
        })
            Assert.ThrowsAny<Exception>(() =>
                VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                    Encoding.UTF8.GetBytes(valid), mismatch, SourceSize));

        Assert.Throws<InvalidDataException>(() =>
            VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                Encoding.UTF8.GetBytes(valid), Current(), 256L * 1024 * 1024 + 1));
        Assert.Throws<InvalidDataException>(() =>
            VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                Encoding.UTF8.GetBytes(valid), Current(), SourceSize + 1));
    }

    [Fact]
    public void Absent_rotation_defaults_to_zero_only_for_recorded_zero()
    {
        string rotated = InputProbe();
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(rotated);
        using MemoryStream buffer = new();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("format");
            document.RootElement.GetProperty("format").WriteTo(writer);
            writer.WritePropertyName("streams");
            writer.WriteStartArray();
            foreach (System.Text.Json.JsonElement stream in
                document.RootElement.GetProperty("streams").EnumerateArray())
            {
                writer.WriteStartObject();
                foreach (System.Text.Json.JsonProperty field in stream.EnumerateObject())
                {
                    if (field.Name == "side_data_list") continue;
                    field.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        byte[] noRotation = buffer.ToArray();
        VideoRevisionInspection zero =
            Current() with { DisplayRotationDegrees = 0 };
        VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
            noRotation, zero, SourceSize);
        Assert.Throws<InvalidDataException>(() =>
            VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                noRotation, Current(), SourceSize));
        Assert.Throws<InvalidDataException>(() =>
            VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                Encoding.UTF8.GetBytes(rotated), zero, SourceSize));
    }

    [Theory]
    [InlineData("\"color_range\":\"pc\"", "\"color_range\":\"tv\"",
        "source-full-range-unproven")]
    [InlineData("\"color_transfer\":\"bt709\"", "\"color_transfer\":\"arib-std-b67\"",
        "source-bt709-signalling-unproven")]
    [InlineData("\"color_primaries\":\"bt709\"", "\"color_primaries\":\"bt2020\"",
        "source-bt709-signalling-unproven")]
    [InlineData("\"color_space\":\"bt709\"", "\"color_space\":\"bt2020nc\"",
        "source-bt709-signalling-unproven")]
    [InlineData("\"pix_fmt\":\"yuvj420p\"", "\"pix_fmt\":\"yuv420p\"",
        "source-pixel-format-unproven")]
    [InlineData("\"codec_name\":\"h264\"", "\"codec_name\":\"vp9\"",
        "source-video-codec-mismatch")]
    [InlineData("\"width\":1920", "\"width\":1280",
        "source-geometry-mismatch")]
    [InlineData("\"rotation\":-90", "\"rotation\":90",
        "source-rotation-unproven")]
    [InlineData("\"codec_name\":\"aac\"", "\"codec_name\":\"opus\"",
        "source-audio-layout-mismatch")]
    [InlineData("\"codec_type\":\"audio\"", "\"codec_type\":\"subtitle\"",
        "source-stream-layout-unsupported")]
    [InlineData("\"size\":\"4096\"", "\"size\":\"4097\"",
        "source-size-or-duration-mismatch")]
    [InlineData("\"format_name\":\"mov,mp4,m4a,3gp,3g2,mj2\"",
        "\"format_name\":\"matroska,webm\"", "source-container-unapproved")]
    public void Independent_input_probe_rejections_have_specific_safe_reason_codes(
        string original, string changed, string expectedReason)
    {
        string valid = InputProbe();
        string rejected = valid.Replace(original, changed);
        Assert.NotEqual(valid, rejected);
        VideoFullRangeInputRejectionException failure =
            Assert.Throws<VideoFullRangeInputRejectionException>(() =>
                VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                    Encoding.UTF8.GetBytes(rejected), Current(), SourceSize));
        Assert.Equal(expectedReason, failure.ReasonCode);
        Assert.DoesNotContain("mov", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1920", failure.Message);
    }

    [Fact]
    public void Malformed_or_changed_registered_input_does_not_leak_probe_evidence()
    {
        VideoFullRangeInputRejectionException malformed =
            Assert.Throws<VideoFullRangeInputRejectionException>(() =>
                VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                    Encoding.UTF8.GetBytes("{not valid video metadata}"),
                    Current(), SourceSize));
        Assert.Equal("source-probe-data-invalid", malformed.ReasonCode);

        VideoFullRangeInputRejectionException ineligible =
            Assert.Throws<VideoFullRangeInputRejectionException>(() =>
                VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                    Encoding.UTF8.GetBytes(InputProbe()),
                    Current() with { PixelFormat = "yuv420p" }, SourceSize));
        Assert.Equal("source-registered-class-ineligible", ineligible.ReasonCode);

        Assert.Throws<ArgumentException>(() =>
            new VideoFullRangeInputRejectionException("private/source/path.mov"));
    }

    [Fact]
    public void Fixed_evaluation_recipe_converts_full_to_limited_and_retains_baseline_guardrails()
    {
        string source = Path.GetFullPath("approved-local-staged.mov");
        string scratch = Path.GetFullPath("bounded-private-workspace");
        string[] baseline = [.. VideoProxyCandidateProfile.BuildArguments(source, scratch)];
        string[] trial = [.. VideoRec709FullRangeEvaluationProfile.BuildArguments(source, scratch)];
        string Arg(string flag) => trial[Array.IndexOf(trial, flag) + 1];

        Assert.Equal(baseline.Length + 2, trial.Length);
        Assert.Contains("in_range=full", Arg("-vf"));
        Assert.Contains("out_range=limited", Arg("-vf"));
        Assert.Contains("in_color_matrix=bt709", Arg("-vf"));
        Assert.Contains("out_color_matrix=bt709", Arg("-vf"));
        Assert.Equal("tv", Arg("-color_range"));
        Assert.Equal("libx264", Arg("-c:v"));
        Assert.Equal("aac", Arg("-c:a"));
        Assert.Equal("file", Arg("-protocol_whitelist"));
        Assert.Equal("+faststart", Arg("-movflags"));
        Assert.Equal("-1", Arg("-map_metadata"));
        Assert.Equal("bt709", Arg("-color_primaries"));
        Assert.Equal("bt709", Arg("-color_trc"));
        Assert.Equal("bt709", Arg("-colorspace"));
        Assert.Equal(Path.Combine(scratch, "output.mp4"), trial[^1]);

        List<string> withoutRangeFlag = [.. trial];
        int range = withoutRangeFlag.IndexOf("-color_range");
        withoutRangeFlag.RemoveRange(range, 2);
        int filter = withoutRangeFlag.IndexOf("-vf");
        withoutRangeFlag[filter + 1] = baseline[filter + 1];
        Assert.Equal(baseline, withoutRangeFlag);
        Assert.Throws<ArgumentException>(() =>
            VideoRec709FullRangeEvaluationProfile.BuildArguments(
                "relative-original.mov", scratch));
    }

    [Fact]
    public void Independent_normalized_output_requires_explicit_limited_range()
    {
        byte[] valid = Encoding.UTF8.GetBytes(OutputProbe());
        VideoProxyInspection accepted =
            VideoRec709FullRangeEvaluationProfile.ValidateOutputProbeJson(valid, SourceSize);
        Assert.Equal(720, accepted.Width);
        Assert.Equal(404, accepted.Height);
        Assert.True(accepted.HasAudio);

        string json = OutputProbe();
        foreach (string corrupted in new[]
        {
            json.Replace("\"color_range\":\"tv\"", "\"color_range\":\"pc\""),
            json.Replace("\"color_range\":\"tv\",", ""),
            json.Replace("\"color_transfer\":\"bt709\"", "\"color_transfer\":\"arib-std-b67\""),
            json.Replace("\"pix_fmt\":\"yuv420p\"", "\"pix_fmt\":\"yuvj420p\"")
        })
        {
            Assert.NotEqual(json, corrupted);
            Assert.ThrowsAny<Exception>(() =>
                VideoRec709FullRangeEvaluationProfile.ValidateOutputProbeJson(
                    Encoding.UTF8.GetBytes(corrupted), SourceSize));
        }
    }

    [Fact]
    public void Synthetic_source_probe_requires_actual_full_range_and_known_test_geometry()
    {
        const long bytes = 4096;
        string valid = SyntheticInputProbe();
        VideoRec709FullRangeEvaluationProfile.ValidateSyntheticInputProbeJson(
            Encoding.UTF8.GetBytes(valid), 1280, 720, audio: true, bytes);

        foreach (string corrupted in new[]
        {
            valid.Replace("\"color_range\":\"pc\"", "\"color_range\":\"tv\""),
            valid.Replace("\"color_range\":\"pc\",", ""),
            valid.Replace("\"color_transfer\":\"bt709\"", "\"color_transfer\":\"arib-std-b67\""),
            valid.Replace("\"color_primaries\":\"bt709\"", "\"color_primaries\":\"bt2020\""),
            valid.Replace("\"color_space\":\"bt709\"", "\"color_space\":\"bt2020nc\""),
            valid.Replace("\"pix_fmt\":\"yuvj420p\"", "\"pix_fmt\":\"yuv420p\""),
            valid.Replace("\"codec_name\":\"h264\"", "\"codec_name\":\"vp9\""),
            valid.Replace("\"width\":1280", "\"width\":1920"),
            valid.Replace("\"height\":720", "\"height\":1080"),
            valid.Replace("\"size\":\"4096\"", "\"size\":\"4097\""),
            valid.Replace("\"duration\":\"3.0\"", "\"duration\":\"900\""),
            valid.Replace("\"format_name\":\"mov,mp4,m4a,3gp,3g2,mj2\"",
                "\"format_name\":\"matroska,webm\""),
            valid.Replace("\"codec_name\":\"aac\"", "\"codec_name\":\"opus\""),
            valid.Replace("\"channels\":1", "\"channels\":6")
        })
        {
            Assert.NotEqual(valid, corrupted);
            Assert.ThrowsAny<Exception>(() =>
                VideoRec709FullRangeEvaluationProfile.ValidateSyntheticInputProbeJson(
                    Encoding.UTF8.GetBytes(corrupted), 1280, 720, true, bytes));
        }

        Assert.Throws<InvalidDataException>(() =>
            VideoRec709FullRangeEvaluationProfile.ValidateSyntheticInputProbeJson(
                Encoding.UTF8.GetBytes(valid), 720, 1280, true, bytes));
        Assert.Throws<InvalidDataException>(() =>
            VideoRec709FullRangeEvaluationProfile.ValidateSyntheticInputProbeJson(
                Encoding.UTF8.GetBytes(valid), 1280, 720, false, bytes));
        Assert.Throws<InvalidDataException>(() =>
            VideoRec709FullRangeEvaluationProfile.ValidateSyntheticInputProbeJson(
                Encoding.UTF8.GetBytes(valid), 1280, 720, true, bytes + 1));


    }

    private static string SyntheticInputProbe() => """
        {"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"3.0","size":"4096"},
         "streams":[
           {"codec_type":"video","codec_name":"h264","pix_fmt":"yuvj420p",
            "color_range":"pc","color_transfer":"bt709","color_primaries":"bt709",
            "color_space":"bt709","width":1280,"height":720},
           {"codec_type":"audio","codec_name":"aac","channels":1}]}
        """;

    private static VideoRevisionInspection Current() => new(
        AssetRevisionId.From(Guid.NewGuid()),
        "supported", null, "mov,mp4", "h264", "High", "aac",
        "yuvj420p", "bt709", 1920, 1080, -90, 10.5m, "30/1",
        null, null, null, null, null, null,
        DateTimeOffset.UtcNow, new string('a', 64), "wi0151-inspection-v2");

    private static string InputProbe() => """
        {"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"10.5","size":"4096"},
         "streams":[
          {"codec_type":"video","codec_name":"h264","pix_fmt":"yuvj420p",
           "color_range":"pc","color_transfer":"bt709","color_primaries":"bt709",
           "color_space":"bt709","width":1920,"height":1080,
           "side_data_list":[{"rotation":-90}]},
          {"codec_type":"audio","codec_name":"aac"}]}
        """;

    private static string OutputProbe() => """
        {"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"10.5","size":"4096"},
         "streams":[
          {"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p",
           "color_range":"tv","color_transfer":"bt709","color_primaries":"bt709",
           "color_space":"bt709","width":720,"height":404,"r_frame_rate":"30/1"},
          {"codec_type":"audio","codec_name":"aac","channels":2}]}
        """;
}
