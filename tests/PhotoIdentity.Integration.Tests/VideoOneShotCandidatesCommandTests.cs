using System.Text.Json;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoOneShotCandidatesCommandTests
{
    private static string[] ValidArguments() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0188_LOCAL_ONLY",
        "--database", "photoidentity_catalogue"
    ];

    [Fact]
    public void Read_only_catalogue_shortlist_has_no_apply_source_or_worker_switch()
    {
        VideoOneShotCandidatesOptions basic =
            VideoOneShotCandidatesOptions.Parse(ValidArguments());
        Assert.Equal(12, basic.Limit);
        Assert.Equal(1, VideoOneShotCandidatesOptions.Parse(
            [.. ValidArguments(), "--limit", "1"]).Limit);
        Assert.Equal(20, VideoOneShotCandidatesOptions.Parse(
            [.. ValidArguments(), "--limit", "20"]).Limit);
        foreach (string[] extra in new[]
        {
            new[] { "--apply" },
            new[] { "--source-path", @"C:\\Archive\\Personal.mov" },
            new[] { "--revision-id", Guid.NewGuid().ToString() },
            new[] { "--video-worker-enabled" },
            new[] { "--limit", "0" },
            new[] { "--limit", "21" },
            new[] { "--limit", "not-a-number" },
            new[] { "--limit", "1", "--limit", "2" }
        })
        {
            Assert.Throws<ArgumentException>(() =>
                VideoOneShotCandidatesOptions.Parse([.. ValidArguments(), .. extra]));
        }
        Assert.Throws<ArgumentException>(() =>
            VideoOneShotCandidatesOptions.Parse([]));
        Assert.Throws<ArgumentException>(() =>
            VideoOneShotCandidatesOptions.Parse(
                ["--postgres-connection-env", "BAD;ENV",
                 "--database", "photoidentity_catalogue"]));
        Assert.Throws<ArgumentException>(() =>
            VideoOneShotCandidatesOptions.Parse(
                ["--postgres-connection-env", "GOOD",
                 "--database", "other-database!"]));
    }

    [Fact]
    public async Task Missing_connection_variable_refuses_without_opening_a_database()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string env = "PHOTOIDENTITY_WI0188_MISSING_CANDIDATES_TEST";
        string? previous = Environment.GetEnvironmentVariable(env);
        try
        {
            Environment.SetEnvironmentVariable(env, null);
            VideoOneShotCandidatesOptions options =
                VideoOneShotCandidatesOptions.Parse(
                [
                    "--postgres-connection-env", env,
                    "--database", "photoidentity_catalogue"
                ]);
            using StringWriter output = new();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoOneShotCandidatesCommandRunner.RunAsync(options, output));
            Assert.Equal(string.Empty, output.ToString());
        }
        finally { Environment.SetEnvironmentVariable(env, previous); }
    }

    [Fact]
    public void Result_contains_only_uuid_and_bounded_numeric_metadata()
    {
        Guid revision = Guid.Parse("b1000000-0000-4000-8000-000000000001");
        using StringWriter output = new();
        VideoOneShotCandidatesCommandRunner.Write(output,
        [
            new VideoOneShotCandidate(revision, 1234567, 3.5m, "coded-landscape", true)
        ], hasMore: false);
        string json = output.ToString();
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;
        Assert.Equal("wi0188-one-shot-candidates-v1",
            root.GetProperty("protocol").GetString());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        Assert.True(root.GetProperty("schemaUsed").GetBoolean());
        Assert.False(root.GetProperty("originalsOpened").GetBoolean());
        Assert.False(root.GetProperty("oneDriveHydrationRequested").GetBoolean());
        Assert.False(root.GetProperty("videoWorkersActivated").GetBoolean());
        Assert.False(root.GetProperty("jobsEnqueued").GetBoolean());
        Assert.False(root.GetProperty("localAvailabilityChecked").GetBoolean());
        Assert.False(root.GetProperty("hasMore").GetBoolean());
        JsonElement rows = root.GetProperty("candidates");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(revision, rows[0].GetProperty("revisionId").GetGuid());
        Assert.Equal(1234567, rows[0].GetProperty("sourceBytes").GetInt64());
        Assert.Equal(3.5m, rows[0].GetProperty("durationSeconds").GetDecimal());
        Assert.True(rows[0].GetProperty("hasAudio").GetBoolean());
        foreach (string forbidden in new[]
        {
            "sourcePath", "sourceKey", "fileName", "gps", "camera",
            "captureAtUtc", "person", "token", "connection"
        })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_shortlist_is_successful_read_only_result()
    {
        using StringWriter output = new();
        VideoOneShotCandidatesCommandRunner.Write(output, [], false);
        using JsonDocument parsed = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, parsed.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.False(parsed.RootElement.GetProperty("hasMore").GetBoolean());
    }
}
