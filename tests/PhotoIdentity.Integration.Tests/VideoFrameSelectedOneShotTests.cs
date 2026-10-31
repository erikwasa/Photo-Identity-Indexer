using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoFrameSelectedOneShotTests
{
    private static string[] Valid(string root) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0190_CONN",
        "--database", "photoidentity_main",
        "--proxy-job-id", "9d20dafa-282e-4240-914c-4cc5328185a4",
        "--cache-root", root,
        "--scratch-root", root,
        "--ffmpeg", Path.Combine(root, "ffmpeg.exe"),
        "--ffmpeg-sha256", new string('a', 64),
        "--clip-model", Path.Combine(root, "model.onnx"),
        "--clip-model-sha256", new string('b', 64),
        "--clip-vocab", Path.Combine(root, "vocab.json"),
        "--clip-merges", Path.Combine(root, "merges.txt"),
        "--ollama-sha256", new string('c', 64),
        "--max-frames", "2"
    ];

    [Fact]
    public void Default_is_preview_only_and_publication_requires_both_specific_acknowledgements()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "wi0190-test"));
        var options = VideoFrameSelectedOneShotOptions.Parse(Valid(root));
        Assert.False(options.Apply);
        Assert.False(options.AcknowledgeV38);
        Assert.False(options.AcknowledgeDisclosure);
        Assert.Equal(2, options.Frames);
        Assert.Equal(new string('b', 64), options.Producer.ExpectedClipSha256);
        Assert.Equal(new string('c', 64), options.Producer.ExpectedOllamaSha256);
        Assert.True(VideoFrameSelectedOneShotOptions.Parse(
            [.. Valid(root), "--apply",
            "--acknowledge-isolated-v38-rehearsal",
            "--acknowledge-frame-disclosure"]).Apply);

        foreach (string[] forbidden in new[]
        {
            new[] { "--apply" },
            new[] { "--apply", "--acknowledge-isolated-v38-rehearsal" },
            new[] { "--apply", "--acknowledge-frame-disclosure" },
            new[] { "--acknowledge-frame-disclosure" },
            new[] { "--source", @"C:\private\family.mov" },
            new[] { "--original-path", @"C:\private\family.mov" },
            new[] { "--online-only", "yes" },
            new[] { "--max-frames", "3" },
            new[] { "--max-frames", "0" },
            new[] { "--max-frames", "2" },
            new[] { "--ollama-sha256", new string('d', 64) },
            new[] { "--clip-model-sha256", "not-a-hash" },
            new[] { "--apply", "--apply" },
            new[] { "--acknowledge-isolated-v38-rehearsal",
                    "--acknowledge-frame-disclosure" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoFrameSelectedOneShotOptions.Parse(
                    [.. Valid(root), .. forbidden]));
    }

    [Fact]
    public void Selected_frame_scratch_does_not_accept_previous_synthetic_pilot_marker()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,
                VideoSyntheticProxyPilotCommandRunner.RootMarker),
                VideoSyntheticProxyPilotCommandRunner.MarkerText);
            Assert.Throws<InvalidOperationException>(() =>
                VideoFrameEvidenceScratchGuard.Validate(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
