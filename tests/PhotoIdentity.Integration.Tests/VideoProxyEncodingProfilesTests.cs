using System.Text;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProxyEncodingProfilesTests
{
    [Fact]
    public void Existing_default_profile_and_arguments_remain_byte_for_byte_unchanged()
    {
        string source = Path.GetFullPath("already-staged.mov");
        string workspace = Path.GetFullPath("private-video-workspace");
        VideoProxyEncodingProfile standard =
            VideoProxyEncodingProfiles.Resolve(VideoProxyEncodingProfiles.StandardKey);
        Assert.Equal(VideoProxyCandidateProfile.Version, standard.Version);
        Assert.Equal(720, standard.MaximumDimension);
        Assert.Equal(VideoProxyCandidateProfile.BuildArguments(source, workspace).ToArray(),
            standard.BuildArguments(source, workspace).ToArray());
    }

    [Fact]
    public void Compact_profile_only_changes_approved_bounded_encoding_values()
    {
        string source = Path.GetFullPath("already-staged.mp4");
        string workspace = Path.GetFullPath("private-video-workspace");
        VideoProxyEncodingProfile compact =
            VideoProxyEncodingProfiles.Resolve(VideoProxyEncodingProfiles.CompactKey);
        Assert.Equal("wi0189-sdr-h264-aac-540p-compact-v1", compact.Version);
        Assert.Equal(540, compact.MaximumDimension);
        string[] args = [.. compact.BuildArguments(source, workspace)];
        string Arg(string option) => args[Array.IndexOf(args, option) + 1];

        Assert.Contains("scale=w=540:h=540", Arg("-vf"));
        Assert.Contains("force_original_aspect_ratio=decrease", Arg("-vf"));
        Assert.Equal("26", Arg("-crf"));
        Assert.Equal("1400k", Arg("-maxrate"));
        Assert.Equal("2800k", Arg("-bufsize"));
        Assert.Equal("80k", Arg("-b:a"));
        Assert.Equal("libx264", Arg("-c:v"));
        Assert.Equal("aac", Arg("-c:a"));
        Assert.Equal("file", Arg("-protocol_whitelist"));
        Assert.Equal("+faststart", Arg("-movflags"));
        Assert.Equal("colorprim=bt709:transfer=bt709:colormatrix=bt709",
            Arg("-x264-params"));
        Assert.Equal(Path.Combine(workspace, "output.mp4"), args[^1]);

        string[] baseline = [.. VideoProxyEncodingProfiles.Standard.BuildArguments(
            source, workspace)];
        Assert.Equal(baseline.Length, args.Length);
        string[] permitted = ["-vf", "-crf", "-maxrate", "-bufsize", "-b:a"];
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0 && permitted.Contains(args[i - 1])) continue;
            Assert.Equal(baseline[i], args[i]);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("540p")]
    [InlineData("STANDARD-720P")]
    [InlineData("-vf scale=9999")]
    [InlineData("compact-540p;http://test")]
    public void No_freeform_or_unknown_profile_is_accepted(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            VideoProxyEncodingProfiles.Resolve(name));
    }

    [Fact]
    public void Selected_version_is_part_of_durable_idempotent_job_identity()
    {
        var revision = AssetRevisionId.From(Guid.NewGuid());
        VideoDerivativeJobIdentity Identity(VideoProxyEncodingProfile profile) =>
            new(revision, VideoDerivativeKind.PlayableProxy, "wi0188-v1",
                profile.Version, new string('a', 64));
        string first = Identity(VideoProxyEncodingProfiles.Standard).IdempotencyKey();
        string again = Identity(VideoProxyEncodingProfiles.Standard).IdempotencyKey();
        string compact = Identity(VideoProxyEncodingProfiles.Compact).IdempotencyKey();
        Assert.Equal(first, again);
        Assert.NotEqual(first, compact);
        Assert.NotEqual(compact,
            new VideoDerivativeJobIdentity(AssetRevisionId.From(Guid.NewGuid()),
                VideoDerivativeKind.PlayableProxy, "wi0188-v1",
                VideoProxyEncodingProfiles.Compact.Version, new string('a', 64))
                .IdempotencyKey());
    }

    [Fact]
    public void Compact_output_validation_rejects_720p_and_accepts_540p()
    {
        byte[] json720 = Probe(width: 720, height: 404);
        byte[] json540 = Probe(width: 540, height: 304);
        Assert.Throws<InvalidDataException>(() =>
            VideoProxyCandidateProfile.ValidateOutputJson(json720, 4096, 540));
        VideoProxyInspection compact =
            VideoProxyCandidateProfile.ValidateOutputJson(json540, 4096, 540);
        Assert.Equal(540, compact.Width);
        Assert.Equal(304, compact.Height);
        Assert.True(compact.HasAudio);
        Assert.Equal(720,
            VideoProxyCandidateProfile.ValidateOutputJson(json720, 4096, 720).Width);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoProxyCandidateProfile.ValidateOutputJson(json540, 4096, 1080));
    }

    private static byte[] Probe(int width, int height) => Encoding.UTF8.GetBytes(
        $$"""
        {"format":{"format_name":"mov,mp4,m4a,3gp,3g2,mj2","duration":"10.5","size":"4096"},
         "streams":[{"codec_type":"video","codec_name":"h264","pix_fmt":"yuv420p",
           "width":{{width}},"height":{{height}},"r_frame_rate":"30/1",
           "color_transfer":"bt709","color_primaries":"bt709","color_space":"bt709"},
           {"codec_type":"audio","codec_name":"aac","channels":2}]}
        """);
}
