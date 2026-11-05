using System.Text.Json;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// WI-0207 first checkpoint. Uses only synthetic states, aggregate text
/// and source contracts; never touches private OneDrive originals, runs
/// FFmpeg or asks the maintained host to change its pinning state.
/// </summary>
public sealed class VideoOnlineOnlyPreflightTests
{
    [Theory]
    [InlineData(AssetAvailability.OnlineOnly, false, "online-only")]
    [InlineData(AssetAvailability.OnlineOnly, true, "pinned-downloading")]
    [InlineData(AssetAvailability.Downloading, false, "downloading")]
    [InlineData(AssetAvailability.Downloading, true, "pinned-downloading")]
    [InlineData(AssetAvailability.Local, false, "local")]
    [InlineData(AssetAvailability.Local, true, "pinned-local")]
    [InlineData(AssetAvailability.Unavailable, false, "unavailable")]
    [InlineData(AssetAvailability.Error, false, "unknown-or-error")]
    public void Live_attribute_categories_do_not_disclose_platform_errors(
        AssetAvailability state, bool pinned, string expected)
    {
        var snapshot = new OneDriveFilesOnDemandState(
            state, pinned, false,
            @"Private C:\Users\Somebody\OneDrive\2023\Video.mov");
        Assert.Equal(expected,
            VideoOnlineOnlyPreflightCommandRunner.Classify(snapshot));
    }

    [Fact]
    public void Read_only_report_sanitizes_untrusted_statuses_and_returns_numeric_counts()
    {
        using StringWriter output = new();
        VideoOnlineOnlyPreflightCommandRunner.WriteReport(output,
        [
            new("online-only", false, true, false),
            new("pinned-local", true, false, true),
            new(@"C:\Private\Secret\Video.mov", false, false, false)
        ]);
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        JsonElement root = report.RootElement;
        Assert.Equal("wi0207-online-only-preflight-v1",
            root.GetProperty("protocol").GetString());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.False(root.GetProperty("originalContentOpened").GetBoolean());
        Assert.False(root.GetProperty("oneDriveHydrationRequested").GetBoolean());
        Assert.False(root.GetProperty("oneDriveReleaseRequested").GetBoolean());
        Assert.False(root.GetProperty("videoJobsEnqueued").GetBoolean());
        Assert.False(root.GetProperty("ffmpegInvoked").GetBoolean());
        Assert.Equal(3, root.GetProperty(
            "catalogueCurrentSupportedWithoutPublishedProxy").GetInt32());
        Assert.Equal(2, root.GetProperty(
            "standardOrCompactProfileCandidates").GetInt32());
        Assert.Equal(1, root.GetProperty(
            "fullRangeProfileCandidates").GetInt32());
        Assert.Equal(1, root.GetProperty("queuedOrRunningDependents").GetInt32());
        Assert.Equal(1, root.GetProperty(
            "activePhotoManagedHydrationRecords").GetInt32());
        Assert.Equal(3, root.GetProperty("availability").GetArrayLength());

        Assert.DoesNotContain("Private", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Video.mov", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("C:", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_refuses_a_partial_unbounded_audit()
    {
        VideoOnlineOnlyPreflightObservation[] rows =
            Enumerable.Repeat(
                new VideoOnlineOnlyPreflightObservation(
                    "online-only", false, false, false),
                VideoOnlineOnlyPreflightCommandRunner.MaximumCandidates + 1)
                .ToArray();
        using StringWriter output = new();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoOnlineOnlyPreflightCommandRunner.WriteReport(output, rows));
        Assert.Equal("", output.ToString());
    }

    [Theory]
    [InlineData("--apply")]
    [InlineData("--read-local-bytes")]
    [InlineData("--show-revision-ids")]
    [InlineData("--source-root")]
    [InlineData("--enable-video")]
    public void Option_parser_refuses_media_actions_or_private_root_parameters(string flag)
    {
        Assert.Throws<ArgumentException>(() =>
            VideoCompatibilitySurveyOptions.Parse(
            [
                "--postgres-connection-env", "PHOTOIDENTITY_TEST_POSTGRES",
                "--database", "photoidentity_main",
                flag
            ]));
    }

    [Fact]
    public void Production_preflight_reads_only_catalogue_and_attributes()
    {
        string root = RepositoryRoot();
        string cli = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Cli", "VideoOnlineOnlyPreflightCommand.cs"));
        string program = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Cli", "Program.cs"));

        Assert.Contains("video", program);
        Assert.Contains("args[1] == \"onedrive\" && args[2] == \"preflight\"", program);
        Assert.Contains("SET TRANSACTION READ ONLY;", cli);
        Assert.Contains("IsolationLevel.RepeatableRead", cli);
        Assert.Contains("MaximumCandidates + 1", cli);
        Assert.Contains("asset.deleted_at_utc IS NULL", cli);
        Assert.Contains("source_copy_exclusions", cli);
        Assert.Contains("asset_revision_managed_hydrations", cli);
        Assert.Contains("video_processing_jobs", cli);
        Assert.Contains("video_published_derivatives", cli);
        Assert.Contains("GetState(file)", cli);
        Assert.DoesNotContain("RequestHydrationAsync(", cli);
        Assert.DoesNotContain("RequestOnlineOnlyAsync(", cli);
        Assert.DoesNotContain("OpenContentAsync(", cli);
        Assert.DoesNotContain("FileStream(", cli);
        Assert.DoesNotContain("InitializeAsync(", cli);
    }

    private static string RepositoryRoot()
    {
        foreach (string location in new[]
            { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? folder = new(Path.GetFullPath(location));
            for (int depth = 0; folder is not null && depth < 12;
                 depth++, folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "PhotoIdentity.slnx")))
                    return folder.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            "Photo Identity repository root not found.");
    }
}
