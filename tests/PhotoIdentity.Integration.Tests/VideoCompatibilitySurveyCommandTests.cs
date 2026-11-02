using System.Text.Json;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoCompatibilitySurveyCommandTests
{
    private static string[] Args() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_LOCAL_POSTGRES",
        "--database", "photoidentity_catalogue"
    ];

    private static VideoCompatibilityObservation Row(
        bool excluded = false, string? transfer = "bt709",
        long bytes = 10 * 1024 * 1024, decimal? duration = 12,
        string? availability = "local", bool poster = false,
        bool proxy = false, bool frame = false) =>
        new(excluded, "local-folder", availability, "supported", "mov,mp4,m4a,3gp,3g2,mj2",
            "h264", "yuv420p", transfer, "aac", 1920, 1080, 0,
            duration, bytes, poster, proxy, frame);

    [Fact]
    public void Survey_rejects_any_write_media_or_hydration_switch()
    {
        var options = VideoCompatibilitySurveyOptions.Parse(Args());
        Assert.Equal("PHOTOIDENTITY_LOCAL_POSTGRES", options.ConnectionEnv);
        Assert.Equal("photoidentity_catalogue", options.Database);
        foreach (string[] invalid in new[]
        {
            new[] { "--apply" }, new[] { "--include-paths" },
            new[] { "--ffprobe", "ffprobe.exe" },
            new[] { "--source", @"C:\Archive" },
            new[] { "--revision-id", Guid.NewGuid().ToString() },
            new[] { "--database", "other" },
            new[] { "--postgres-connection-env", "OTHER" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoCompatibilitySurveyOptions.Parse([.. Args(), .. invalid]));
        Assert.Throws<ArgumentException>(() =>
            VideoCompatibilitySurveyOptions.Parse([]));
        Assert.Throws<ArgumentException>(() =>
            VideoCompatibilitySurveyOptions.Parse(
                ["--postgres-connection-env", "BAD;NAME", "--database", "valid"]));
    }

    [Fact]
    public void Worker_gate_identifies_exact_current_metadata_boundaries()
    {
        Assert.Equal("metadata-eligible", VideoCompatibilitySurvey.WorkerGate(Row()));
        Assert.Equal("transfer", VideoCompatibilitySurvey.WorkerGate(Row(transfer: "arib-std-b67")));
        Assert.Equal("transfer", VideoCompatibilitySurvey.WorkerGate(Row(transfer: null)));
        Assert.Equal("source-size", VideoCompatibilitySurvey.WorkerGate(
            Row(bytes: 256L * 1024 * 1024 + 1)));
        Assert.Equal("duration", VideoCompatibilitySurvey.WorkerGate(Row(duration: 601)));
        Assert.Equal("metadata-eligible", VideoCompatibilitySurvey.WorkerGate(
            Row(availability: "online-only"))); // eligibility != live file presence
    }

    [Fact]
    public void Output_is_aggregated_stage_specific_and_excludes_source_identifiers()
    {
        VideoCompatibilitySurvey survey = new();
        survey.Add(Row(poster: true, proxy: true, frame: true));
        survey.Add(Row(transfer: "arib-std-b67", bytes: 400L * 1024 * 1024,
            availability: "online-only"));
        survey.Add(Row(excluded: true, transfer: "smpte2084"));
        using StringWriter output = new();
        survey.Write(output);
        string json = output.ToString();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("wi0210-compatibility-inventory-v1", root.GetProperty("protocol").GetString());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        Assert.False(root.GetProperty("originalBytesOpened").GetBoolean());
        Assert.False(root.GetProperty("filesystemAvailabilityChecked").GetBoolean());
        Assert.False(root.GetProperty("oneDriveHydrationRequested").GetBoolean());
        Assert.False(root.GetProperty("jobsEnqueued").GetBoolean());
        Assert.Equal(2, root.GetProperty("currentNonExcludedVideoCount").GetInt64());
        Assert.Equal(1, root.GetProperty("excludedCurrentVideoCount").GetInt64());
        Assert.Contains(root.GetProperty("dimensions").EnumerateArray(),
            item => item.GetProperty("dimension").GetString() == "last-recorded-availability" &&
                item.GetProperty("bucket").GetString() == "online-only" &&
                item.GetProperty("count").GetInt64() == 1);
        Assert.Contains(root.GetProperty("stages").EnumerateArray(),
            item => item.GetProperty("stage").GetString() == "playable-proxy-receipt" &&
                item.GetProperty("state").GetString() == "registered" &&
                item.GetProperty("count").GetInt64() == 1);
        Assert.Contains(root.GetProperty("overlappingCandidateGaps").EnumerateArray(),
            item => item.GetProperty("reason").GetString() == "source-over-256mib" &&
                item.GetProperty("affected").GetInt64() == 1);
        Assert.Contains(root.GetProperty("overlappingCandidateGaps").EnumerateArray(),
            item => item.GetProperty("reason").GetString() == "non-bt709-or-unknown-transfer" &&
                item.GetProperty("affected").GetInt64() == 1);
        foreach (string forbidden in new[]
        {
            "sourceKey", "rootLocator", "fileName", "revisionId", "assetId",
            "captureAtUtc", "gpsLatitude", "gpsLongitude", "cameraMake",
            "cameraModel", "caption", "embedding", "person", "connectionString"
        })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_archive_produces_zero_counts_without_division_or_probe()
    {
        using StringWriter output = new();
        new VideoCompatibilitySurvey().Write(output);
        using JsonDocument parsed = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, parsed.RootElement.GetProperty("currentNonExcludedVideoCount").GetInt64());
        Assert.Empty(parsed.RootElement.GetProperty("overlappingCandidateGaps").EnumerateArray());
    }
}
