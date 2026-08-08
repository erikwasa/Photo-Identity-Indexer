using OpenCvSharp;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ReviewProxyScaleValidationTests
{
    [Fact]
    public async Task Archive_proxy_measure_allows_one_exact_profile_for_scale_validation()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string sourceRoot = Path.Combine(directory, "sample");
            string outputRoot = Path.Combine(directory, "proxies");
            Directory.CreateDirectory(sourceRoot);
            using (Mat image = new(new Size(1000, 750), MatType.CV_8UC3, new Scalar(40, 80, 120)))
            {
                Cv2.ImEncode(".jpg", image, out byte[] encoded);
                await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "one.jpg"), encoded);
            }

            StringWriter output = new();
            StringWriter error = new();
            int exitCode = await Program.RunAsync(
                [
                    "archive", "proxy", "measure",
                    "--source", sourceRoot,
                    "--output", outputRoot,
                    "--profile", "selected-profile:1600:82",
                ],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains("profiles: 1", output.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(outputRoot, "selected-profile", "one.jpg")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
