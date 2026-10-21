using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoOneShotSourceProofCommandTests
{
    private static string[] Arguments() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0188_TEST",
        "--database", "photoidentity_catalogue",
        "--revision-id", "12345678-1234-4234-8234-123456789abc"
    ];

    [Fact]
    public void Content_read_requires_a_distinct_explicit_flag_and_rejects_execution_flags()
    {
        Assert.False(VideoOneShotSourceProofOptions.Parse(Arguments()).ReadLocalBytes);
        Assert.True(VideoOneShotSourceProofOptions.Parse(
            [.. Arguments(), "--read-local-bytes"]).ReadLocalBytes);
        foreach (string[] forbidden in new[]
        {
            new[] { "--apply" },
            new[] { "--run" },
            new[] { "--read-local-bytes", "--read-local-bytes" },
            new[] { "--job-id", Guid.NewGuid().ToString() },
            new[] { "--source-path", @"C:\\Archive\\private.mov" },
            new[] { "--revision-id", Guid.NewGuid().ToString() }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoOneShotSourceProofOptions.Parse([.. Arguments(), .. forbidden]));
        Assert.Throws<ArgumentException>(() =>
            VideoOneShotSourceProofOptions.Parse([]));
    }

    [Fact]
    public async Task Default_preview_does_not_even_connect_to_PostgreSQL()
    {
        var options = VideoOneShotSourceProofOptions.Parse(Arguments());
        using StringWriter output = new();
        Assert.Equal(0, await VideoOneShotSourceProofCommandRunner.RunAsync(options, output));
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement result = document.RootElement;
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.False(result.GetProperty("explicitLocalReadRequested").GetBoolean());
        Assert.Equal("not-run", result.GetProperty("status").GetString());
        Assert.False(result.GetProperty("originalBytesOpened").GetBoolean());
        Assert.False(result.GetProperty("oneDriveHydrationRequested").GetBoolean());
    }

    [Fact]
    public void Output_is_source_free_and_explicitly_cannot_authorize_video_processing()
    {
        using StringWriter report = new();
        VideoOneShotSourceProofCommandRunner.Write(
            report, true, VideoOneShotSourceProofResult.Success());
        string json = report.ToString();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement obj = document.RootElement;
        Assert.Equal("wi0188-one-shot-source-proof-v1",
            obj.GetProperty("protocol").GetString());
        Assert.True(obj.GetProperty("explicitLocalReadRequested").GetBoolean());
        Assert.True(obj.GetProperty("originalBytesOpened").GetBoolean());
        Assert.True(obj.GetProperty("revisionDigestMatched").GetBoolean());
        Assert.True(obj.GetProperty("sourceMetadataStable").GetBoolean());
        Assert.False(obj.GetProperty("originalWriteAccessRequested").GetBoolean());
        Assert.False(obj.GetProperty("oneDriveHydrationRequested").GetBoolean());
        Assert.False(obj.GetProperty("jobsEnqueued").GetBoolean());
        Assert.False(obj.GetProperty("originalPostTrialIntegrityVerified").GetBoolean());
        Assert.False(obj.GetProperty("realVideoExecutionAuthorized").GetBoolean());
        foreach (string forbidden in new[]
        {
            "sourcePath", "sourceKey", "fileName", "gps", "captureAtUtc",
            "private.mov", "connectionString", "password", "sha256"
        })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<ArgumentException>(() =>
            VideoOneShotSourceProofCommandRunner.Write(report, true,
                VideoOneShotSourceProofResult.Fail(null, @"C:\\Archive\\secret.mov")));
    }

    [Fact]
    public async Task Bounded_no_recall_file_digest_matches_current_bytes_and_denies_mutation()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "wi0188-video-sourceproof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] sourceBytes = Enumerable.Range(0, 25000)
            .Select(i => (byte)(i % 251)).ToArray();
        string video = Path.Combine(root, "test.mov");
        await File.WriteAllBytesAsync(video, sourceBytes);
        try
        {
            string sha = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
            Guid sourceId = Guid.NewGuid();
            var result = await VideoOneShotSourceProofCommandRunner.VerifyLocalFileAsync(
                sourceId, root, "test.mov", sha, sourceBytes.Length, CancellationToken.None);
            Assert.True(result.Verified);
            Assert.True(result.OriginalOpened);
            Assert.True(result.SourceMetadataStable);
            Assert.Equal("verified", result.ReasonCode);

            var mismatch = await VideoOneShotSourceProofCommandRunner.VerifyLocalFileAsync(
                sourceId, root, "test.mov", new string('f', 64),
                sourceBytes.Length, CancellationToken.None);
            Assert.False(mismatch.Verified);
            Assert.True(mismatch.OriginalOpened);
            Assert.True(mismatch.SourceMetadataStable);
            Assert.Equal("digest-mismatch", mismatch.ReasonCode);

            var size = await VideoOneShotSourceProofCommandRunner.VerifyLocalFileAsync(
                sourceId, root, "test.mov", sha,
                sourceBytes.Length + 1, CancellationToken.None);
            Assert.False(size.Verified);
            Assert.False(size.OriginalOpened);
            Assert.Equal("source-changed", size.ReasonCode);
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(video));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Digest_rule_rejects_unbounded_or_pathlike_values()
    {
        Assert.True(VideoOneShotSourceProofCommandRunner.IsApprovedDigest(new string('a', 64)));
        Assert.False(VideoOneShotSourceProofCommandRunner.IsApprovedDigest("abc"));
        Assert.False(VideoOneShotSourceProofCommandRunner.IsApprovedDigest(
            new string('G', 64)));
        Assert.False(VideoOneShotSourceProofCommandRunner.IsApprovedDigest(
            @"C:\\Archive\\private.mov"));
    }
}
