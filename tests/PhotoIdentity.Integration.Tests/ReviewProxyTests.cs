using OpenCvSharp;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewProxyTests
{
    [Fact]
    public async Task Renderer_is_repeatable_and_preserves_aspect_ratio_without_upscaling()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string source = Path.Combine(directory, "source.jpg");
            await WriteTestJpegAsync(source, 2400, 1200);
            ReviewProxyProfile profile = new("candidate-1600-q82", 1600, 82);
            OpenCvReviewProxyRenderer renderer = new();

            EncodedReviewProxy first = await renderer.RenderAsync(source, profile);
            EncodedReviewProxy second = await renderer.RenderAsync(source, profile);

            Assert.Equal(1600, first.Width);
            Assert.Equal(800, first.Height);
            Assert.Equal("image/jpeg", first.ContentType);
            Assert.Equal(first.Content, second.Content);

            string small = Path.Combine(directory, "small.jpg");
            await WriteTestJpegAsync(small, 640, 480);
            EncodedReviewProxy notUpscaled = await renderer.RenderAsync(small, profile);
            Assert.Equal(640, notUpscaled.Width);
            Assert.Equal(480, notUpscaled.Height);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Archive_proxy_measure_generates_multiple_candidates_and_reports_only_aggregates()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceRoot = Path.Combine(directory, "sample");
            string nested = Path.Combine(sourceRoot, "nested");
            string outputRoot = Path.Combine(directory, "proxies");
            Directory.CreateDirectory(nested);
            await WriteTestJpegAsync(Path.Combine(sourceRoot, "one.jpg"), 1800, 1200);
            await WriteTestJpegAsync(Path.Combine(nested, "two.jpg"), 2200, 1100);
            await WriteTestJpegAsync(Path.Combine(nested, "three.jpg"), 1200, 1800);

            StringWriter output = new();
            StringWriter error = new();
            int exitCode = await Program.RunAsync(
                [
                    "archive", "proxy", "measure",
                    "--source", sourceRoot,
                    "--output", outputRoot,
                    "--profile", "candidate-1600-q78:1600:78",
                    "--profile", "candidate-2048-q82:2048:82",
                ],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            string report = output.ToString();
            Assert.Contains("archive-proxy-measurement: complete", report, StringComparison.Ordinal);
            Assert.Contains("source-images: 3", report, StringComparison.Ordinal);
            Assert.Contains("profiles: 2", report, StringComparison.Ordinal);
            Assert.Contains("profile: candidate-1600-q78", report, StringComparison.Ordinal);
            Assert.Contains("profile: candidate-2048-q82", report, StringComparison.Ordinal);
            Assert.Contains("protocol=review-proxy-v1", report, StringComparison.Ordinal);
            Assert.Contains("mean-proxy-bytes:", report, StringComparison.Ordinal);
            Assert.Contains("median-proxy-bytes:", report, StringComparison.Ordinal);
            Assert.Contains("p95-proxy-bytes:", report, StringComparison.Ordinal);
            Assert.Contains("compression-ratio-source-to-proxy:", report, StringComparison.Ordinal);
            Assert.DoesNotContain("one.jpg", report, StringComparison.Ordinal);
            Assert.DoesNotContain("two.jpg", report, StringComparison.Ordinal);
            Assert.DoesNotContain("three.jpg", report, StringComparison.Ordinal);

            Assert.Equal(
                3,
                Directory.EnumerateFiles(
                    Path.Combine(outputRoot, "candidate-1600-q78"),
                    "*.jpg",
                    SearchOption.AllDirectories).Count());
            Assert.Equal(
                3,
                Directory.EnumerateFiles(
                    Path.Combine(outputRoot, "candidate-2048-q82"),
                    "*.jpg",
                    SearchOption.AllDirectories).Count());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Archive_proxy_measure_rejects_output_under_source_root()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceRoot = Path.Combine(directory, "sample");
            Directory.CreateDirectory(sourceRoot);
            await WriteTestJpegAsync(Path.Combine(sourceRoot, "one.jpg"), 800, 600);
            string outputRoot = Path.Combine(sourceRoot, "generated");

            StringWriter output = new();
            StringWriter error = new();
            int exitCode = await Program.RunAsync(
                [
                    "archive", "proxy", "measure",
                    "--source", sourceRoot,
                    "--output", outputRoot,
                    "--profile", "candidate-a:1600:78",
                    "--profile", "candidate-b:2048:82",
                ],
                output,
                error);

            Assert.Equal(2, exitCode);
            Assert.Contains("outside the source root", error.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(outputRoot));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task WriteTestJpegAsync(string path, int width, int height)
    {
        using Mat image = new(new Size(width, height), MatType.CV_8UC3, new Scalar(30, 90, 180));
        Cv2.Line(
            image,
            new Point(0, 0),
            new Point(width - 1, height - 1),
            new Scalar(220, 180, 40),
            thickness: Math.Max(1, width / 100));
        Cv2.ImEncode(
            ".jpg",
            image,
            out byte[] encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 94));
        await File.WriteAllBytesAsync(path, encoded);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
