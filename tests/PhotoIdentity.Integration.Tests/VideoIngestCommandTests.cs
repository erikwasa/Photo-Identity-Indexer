using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoIngestCommandTests
{
    [Fact]
    public void Video_ingest_is_default_dry_run_and_requires_explicit_opt_in()
    {
        string[] args = ["--source", "C:\\Archive", "--ffprobe", "C:\\Tools\\ffprobe.exe"];
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(args));
        VideoIngestOptions options = VideoIngestOptions.Parse(["--enable-video", .. args]);
        Assert.False(options.Apply);
        Assert.Equal(8, options.MaximumVideos);
        Assert.Equal(20, options.TimeoutSeconds);
        Assert.Null(options.ConnectionEnvironmentVariable);
        Assert.Null(options.DatabaseName);
    }

    [Fact]
    public void Write_mode_requires_named_environment_and_bounded_arguments()
    {
        string[] args = ["--enable-video", "--source", "C:\\Archive",
            "--ffprobe", "C:\\Tools\\ffprobe.exe"];
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse([.. args, "--apply"]));
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(
            [.. args, "--postgres-connection-env", "PRIVATE_POSTGRES", "--apply"]));
        VideoIngestOptions valid = VideoIngestOptions.Parse(
            [.. args, "--postgres-connection-env", "PRIVATE_POSTGRES",
                "--database", "photoidentity", "--apply",
                "--max-videos", "3", "--timeout-seconds", "40"]);
        Assert.True(valid.Apply);
        Assert.Equal("PRIVATE_POSTGRES", valid.ConnectionEnvironmentVariable);
        Assert.Equal("photoidentity", valid.DatabaseName);
        Assert.Equal(3, valid.MaximumVideos);
        Assert.Equal(40, valid.TimeoutSeconds);
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(
            [.. args, "--postgres-connection-env", "Host=localhost;Password=secret",
                "--database", "photoidentity", "--apply"]));
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(
            [.. args, "--postgres-connection-env", "PRIVATE_POSTGRES",
                "--database", "photoidentity;DROP DATABASE", "--apply"]));
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(
            [.. args, "--max-videos", "26"]));
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(
            [.. args, "--timeout-seconds", "121"]));
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(
            [.. args, "--apply", "--apply", "--postgres-connection-env", "PRIVATE_POSTGRES"]));
        Assert.Throws<ArgumentException>(() => VideoIngestOptions.Parse(
            [.. args, "--unknown", "on"]));
    }

    [Fact]
    public void Metadata_projection_distinguishes_supported_unknown_and_unsupported_without_inventing_capture()
    {
        var revision = AssetRevisionId.New();
        DateTimeOffset when = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        VideoPilotMetadata hlg = new(
            "mov,mp4", "hevc", "Main 10", "yuv420p10le", "arib-std-b67",
            "aac", 1920, 1080, -90, 42.5, "60000/1001", true, true);
        var supported = VideoIngestCommandRunner.BuildInspection(revision, hlg, hash, when);
        Assert.Equal("supported", supported.InspectionStatus);
        Assert.Null(supported.UnsupportedReasonCode);
        Assert.Equal("hevc", supported.VideoCodec);
        Assert.Equal(-90, supported.DisplayRotationDegrees);
        Assert.Equal(42.5m, supported.DurationSeconds);
        Assert.Equal("60000/1001", supported.AverageFrameRate);
        Assert.Equal("arib-std-b67", supported.ColorTransfer);
        Assert.Null(supported.CaptureAtUtc);
        Assert.Null(supported.GpsLatitude);
        Assert.Null(supported.CaptureTimeProvenance);
        Assert.Equal(hash, supported.ToolSha256);

        VideoPilotMetadata unknown = hlg with {
            VideoCodec = "mpeg4", Profile = "Simple",
            AverageFrameRate = "0/0", DurationSeconds = null,
            HasCreationTime = false, HasLocation = false
        };
        var unsupported = VideoIngestCommandRunner.BuildInspection(revision, unknown, hash, when);
        Assert.Equal("unsupported", unsupported.InspectionStatus);
        Assert.Equal("unsupported_codec_or_profile", unsupported.UnsupportedReasonCode);
        Assert.Null(unsupported.AverageFrameRate);
        Assert.Null(unsupported.DurationSeconds);
    }

    [Fact]
    public void Verified_tag_metadata_is_projected_with_provenance_into_video_revision_evidence()
    {
        const string json = """
            {"streams":[{"codec_type":"video","codec_name":"h264","profile":"High",
              "pix_fmt":"yuv420p","color_transfer":"bt709",
              "width":1920,"height":1080,"avg_frame_rate":"30/1"}],
             "format":{"format_name":"mov,mp4","duration":"22.125",
               "tags":{"com.apple.quicktime.creationdate":"2026-02-16T14:12:16+01:00",
                       "com.apple.quicktime.location.ISO6709":"+59.3123+018.0223/",
                       "com.apple.quicktime.make":"Apple",
                       "com.apple.quicktime.model":"iPhone"}}}
            """;
        var media = VideoPilotMetadataParser.Parse(json);
        var revision = AssetRevisionId.New();
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var evidence = VideoIngestCommandRunner.BuildInspection(
            revision, media, hash, DateTimeOffset.UtcNow);

        Assert.Equal("supported", evidence.InspectionStatus);
        Assert.Equal(new DateTimeOffset(2026, 2, 16, 13, 12, 16, TimeSpan.Zero),
            evidence.CaptureAtUtc);
        Assert.Equal("format:com.apple.quicktime.creationdate",
            evidence.CaptureTimeProvenance);
        Assert.Equal(59.3123, evidence.GpsLatitude);
        Assert.Equal(18.0223, evidence.GpsLongitude);
        Assert.Equal("Apple", evidence.CameraMake);
        Assert.Equal("iPhone", evidence.CameraModel);
        Assert.Equal("wi0151-ffprobe-metadata-v2", evidence.InspectionProfileId);
    }

    [Fact]
    public void File_observation_must_match_both_size_and_timestamp()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "photoidentity-wi0151-verify-" + Guid.NewGuid().ToString("N") + ".mov");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
            FileInfo file = new(path);
            DateTimeOffset modified = new(file.LastWriteTimeUtc, TimeSpan.Zero);
            SourceAsset asset = new(
                new SourceAssetReference(SourceId.New(), "sample.mov"), "sample.mov",
                "video/quicktime", 5, modified, AssetAvailability.Local);
            Assert.True(VideoIngestCommandRunner.MatchesObservation(asset, file));
            Assert.False(VideoIngestCommandRunner.MatchesObservation(
                new SourceAsset(asset.Reference, asset.RelativePath, asset.MediaType,
                    6, modified, asset.Availability), file));
            File.AppendAllText(path, "new data");
            Assert.False(VideoIngestCommandRunner.MatchesObservation(asset, file));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
