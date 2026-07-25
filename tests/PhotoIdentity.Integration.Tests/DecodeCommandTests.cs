using System.Text.Json;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class DecodeCommandTests
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZlS8AAAAASUVORK5CYII=");

    [Fact]
    public async Task Decode_writes_normalised_png_and_privacy_safe_report()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string inputPath = Path.Combine(directory, "private-phone-photo.png");
            string outputPath = Path.Combine(directory, "normalised.png");
            string reportPath = Path.Combine(directory, "report.json");
            await File.WriteAllBytesAsync(inputPath, OnePixelPng);
            byte[] original = await File.ReadAllBytesAsync(inputPath);

            StringWriter output = new();
            StringWriter error = new();
            int exitCode = await PhotoIdentity.Cli.Program.RunAsync(
                [
                    "decode",
                    "--input", inputPath,
                    "--output", outputPath,
                    "--report", reportPath,
                ],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Empty(error.ToString());
            Assert.True(File.Exists(outputPath));
            Assert.Equal(original, await File.ReadAllBytesAsync(inputPath));

            byte[] encoded = await File.ReadAllBytesAsync(outputPath);
            Assert.True(encoded.AsSpan().StartsWith(PngSignature));

            string json = await File.ReadAllTextAsync(reportPath);
            Assert.False(json.Contains(inputPath, StringComparison.OrdinalIgnoreCase));

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.Equal("passed", root.GetProperty("result").GetString());
            Assert.True(root.GetProperty("inputUnchanged").GetBoolean());
            Assert.Equal("Bgr24", root.GetProperty("pixelFormat").GetString());
            Assert.Equal("normalised.png", root.GetProperty("outputFileName").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Decode_returns_specific_exit_code_for_unsupported_media()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string inputPath = Path.Combine(directory, "unsupported.gif");
            string outputPath = Path.Combine(directory, "normalised.png");
            await File.WriteAllBytesAsync(inputPath, "GIF89a"u8.ToArray());

            StringWriter output = new();
            StringWriter error = new();
            int exitCode = await PhotoIdentity.Cli.Program.RunAsync(
                ["decode", "--input", inputPath, "--output", outputPath],
                output,
                error);

            Assert.Equal(3, exitCode);
            Assert.Contains("unsupported-format", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"photoidentity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
