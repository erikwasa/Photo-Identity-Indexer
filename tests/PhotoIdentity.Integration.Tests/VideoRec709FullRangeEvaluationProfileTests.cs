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
